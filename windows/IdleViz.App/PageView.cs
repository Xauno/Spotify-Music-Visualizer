using System.Diagnostics;
using IdleViz.Core;
using Microsoft.UI.Dispatching;
using Microsoft.Web.WebView2.Core;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace IdleViz.App;

/// <summary>
/// The web page (visualizer, dim layer and overlay) in WebView2, on a visualizer window. There is
/// one main page; with more than one display mirrored, each further window has a page of its own
/// that is sent everything the main page is sent and is held on the main page's preset. The
/// app talks to it only by running script; the page has no way to call the app: web messages are
/// off, there are no host objects, and every permission is denied. Ported from <c>PageView.swift</c>
/// and <c>AppSchemeHandler.swift</c>.
/// </summary>
internal sealed class PageView : IDisposable
{
    // Ask every 50 ms, for up to 10 s, whether the page's scripts have run.
    private const int ReadyCheckMilliseconds = 50;
    private const int ReadyCheckAttempts = 200;
    private const string ReadyCheck = "['nowPlaying', 'setPresetSettings', 'setCustomPresets'].every((name) => typeof window[name] === 'function')";

    // Audio frames the page hasn't taken yet. More than a couple means it's busy, so newer frames are dropped.
    private const int MaxFramesInFlight = 2;

    // How often the pages on other displays are checked against the main page's preset.
    private const int MirrorCheckMilliseconds = 250;

    private readonly HWND _parent;
    private readonly DispatcherQueue _dispatcher;
    private readonly string _logName;
    private readonly List<PageView> _mirrors = [];
    private readonly DispatcherQueueTimer _mirrorTimer;
    private readonly string _root;
    private readonly string _presetsRoot;
    private readonly DispatcherQueueTimer _readyTimer;
    private readonly DispatcherQueueTimer _statusTimer;
    private readonly PageWatchdog _watchdog = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly TaskCompletionSource<CoreWebView2Environment?> _environmentReady = new();
    private CoreWebView2Environment? _environment;
    private CoreWebView2Controller? _controller;

    // The controller's web view, kept for as long as the controller. The page's event handlers are
    // registered on it, and if nothing refers to it the garbage collector frees it while WebView2
    // still calls those handlers: the app crashed on the first request, or the page never got ready.
    private CoreWebView2? _web;
    private bool _visible;
    private bool _loaded;
    private int _readyAttempts;
    private bool _disposed;

    // Counts page loads, so an answer from an earlier page is ignored.
    private int _loadId;
    private int _framesInFlight;
    private PageStatus? _lastStatus;

    // The page's own counts at the first answer after the window opened, for the line logged when it closes.
    private long _framesAtOpen = -1;
    private long _audioFramesAtOpen;

    /// <summary>The latest <c>nowPlaying</c> call, replayed whenever the page (re)loads.</summary>
    private string _nowPlayingScript = OverlayPayload.Script(null);

    /// <summary>The latest list of custom presets and hung presets, replayed the same way.</summary>
    private string _customPresetsScript = CustomPresetPayload.Empty.Script;

    /// <summary>The latest preset controls, replayed the same way.</summary>
    private string _presetSettingsScript = new PresetSettings().Script;

    /// <summary>The latest audio delay, replayed the same way. The progress bar needs it to show the position you hear.</summary>
    private string _audioDelayScript = AudioDelaySetting.Script(0);
    private string _brightnessScript = BrightnessSetting.Script(BrightnessSetting.DefaultValue);
    private string _overlayEnabledScript = OverlaySetting.Script(true);

    /// <summary>Which displays the window covers, replayed the same way. Empty until the window first opens.</summary>
    private string _layoutScript = string.Empty;

    // The preset controls as last sent, and the preset the pages on other displays are held on.
    private PresetSettings? _presetSettings;
    private string? _mirroredPreset;
    private bool _checkingMirrors;

    /// <param name="parent">The visualizer window. The page fills it.</param>
    /// <param name="dispatcher">The UI thread's queue.</param>
    /// <param name="logName">What the page's log lines start with.</param>
    public PageView(HWND parent, DispatcherQueue dispatcher, string logName = "page")
    {
        _parent = parent;
        _dispatcher = dispatcher;
        _logName = logName;
        _mirrorTimer = dispatcher.CreateTimer();
        _mirrorTimer.Interval = TimeSpan.FromMilliseconds(MirrorCheckMilliseconds);
        _mirrorTimer.Tick += (_, _) => SyncMirrors();
        _root = Path.Combine(AppContext.BaseDirectory, "web");
        _presetsRoot = AppPaths.PresetsFolder;
        _readyTimer = dispatcher.CreateTimer();
        _readyTimer.Interval = TimeSpan.FromMilliseconds(ReadyCheckMilliseconds);
        _readyTimer.Tick += (_, _) => CheckReady();
        _statusTimer = dispatcher.CreateTimer();
        _statusTimer.Interval = TimeSpan.FromSeconds(1);
        _statusTimer.Tick += (_, _) => CheckStatus();
    }

