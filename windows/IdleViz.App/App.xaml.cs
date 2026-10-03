using System.Diagnostics.CodeAnalysis;
using IdleViz.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace IdleViz.App;

[SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "The app object lives as long as the process, and Quit disposes what it owns.")]
public partial class App : Application
{
    private readonly LaunchOptions _launchOptions;
    private readonly SettingsStore _settings = new(AppPaths.SettingsFile);
    private DispatcherQueue? _dispatcher;
    private HotkeyWindow? _hotkeyWindow;
    private VisualizerController? _visualizer;
    private TrayIcon? _trayIcon;
    private SettingsWindow? _settingsWindow;
    private SpotifyInfo? _spotify;
    private OverlayFeed? _overlay;
    private AudioPump? _audio;
    private PresetController? _presets;
    private AudioDelayController? _audioDelay;
#if DEBUG
    private DispatcherQueueTimer? _hangTimer;
    private DispatcherQueueTimer? _detectTimer;
#endif
    private IdleWatcher? _idle;
    private KeepAwake? _keepAwake;
    private PowerSource? _power;
    private DispatcherQueueTimer? _openAtLaunchTimer;
    private DispatcherQueueTimer? _firstReadingTimer;
    private TriggerSource? _waitingSource;
    private bool _pretendWarnings;
    private bool _probingCapture;

    public App(LaunchOptions launchOptions)
    {
        _launchOptions = launchOptions;
        InitializeComponent();
        // The flyout, the menu and settings are windows that come and go. Closing the last one must not end the app.
        DispatcherShutdownMode = DispatcherShutdownMode.OnExplicitShutdown;
        UnhandledException += (_, e) => Log.Info("app", $"Unhandled exception: {e.Exception}");
    }

    /// <summary>The open hotkey as stored, or null if it was cleared.</summary>
    internal Hotkey? OpenHotkey => HotkeySetting.Read(_settings);

    /// <summary>The settings file, for the rows that are stored as they are shown.</summary>
    internal SettingsStore Settings => _settings;

    /// <summary>The problems that turn the tray icon yellow and get a row in the flyout.</summary>
    internal WarningList Warnings { get; } = new();

    /// <summary>False on a desktop PC, where the battery rows are hidden.</summary>
    internal bool HasBattery => _power?.HasBattery ?? false;

    /// <summary>The preset controls. Set once the app has launched, before any window can show them.</summary>
    internal PresetController Presets => _presets ?? throw new InvalidOperationException("The app hasn't launched yet.");

    /// <summary>The audio delay for the current speakers or headphones. Set once the app has launched.</summary>
    internal AudioDelayController AudioDelay => _audioDelay ?? throw new InvalidOperationException("The app hasn't launched yet.");

    /// <summary>False while Windows refuses the stored hotkey because another app has it.</summary>
    internal bool HotkeyRegistered { get; private set; } = true;

    // The app has no main window: the tray icon is all there is until something opens.
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        Log.Info("app", $"Started, version {typeof(App).Assembly.GetName().Version}");

        // The debug switches are ignored in Release builds.
#if DEBUG
        var debug = _launchOptions;
#else
        var debug = new LaunchOptions();
#endif

        // Created now and kept hidden, so the first open is instant.
        _visualizer = new VisualizerController(
            _dispatcher, dismissEnabled: !debug.NoDismiss, () => DisplayPlan.For(MultiDisplaySettings.Read(_settings), Displays.Current()));

        _hotkeyWindow = new HotkeyWindow();
        _hotkeyWindow.HotkeyPressed += () => OpenVisualizer(TriggerSource.Hotkey);
        _hotkeyWindow.ThemeChanged += () => _trayIcon?.RefreshIcon();
        ApplyHotkey();

        _trayIcon = new TrayIcon(this);

        Warnings.Changed += LogWarnings;
        _pretendWarnings = debug.PretendWarning;
        _spotify = new SpotifyInfo(_dispatcher);
        if (_pretendWarnings)
        {
            Warnings.Report(WarningKind.AudioCapture, "Pretend: the capture request was refused. (0x80070005)");
            Warnings.Report(WarningKind.NowPlaying, "Pretend: the media controls did not answer. (0x800706BA)");
        }
        else
        {
            _spotify.ProblemChanged += problem => Warnings.Report(WarningKind.NowPlaying, problem);
            // Spotify was started: see whether its sound can be captured, before the window needs it.
            _spotify.SessionFound += ProbeCapture;
            _visualizer.Opened += _spotify.Recheck;
        }

        _spotify.Start();