    /// <summary>Called with the presets the page reports as failed, each time that list changes.</summary>
    public event Action<IReadOnlyList<PresetFailure>>? FailuresChanged;

    /// <summary>Called with the page's preset list each time the page has loaded.</summary>
    public event Action<IReadOnlyList<PresetInfo>>? PresetsLoaded;

    /// <summary>Called when a different preset comes on screen.</summary>
    public event Action<string>? PresetShown;

    /// <summary>Called with the preset that was on screen when the page stopped answering.</summary>
    public event Action<string>? Hung;

    /// <summary>
    /// Called after this page ended every renderer in the environment to get rid of its own stuck
    /// one. The other pages lost theirs too and have to be made again.
    /// </summary>
    public event Action? RenderersEnded;

    /// <summary>The WebView2 environment, once it exists; null if WebView2 couldn't start. The converter page shares it.</summary>
    public Task<CoreWebView2Environment?> Environment => _environmentReady.Task;

    /// <summary>The visualizer window, which the page (and the hidden converter page) live in.</summary>
    public HWND Parent => _parent;

    /// <summary>Starts WebView2 and loads the page. The window may still be hidden.</summary>
    public async void Start()
    {
        try
        {
            var options = new CoreWebView2EnvironmentOptions
            {
                // No autoplay prompt for the AudioContext the visualizer makes.
                AdditionalBrowserArguments = "--autoplay-policy=no-user-gesture-required",
            };
            var dataFolder = Path.Combine(AppPaths.LocalData, "WebView2");
            _environment = await CoreWebView2Environment.CreateWithOptionsAsync(string.Empty, dataFolder, options);
            _environmentReady.TrySetResult(_environment);
            if (_disposed)
            {
                return;
            }

            Log.Info(_logName, $"WebView2 {_environment.BrowserVersionString}");
            await CreateController();
        }
        catch (Exception error)
        {
            Log.Info(_logName, $"Can't start the page; the visualizer stays black. Is the WebView2 runtime installed? {error.Message}");
            _environmentReady.TrySetResult(null);
        }
    }

    /// <summary>
    /// Adds the page of another display. It gets what this page has been sent so far, and from now
    /// on everything this page is sent.
    /// </summary>
    public void AddMirror(PageView mirror)
    {
        mirror._nowPlayingScript = _nowPlayingScript;
        mirror._customPresetsScript = _customPresetsScript;
        mirror._presetSettingsScript = MirrorPresetScript ?? _presetSettingsScript;
        mirror._audioDelayScript = _audioDelayScript;
        mirror._brightnessScript = _brightnessScript;
        mirror._overlayEnabledScript = _overlayEnabledScript;
        _mirrors.Add(mirror);
    }

    public void RemoveMirror(PageView mirror) => _mirrors.Remove(mirror);

    /// <summary>
    /// Asks this page which preset is on screen and, if it changed, has the pages on other displays
    /// blend to it. Runs a few times a second while the window is open, and when it opens.
    /// </summary>
    public async void SyncMirrors()
    {
        if (_mirrors.Count == 0 || _checkingMirrors)
        {
            return;
        }

        _checkingMirrors = true;
        try
        {
            if (await CurrentPreset() is { } preset && preset != _mirroredPreset)
            {
                _mirroredPreset = preset;
                SendMirrorPreset();
            }
        }
        finally
        {
            _checkingMirrors = false;
        }
    }

    /// <summary>Tells the page which displays its window covers, each time the window opens.</summary>
    public void SendLayout(PlannedWindow place)
    {
        _layoutScript = place.Script;
        Run(_layoutScript);
    }

    /// <summary>Makes the page again after another page ended this one's renderer.</summary>
    public async void Recreate()
    {
        if (_disposed || _environment is null)
        {
            return;
        }

        try
        {
            // The count starts over, or this page would take its lost renderer for a stuck one.
            if (_statusTimer.IsRunning)
            {
                _watchdog.Start(Now);
            }

            await ReplaceController();
        }
        catch (Exception error)
        {
            Log.Info(_logName, $"Can't make a new page: {error.Message}");
        }
    }