        _overlay = new OverlayFeed(_spotify, _visualizer.Page, _dispatcher);
        _visualizer.Opened += _overlay.Start;
        _visualizer.Closed += _overlay.Stop;
        // The capture and the status checks only run while the window is open.
        var spotify = _spotify;
        var page = _visualizer.Page;
        // Before the page loads, so its first state carries the stored controls.
        var converter = new MilkConverter(() => page.Environment, page.Parent);
        _presets = new PresetController(page, _settings, new PresetLibrary(_dispatcher, AppPaths.PresetsFolder, converter));
        bool SpotifyIsPlaying() => spotify.Tracker.Current?.State == SpotifyPlayerState.Playing;
        var audio = _audio = new AudioPump(_dispatcher, SpotifyIsPlaying, page.SendAudioFrame);
        if (!_pretendWarnings)
        {
            var dispatcher = _dispatcher;
            audio.CaptureProblemChanged += problem => dispatcher.TryEnqueue(() => Warnings.Report(WarningKind.AudioCapture, problem));
        }

        _audioDelay = new AudioDelayController(_dispatcher, _settings, audio, SpotifyIsPlaying, spotify.Pause, spotify.Play);
        // The same delay holds back the audio frames and the progress bar.
        _audioDelay.DelayChanged += delay =>
        {
            audio.Delay = delay;
            page.SendAudioDelay(delay);
        };
        audio.Delay = _audioDelay.Delay;
        page.SendAudioDelay(_audioDelay.Delay);
        var presets = _presets;
        _visualizer.Keys = () => VisualizerKeys.Read(_settings);
        _visualizer.KeyPressed += presets.Perform;
        _visualizer.MirrorHung += presets.Library.MarkHung;
        page.SendBrightness(BrightnessSetting.Value(_settings));
        page.SendOverlayEnabled(OverlaySetting.Value(_settings));
        _visualizer.Opened += _audio.Start;
        _visualizer.Opened += page.StartStatusChecks;
        _visualizer.Closed += _audio.Stop;
        _visualizer.Closed += page.StopStatusChecks;
        _visualizer.Start();

        var power = _power = new PowerSource(_hotkeyWindow, debug.PretendBattery);
        var visualizer = _visualizer;
        var idle = _idle = new IdleWatcher(
            _dispatcher, () => TimingSettings.Read(_settings).IdleTimeout(power.OnBattery), _hotkeyWindow, () => OpenVisualizer(TriggerSource.Idle));
        var keepAwake = _keepAwake = new KeepAwake(
            _dispatcher,
            () => TimingSettings.Read(_settings).KeepAwakeLimit(power.OnBattery),
            () =>
            {
                // The PC is still idle, so the idle trigger has to wait for new input.
                idle.WaitForInput();
                visualizer.Close(CloseReason.KeepAwakeLimit);
            });
        _visualizer.Opened += keepAwake.Start;
        // The PC may sleep again as soon as the fade-out starts.
        _visualizer.Closing += _ => keepAwake.Stop();
        _settings.Changed += (_, e) =>
        {
            if (e.Key is IdleTimeoutSetting.Key or KeepAwakeSetting.Key
                or BatteryTimesSetting.EnabledKey or BatteryTimesSetting.IdleTimeoutKey or BatteryTimesSetting.KeepAwakeKey)
            {
                idle.TimeoutMayHaveChanged();
                keepAwake.LimitMayHaveChanged();
            }
            else if (e.Key == BrightnessSetting.Key)
            {
                page.SendBrightness(BrightnessSetting.Value(_settings));
            }
            else if (e.Key == OverlaySetting.Key)
            {
                page.SendOverlayEnabled(OverlaySetting.Value(_settings));
            }
            else if (MultiDisplaySettings.IsKey(e.Key))
            {
                // Windows for other displays are made now, so the next open finds their pages loaded.
                visualizer.Prepare();
            }
        };
        power.Changed += () =>
        {
            Log.Info("power", $"Now on {(power.OnBattery ? "battery" : "mains power")}");
            idle.TimeoutMayHaveChanged();
            keepAwake.LimitMayHaveChanged();
        };
        CloseWhenTheDisplayMayHaveChanged(_hotkeyWindow, visualizer);
        _idle.Start();
        Program.Relaunched += options => _dispatcher.TryEnqueue(() =>
        {
            try
            {
                OnRelaunched(options);
            }
            catch (Exception error)
            {
                Log.Info("app", $"Relaunch failed: {error}");
                throw;
            }
        });

        if (_launchOptions.Command == UrlCommand.Open)
        {
            OpenVisualizer(TriggerSource.UrlScheme);
        }