    /// <summary>Shows a Spotify item on the overlay, or <c>null</c> for no track.</summary>
    public void Show(OverlayPayload? payload)
    {
        _nowPlayingScript = OverlayPayload.Script(payload);
        Run(_nowPlayingScript);
        foreach (var mirror in _mirrors)
        {
            mirror.Show(payload);
        }
    }

    /// <summary>Hands the bundled plugins and the custom presets folder to the page, and asks for the new preset list.</summary>
    public void SendCustomPresets(CustomPresetPayload payload)
    {
        _customPresetsScript = payload.Script;
        if (_loaded)
        {
            Run(_customPresetsScript);
            FetchPresetList();
        }

        foreach (var mirror in _mirrors)
        {
            mirror.SendCustomPresets(payload);
        }
    }

    /// <summary>Hands the preset controls to the page, which applies them at once.</summary>
    public void SendPresetSettings(PresetSettings settings)
    {
        _presetSettings = settings;
        _presetSettingsScript = settings.Script;
        Run(_presetSettingsScript);
        SendMirrorPreset();
    }

    /// <summary>Tells the page how far the speakers lag behind Spotify, in seconds.</summary>
    public void SendAudioDelay(double seconds)
    {
        _audioDelayScript = AudioDelaySetting.Script(seconds);
        Run(_audioDelayScript);
        foreach (var mirror in _mirrors)
        {
            mirror.SendAudioDelay(seconds);
        }
    }

    /// <summary>Sets how much of the visualizer shows through the black dim layer, 0.5 to 1.</summary>
    public void SendBrightness(double value)
    {
        _brightnessScript = BrightnessSetting.Script(value);
        Run(_brightnessScript);
        foreach (var mirror in _mirrors)
        {
            mirror.SendBrightness(value);
        }
    }

    /// <summary>Shows or hides the Spotify overlay as a whole.</summary>
    public void SendOverlayEnabled(bool enabled)
    {
        _overlayEnabledScript = OverlaySetting.Script(enabled);
        Run(_overlayEnabledScript);
        foreach (var mirror in _mirrors)
        {
            mirror.SendOverlayEnabled(enabled);
        }
    }

    /// <summary>Asks the page for the next preset. It does nothing outside Shuffle. Pages on other displays follow.</summary>
    public void SkipPreset()
    {
        Run(VisualizerKeys.SkipScript);
        SyncMirrors();
    }

    /// <summary>Shows the heart that confirms the like key.</summary>
    public void ShowLike(bool liked)
    {
        Run(VisualizerKeys.LikeScript(liked));
        foreach (var mirror in _mirrors)
        {
            mirror.ShowLike(liked);
        }
    }

    /// <summary>The preset on screen right now, or null if the page doesn't say. The once-a-second status can be a moment behind.</summary>
    public async Task<string?> CurrentPreset()
    {
        if (!_loaded || _controller is null)
        {
            return null;
        }

        try
        {
            return PageStatus.FromReply(await _controller.CoreWebView2.ExecuteScriptAsync("window.idlevizStatus?.()"))?.Preset;
        }
        catch (Exception error)
        {
            Log.Info(_logName, $"Asking the page for its preset failed: {error.Message}");
            return null;
        }
    }

    /// <summary>Hands one audio frame's script to the page. Frames are dropped, not queued, while the page is busy or loading.</summary>
    public async void SendAudioFrame(string script)
    {
        foreach (var mirror in _mirrors)
        {
            mirror.SendAudioFrame(script);
        }

        if (!_loaded || _controller is null || _framesInFlight >= MaxFramesInFlight)
        {
            return;
        }

        var load = _loadId;
        _framesInFlight++;
        try
        {
            await _controller.CoreWebView2.ExecuteScriptAsync(script);
        }
        catch (Exception)
        {
            // The page went away mid-call (reload or crash); the next frame goes to the new one.
        }
        finally
        {
            if (load == _loadId)
            {
                _framesInFlight--;
            }
        }
    }

    /// <summary>
    /// While the window is open, asks the page once a second how it's doing. A page that stops
    /// answering (a preset or plugin stuck in a loop) is replaced.
    /// </summary>
    public void StartStatusChecks()
    {
        if (_statusTimer.IsRunning)
        {
            return;
        }

        _watchdog.Start(Now);
        _framesAtOpen = -1;
        _statusTimer.Start();
        _mirrorTimer.Start();
        SyncMirrors();
        foreach (var mirror in _mirrors)
        {
            mirror.StartStatusChecks();
        }
    }