        ShowDebugWindows(debug);
#if DEBUG
        if (debug.HangPage)
        {
            _hangTimer = _dispatcher.CreateTimer();
            _hangTimer.Interval = TimeSpan.FromSeconds(5);
            _hangTimer.IsRepeating = false;
            _hangTimer.Tick += (_, _) => page.Hang();
            _visualizer.Opened += _hangTimer.Start;
            _visualizer.Closed += _hangTimer.Stop;
        }

        if (debug.DetectDelay)
        {
            _detectTimer = _dispatcher.CreateTimer();
            _detectTimer.Interval = TimeSpan.FromSeconds(5);
            _detectTimer.IsRepeating = false;
            _detectTimer.Tick += (_, _) => _audioDelay.Detect();
            _detectTimer.Start();
        }
#endif

        if (debug.OpenAtLaunch)
        {
            // In a field: a timer nothing refers to is collected, and then never fires.
            _openAtLaunchTimer = _dispatcher.CreateTimer();
            _openAtLaunchTimer.Interval = TimeSpan.FromSeconds(3);
            _openAtLaunchTimer.IsRepeating = false;
            _openAtLaunchTimer.Tick += (_, _) => OpenVisualizer(TriggerSource.Settings);
            _openAtLaunchTimer.Start();
        }
    }

    // No fade for these: the screen the window is on may be off or gone before a fade could finish.
    private static void CloseWhenTheDisplayMayHaveChanged(HotkeyWindow window, VisualizerController visualizer)
    {
        window.Suspending += () =>
        {
            Log.Info("power", "The PC is going to sleep");
            visualizer.Close(CloseReason.DisplayChanged);
        };
        // Windows sends the notice for more than the displays (a wallpaper change, the taskbar), so the
        // layout is compared with the last one seen.
        var displays = new DisplayTracker(Displays.Current());
        Log.Info("display", $"Displays: {Displays.Describe(displays.Layout)}");
        window.DisplaysMayHaveChanged += () =>
        {
            if (displays.ShouldClose(Displays.Current()))
            {
                Log.Info("display", $"The displays changed: {Displays.Describe(displays.Layout)}");
                visualizer.Close(CloseReason.DisplayChanged);
                visualizer.Prepare();
            }
        };
    }

    /// <summary>Opens the visualizer if the open rules allow it. A refused manual trigger flashes the tray icon.</summary>
    internal void OpenVisualizer(TriggerSource source)
    {
        if (_visualizer is null || _spotify is null)
        {
            return;
        }

        // Already open: the window decides. In the no-dismiss mode a second trigger closes it.
        if (_visualizer.IsOpen)
        {
            _visualizer.Open(source);
            return;
        }

        var tracker = _spotify.Tracker;
        switch (OpenRules.Refusal(tracker, DateTimeOffset.Now))
        {
            case OpenRefusal.NotKnownYet:
                WaitForFirstReading(source);
                return;
            case { } refusal:
                Refuse(source, refusal);
                return;
        }

        if (tracker.Current is null)
        {
            Log.Info("open", "Spotify is between two tracks; opening anyway");
        }

        _visualizer.Open(source);
    }

    // The app has only just started and Windows hasn't said what Spotify is doing yet.
    private void WaitForFirstReading(TriggerSource source)
    {
        // A manual trigger during the wait makes it manual, so a refusal still flashes the icon.
        if (_waitingSource is null || source.IsManual())
        {
            _waitingSource = source;
        }

        if (_firstReadingTimer is not null || _dispatcher is null || _spotify is null)
        {
            return;
        }

        Log.Info("open", $"Waiting for Spotify's state before opening via {source}");
        _firstReadingTimer = _dispatcher.CreateTimer();
        _firstReadingTimer.Interval = TimeSpan.FromSeconds(OpenRules.FirstReadingWaitSeconds);
        _firstReadingTimer.IsRepeating = false;
        _firstReadingTimer.Tick += (_, _) =>
        {
            if (StopWaiting() is { } waiting)
            {
                Refuse(waiting, OpenRefusal.NotKnownYet);
            }
        };
        _spotify.Tracker.Changed += OnFirstReading;
        _firstReadingTimer.Start();
    }

    private void OnFirstReading(NowPlaying? item)
    {
        if (StopWaiting() is { } waiting)
        {
            OpenVisualizer(waiting);
        }
    }

    /// <summary>Ends a wait for the first reading and returns the trigger that was waiting, if any.</summary>
    private TriggerSource? StopWaiting()
    {
        var waiting = _waitingSource;
        _waitingSource = null;
        _firstReadingTimer?.Stop();
        _firstReadingTimer = null;
        if (_spotify is not null)
        {
            _spotify.Tracker.Changed -= OnFirstReading;
        }

        return waiting;
    }

    private void Refuse(TriggerSource source, OpenRefusal refusal)
    {
        var reason = refusal switch
        {
            OpenRefusal.SpotifyNotRunning => "Spotify isn't running, or has played nothing since it started",
            OpenRefusal.NoTrack => "Spotify has no track",
            _ => $"Windows hasn't said what Spotify is doing after {OpenRules.FirstReadingWaitSeconds} s",
        };
        Log.Info("open", $"Not opening via {source}: {reason}");
        // A refused idle trigger does nothing visible.
        if (source.IsManual())
        {
            _trayIcon?.Flash();
        }
    }

    /// <summary>Checks both warnings again. The results arrive a moment later through <see cref="Warnings"/>.</summary>
    internal void RecheckWarnings()
    {
        if (_pretendWarnings)
        {
            return;
        }

        _spotify?.Recheck();
        ProbeCapture();
    }

    /// <summary>Opens the details of a warning, as a dialog on the settings window.</summary>
    internal void ShowWarning(Warning warning)
    {
        ShowSettings();
        _settingsWindow?.ShowWarning(warning);
    }

    // Asks Windows for a capture of Spotify without starting it. Off the UI thread: it can take a moment.
    private void ProbeCapture()
    {
        if (_probingCapture || _dispatcher is not { } dispatcher)
        {
            return;
        }

        _probingCapture = true;
        Task.Run(SpotifyCapture.Probe).ContinueWith(
            probe => dispatcher.TryEnqueue(() =>
            {
                _probingCapture = false;
                Warnings.Report(WarningKind.AudioCapture, probe.IsFaulted ? probe.Exception.InnerException?.Message ?? "The check failed." : probe.Result);
            }),
            TaskScheduler.Default);
    }

    private void LogWarnings()
    {
        var showing = Warnings.Current;
        Log.Info("warning", showing.Count == 0
            ? "No problems"
            : string.Join("; ", showing.Select(warning => $"{warning.Title}: {warning.Details}")));
    }

    internal void ShowSettings()
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(this);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }

        _settingsWindow.BringToFront();
    }

    /// <summary>Stores the hotkey and registers it. Returns false if Windows refuses it.</summary>
    internal bool SetOpenHotkey(Hotkey? hotkey)
    {
        HotkeySetting.Write(_settings, hotkey);
        return ApplyHotkey();
    }

    /// <summary>Lets the hotkey go while the recorder listens, so pressing it there doesn't open the visualizer.</summary>
    internal void SuspendHotkey() => _hotkeyWindow?.Register(null);

    internal bool ApplyHotkey()
    {
        var hotkey = OpenHotkey;
        HotkeyRegistered = _hotkeyWindow?.Register(hotkey) ?? true;
        if (!HotkeyRegistered)
        {
            Log.Info("hotkey", $"Windows refused {hotkey?.Label}: another app has it.");
        }

        return HotkeyRegistered;
    }

    internal void Quit()
    {
        Log.Info("app", "Exit");
        _settingsWindow?.Close();
        StopWaiting();
        _idle?.Dispose();
        _keepAwake?.Dispose();
        _audioDelay?.Dispose();
        _audio?.Dispose();
        _presets?.Library.Dispose();
        _spotify?.Dispose();
        _visualizer?.Dispose();
        _hotkeyWindow?.Dispose();
        // Without this the icon stays in the tray until the mouse moves over it.
        _trayIcon?.Dispose();
        _trayIcon = null;
        Exit();
    }

    private void ShowDebugWindows(LaunchOptions debug)
    {
        if (debug.ShowSettings)
        {
            ShowSettings();
        }

        if (debug.ShowFlyout)
        {
            _trayIcon?.ToggleFlyout();
        }

        if (debug.ShowMenu)
        {
            _trayIcon?.ShowMenu();
        }
    }

    private void OnRelaunched(LaunchOptions options)
    {
        if (options.Command == UrlCommand.Open)
        {
            OpenVisualizer(TriggerSource.UrlScheme);
        }
#if DEBUG
        else if (options.ShowSettings || options.ShowFlyout || options.ShowMenu)
        {
            ShowDebugWindows(options);
        }
#endif
        else
        {
            // Started again from the Start menu: there is no window to bring forward, so show settings.
            ShowSettings();
        }
    }
}