    public void StopStatusChecks()
    {
        foreach (var mirror in _mirrors)
        {
            mirror.StopStatusChecks();
        }

        _mirrorTimer.Stop();
        _statusTimer.Stop();
        _watchdog.Stop();
        if (_framesAtOpen >= 0 && _lastStatus is { } status)
        {
            Log.Info(_logName, $"While open: the page drew {status.Frames - _framesAtOpen} frames and got {status.AudioFrames - _audioFramesAtOpen} audio frames");
        }

        _framesAtOpen = -1;
    }

#if DEBUG
    /// <summary>Debug builds only: makes the page's renderer loop forever, as a broken preset would.</summary>
    public void Hang()
    {
        Log.Info(_logName, "Hanging the page on purpose (--hang-page)");
        Run("setTimeout(() => { for (;;) {} }, 0)");
    }
#endif

    /// <summary>
    /// The page renders only while the window is showing. Hidden, it is suspended, so it costs
    /// nothing between opens.
    /// </summary>
    public void SetVisible(bool visible)
    {
        _visible = visible;
        if (_controller is null)
        {
            return;
        }

        if (visible)
        {
            Resize();
        }

        _controller.IsVisible = visible;
        if (!visible)
        {
            Suspend();
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _readyTimer.Stop();
        _statusTimer.Stop();
        _mirrorTimer.Stop();
        _controller?.Close();
        _controller = null;
        _web = null;
    }

    private async Task CreateController()
    {
        if (_environment is null)
        {
            return;
        }

        var reference = CoreWebView2ControllerWindowReference.CreateFromWindowHandle((ulong)(nint)_parent);
        var options = _environment.CreateCoreWebView2ControllerOptions();
        // Like the Mac's non-persistent store: nothing the page or a plugin stores outlives the page.
        options.IsInPrivateModeEnabled = true;
        var controller = await _environment.CreateCoreWebView2ControllerAsync(reference, options);
        if (_disposed)
        {
            controller.Close();
            return;
        }

        _controller = controller;
        _web = controller.CoreWebView2;
        controller.DefaultBackgroundColor = Windows.UI.Color.FromArgb(255, 0, 0, 0);
        Resize();
        controller.IsVisible = _visible;

        var web = _web;
        var settings = web.Settings;
        settings.IsWebMessageEnabled = false;
        settings.AreHostObjectsAllowed = false;
        settings.AreDefaultScriptDialogsEnabled = false;
        settings.AreDefaultContextMenusEnabled = false;
        settings.IsStatusBarEnabled = false;
        settings.IsZoomControlEnabled = false;
        settings.AreBrowserAcceleratorKeysEnabled = false;
        settings.IsBuiltInErrorPageEnabled = false;
        settings.IsGeneralAutofillEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;
#if DEBUG
        settings.AreDevToolsEnabled = true;
#else
        settings.AreDevToolsEnabled = false;
#endif

        web.AddWebResourceRequestedFilter($"{AppAddresses.AppOrigin}/*", CoreWebView2WebResourceContext.All);
        web.AddWebResourceRequestedFilter($"{AppAddresses.PresetsOrigin}/*", CoreWebView2WebResourceContext.All);
        web.WebResourceRequested += (_, e) => WebServer.Respond(_environment, e, _root, _presetsRoot);
        web.NavigationStarting += (_, e) => e.Cancel = !AllowNavigation(e.Uri, "page");
        web.FrameNavigationStarting += (_, e) => e.Cancel = !AllowNavigation(e.Uri, "frame");
        web.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            Log.Info(_logName, $"Blocked a new window for {e.Uri}");
        };
        web.PermissionRequested += (_, e) =>
        {
            // The visuals only ever see Spotify's audio, which reaches the page from the app.
            e.State = CoreWebView2PermissionState.Deny;
            Log.Info(_logName, $"Denied the permission {e.PermissionKind}");
        };
        web.DownloadStarting += (_, e) => e.Cancel = true;
        web.NavigationCompleted += (_, e) =>
        {
            if (!e.IsSuccess)
            {
                Log.Info(_logName, $"The page didn't load: {e.WebErrorStatus}");
                return;
            }

            _readyAttempts = 0;
            _readyTimer.Start();
        };
        web.ProcessFailed += OnProcessFailed;
        Load();
    }

    private void Load()
    {
        ResetPageState();
        _readyTimer.Stop();
        _controller?.CoreWebView2.Navigate(AppAddresses.PageUrl);
    }

    private bool AllowNavigation(string url, string kind)
    {
        // A frame starts out as about:blank before it loads its own address.
        if (AppAddresses.IsAppPage(url) || (kind == "frame" && url == "about:blank"))
        {
            return true;
        }

        Log.Info(_logName, $"Blocked {kind} navigation to {url}");
        return false;
    }

    // WebView2 can report the navigation as complete before the page's modules have run, and a call
    // made then is lost (seen on the Mac). So ask whether its functions exist, and only then send state.
    private async void CheckReady()
    {
        if (_controller is null || _loaded)
        {
            _readyTimer.Stop();
            return;
        }

        if (++_readyAttempts > ReadyCheckAttempts)
        {
            _readyTimer.Stop();
            Log.Info(_logName, $"The page's scripts didn't start within {ReadyCheckAttempts * ReadyCheckMilliseconds / 1000} s");
            return;
        }

        try
        {
            if (await _controller.CoreWebView2.ExecuteScriptAsync(ReadyCheck) == "true" && !_loaded)
            {
                _readyTimer.Stop();
                _loaded = true;
                Log.Info(_logName, "Ready");
                Run(_customPresetsScript);
                Run(_presetSettingsScript);
                Run(_audioDelayScript);
                Run(_brightnessScript);
                Run(_overlayEnabledScript);
                Run(_layoutScript);
                Run(_nowPlayingScript);
                FetchPresetList();
                // A main page that was made again may have come up on another preset.
                SyncMirrors();
            }
        }
        catch (Exception error)
        {
            Log.Info(_logName, $"Asking the page whether it is ready failed: {error.Message}");
        }
    }

    private async void FetchPresetList()
    {
        if (_controller is null)
        {
            return;
        }

        var load = _loadId;
        try
        {
            var reply = await _controller.CoreWebView2.ExecuteScriptAsync("window.idlevizPresets?.()");
            // A page replaced in the meantime answers for nothing; the new one is asked when it is ready.
            if (load != _loadId)
            {
                return;
            }

            var presets = PresetInfo.List(reply);
            Log.Info(_logName, $"{presets.Count} presets");
            PresetsLoaded?.Invoke(presets);
        }
        catch (Exception error)
        {
            Log.Info(_logName, $"Asking the page for its presets failed: {error.Message}");
        }
    }

    // The main page's settings, held on the preset it shows. Null until that preset is known:
    // a page on another display then shuffles by itself for a moment.
    private string? MirrorPresetScript =>
        _presetSettings is { } settings && _mirroredPreset is { } preset ? settings.FollowScript(preset) : null;

    private void SendMirrorPreset()
    {
        var script = MirrorPresetScript ?? _presetSettingsScript;
        foreach (var mirror in _mirrors)
        {
            mirror._presetSettingsScript = script;
            mirror.Run(script);
        }
    }

    private async void Run(string script)
    {
        if (!_loaded || _controller is null || script.Length == 0)
        {
            return;
        }

        try
        {
            await _controller.CoreWebView2.ExecuteScriptAsync(script);
        }
        catch (Exception error)
        {
            Log.Info(_logName, $"A call to the page failed: {error.Message}");
        }
    }

    private void OnProcessFailed(CoreWebView2 sender, CoreWebView2ProcessFailedEventArgs e)
    {
        Log.Info(_logName, $"WebView2 process failed: {e.ProcessFailedKind} ({e.Reason}, exit code {e.ExitCode})");
        _dispatcher.TryEnqueue(async () =>
        {
            if (_disposed || !IsCurrent(sender))
            {
                return;
            }

            if (e.ProcessFailedKind == CoreWebView2ProcessFailedKind.BrowserProcessExited)
            {
                // Everything is gone: start over with a new controller.
                _controller?.Close();
                _controller = null;
                _web = null;
                ResetPageState();
                try
                {
                    await CreateController();
                }
                catch (Exception error)
                {
                    Log.Info(_logName, $"Can't restart the page: {error.Message}");
                }
            }
            else if (e.ProcessFailedKind is CoreWebView2ProcessFailedKind.RenderProcessExited or CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)
            {
                // Load again, and the state is sent again once it is ready.
                Load();
            }
        });
    }

    private double Now => _clock.Elapsed.TotalSeconds;

    private bool IsCurrent(CoreWebView2 sender)
    {
        try
        {
            return _controller?.CoreWebView2 == sender;
        }
        catch (Exception)
        {
            // The controller can't be asked once its browser process is gone; that report is for it.
            return true;
        }
    }

    private void ResetPageState()
    {
        _loaded = false;
        _loadId++;
        _framesInFlight = 0;
        _lastStatus = null;
        _framesAtOpen = -1;
    }

    private async void CheckStatus()
    {
        if (_controller is null)
        {
            return;
        }

        if (_watchdog.ShouldReload(Now) && _environment is not null)
        {
            var preset = _lastStatus?.Preset;
            Log.Info(_logName, $"The page stopped answering; replacing it (last preset: {preset ?? "none"})");
            // Whatever was on screen is the likely cause. Keep it out, or the new page would hang on it too.
            // The library answers at once with a new list, which the new page gets when it is ready.
            if (preset is not null)
            {
                Hung?.Invoke(preset);
            }

            await ReplaceStuckPage();
            return;
        }

        if (!_loaded)
        {
            return;
        }

        var load = _loadId;
        string reply;
        try
        {
            reply = await _controller.CoreWebView2.ExecuteScriptAsync("window.idlevizStatus?.()");
        }
        catch (Exception)
        {
            return;
        }

        if (load != _loadId || PageStatus.FromReply(reply) is not { } status)
        {
            return;
        }

        _watchdog.Replied(Now);
        if (status.Preset != _lastStatus?.Preset)
        {
            Log.Info(_logName, $"Preset: {status.Preset ?? "none"}");
            if (status.Preset is { } shown)
            {
                PresetShown?.Invoke(shown);
            }
        }

        if (!status.SameFailures(_lastStatus) && (_lastStatus is not null || status.Failed.Count > 0))
        {
            var known = (_lastStatus?.Failed ?? []).Select(f => f.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var failure in status.Failed.Where(f => !known.Contains(f.Id)))
            {
                Log.Info(_logName, $"Failed to load {failure.Id}: {failure.Error}");
            }

            FailuresChanged?.Invoke(status.Failed);
        }

        if (_framesAtOpen < 0)
        {
            _framesAtOpen = status.Frames;
            _audioFramesAtOpen = status.AudioFrames;
        }

        _lastStatus = status;
    }

    /// <summary>
    /// A renderer stuck in a JavaScript loop can't be reloaded, and ending it doesn't make WebView2
    /// report a crash (runtime 124): navigating that WebView again crashed the app. So, as on the Mac,
    /// the stuck renderer is ended (or it would keep a core busy) and a new WebView takes the old one's place.
    /// </summary>
    private async Task ReplaceStuckPage()
    {
        var ended = 0;
        try
        {
            // The environment belongs to IdleViz alone, so every renderer in it is the page's or a plugin frame's.
            foreach (var info in _environment?.GetProcessInfos() ?? [])
            {
                if (info.Kind != CoreWebView2ProcessKind.Renderer)
                {
                    continue;
                }

                try
                {
                    using var process = Process.GetProcessById(info.ProcessId);
                    process.Kill();
                    ended++;
                }
                catch (Exception error) when (error is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    // Already gone.
                }
            }
        }
        catch (Exception error)
        {
            Log.Info(_logName, $"Listing the page's processes failed: {error.Message}");
        }

        Log.Info(_logName, $"Ended {ended} renderer process(es)");
        // Before anything is awaited, so every page has let go of its old web view by the time
        // WebView2 reports the ended renderers.
        var replaced = ReplaceController();
        RenderersEnded?.Invoke();
        try
        {
            await replaced;
        }
        catch (Exception error)
        {
            Log.Info(_logName, $"Can't make a new page: {error.Message}");
        }
    }

    private Task ReplaceController()
    {
        ResetPageState();
        _readyTimer.Stop();
        var old = _controller;
        _controller = null;
        _web = null;
        old?.Close();
        return CreateController();
    }

    private void Resize()
    {
        if (_controller is null)
        {
            return;
        }

        PInvoke.GetClientRect(_parent, out var rect);
        _controller.Bounds = new Windows.Foundation.Rect(0, 0, rect.right - rect.left, rect.bottom - rect.top);
    }

    private async void Suspend()
    {
        try
        {
            if (_controller is { IsVisible: false } controller)
            {
                await controller.CoreWebView2.TrySuspendAsync();
            }
        }
        catch (Exception error)
        {
            // Suspending is a saving, not a must: a page that wasn't suspended is only hidden.
            Log.Info(_logName, $"Couldn't suspend the page: {error.Message}");
        }
    }
}
