using System.Diagnostics;
using IdleViz.Core;
using Microsoft.UI.Dispatching;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace IdleViz.App;

/// <summary>
/// Opens and closes the visualizer: fades, focus, the cursor and the dismiss watcher. There is one
/// main window with the main page. With more than one display mirrored there is a further window,
/// with a page of its own, for each other display; extended, the main window covers them all.
/// </summary>
internal sealed class VisualizerController : IDisposable
{
    private enum State
    {
        Closed,
        Open,
        Closing,
    }

    private readonly VisualizerWindow _window = new();
    private readonly List<(VisualizerWindow Window, PageView Page)> _mirrors = [];
    private readonly DispatcherQueue _dispatcher;
    private readonly Func<DisplayPlan> _plan;
    private readonly PageView _page;
    private readonly DispatcherQueueTimer _fadeTimer;
    private readonly DispatcherQueueTimer _cursorTimer;
    private readonly Stopwatch _fadeClock = new();
    private readonly bool _dismissEnabled;
    private State _state = State.Closed;

    // Whether input closes the window that is open now: the debug switch and the "Close on input" setting.
    private bool _closesOnInput = true;
    private bool _started;
    private DismissWatcher? _dismissWatcher;
    private HWND _previousWindow;
    private double _fadeFrom;
    private double _fadeTo;
    private double _fadeSeconds;
    private double _opacity;

    /// <param name="dispatcher">The UI thread's queue.</param>
    /// <param name="dismissEnabled">False keeps the window open on input, for inspecting it (Debug builds).</param>
    /// <param name="plan">The windows to show for the settings and the displays as they are now.</param>
    public VisualizerController(DispatcherQueue dispatcher, bool dismissEnabled, Func<DisplayPlan> plan)
    {
        _dispatcher = dispatcher;
        _dismissEnabled = dismissEnabled;
        _plan = plan;
        _page = new PageView(_window.Handle, dispatcher);
        _page.RenderersEnded += () => RecreatePagesExcept(_page);
        _fadeTimer = dispatcher.CreateTimer();
        _fadeTimer.Interval = TimeSpan.FromMilliseconds(8);
        _fadeTimer.Tick += (_, _) => OnFadeTick();
        _cursorTimer = dispatcher.CreateTimer();
        _cursorTimer.Interval = TimeSpan.FromMilliseconds(50);
        _cursorTimer.Tick += (_, _) =>
        {
            foreach (var window in AllWindows)
            {
                window.HideRestingCursor();
            }
        };
    }

    /// <summary>False again as soon as it starts to fade out.</summary>
    public bool IsOpen => _state == State.Open;

    /// <summary>The main page. It loads at launch, so the first open shows it at once.</summary>
    public PageView Page => _page;

    /// <summary>Raised with the preset a page of another display was showing when it stopped answering.</summary>
    public event Action<string>? MirrorHung;

    private IEnumerable<VisualizerWindow> AllWindows => [_window, .. _mirrors.Select(mirror => mirror.Window)];

    /// <summary>The like and skip keys. Read each time the window opens.</summary>
    public Func<VisualizerKeys> Keys { get; set; } = () => new VisualizerKeys(null, null);

    /// <summary>Raised when the like or the skip key is pressed while the window is open.</summary>
    public event Action<VisualizerAction>? KeyPressed;

    /// <summary>Raised when the window has opened.</summary>
    public event Action? Opened;

    /// <summary>Raised when the window starts to fade out and the PC is handed back, with the reason.</summary>
    public event Action<CloseReason>? Closing;

    /// <summary>Raised when the window is gone after its fade-out.</summary>
    public event Action? Closed;

    /// <summary>Loads the page into the hidden window, and the pages of other displays into theirs.</summary>
    public void Start()
    {
        _started = true;
        _page.Start();
        Prepare();
    }

    /// <summary>
    /// Makes or removes the hidden windows of the other displays, so that an open finds their pages
    /// loaded. Call it when the display settings or the displays change; it waits while the
    /// visualizer is open, and the next open catches up.
    /// </summary>
    public DisplayPlan? Prepare()
    {
        if (!_started || _state != State.Closed)
        {
            return null;
        }

        var plan = _plan();
        while (_mirrors.Count > plan.Windows.Count - 1)
        {
            var (window, page) = _mirrors[^1];
            _mirrors.RemoveAt(_mirrors.Count - 1);
            _page.RemoveMirror(page);
            page.Dispose();
            window.Dispose();
        }

        while (_mirrors.Count < plan.Windows.Count - 1)
        {
            var window = new VisualizerWindow();
            var page = new PageView(window.Handle, _dispatcher, $"page {_mirrors.Count + 2}");
            page.Hung += preset => MirrorHung?.Invoke(preset);
            page.RenderersEnded += () => RecreatePagesExcept(page);
            _page.AddMirror(page);
            _mirrors.Add((window, page));
            page.Start();
            Log.Info("window", $"Made a window for display {_mirrors.Count + 1}");
        }

        return plan;
    }

    public void Open(TriggerSource source)
    {
        if (_state == State.Open)
        {
            // Nothing else can close it while input is ignored, so a second manual trigger does.
            if (!_closesOnInput && source.IsManual())
            {
                Close(CloseReason.Input);
            }

            return;
        }

        // Triggered again while it fades out: finish that close first, so everything starts clean.
        if (_state == State.Closing)
        {
            FinishClosing();
        }

        if (Prepare() is not { } plan)
        {
            return;
        }

        _closesOnInput = _dismissEnabled && plan.CloseOnInput;
        _previousWindow = PInvoke.GetForegroundWindow();
        SetOpacity(0);
        IReadOnlyList<(VisualizerWindow Window, PageView Page)> all = [(_window, _page), .. _mirrors];
        for (var index = 0; index < all.Count; index++)
        {
            var (window, page) = all[index];
            window.SetClickThrough(false);
            window.HidesCursor = _closesOnInput;
            window.Show(plan.Windows[index]);
            page.SetVisible(true);
            page.SendLayout(plan.Windows[index]);
        }

        // With input ignored the keyboard stays with the app that has it.
        var focused = plan.CloseOnInput && _window.TakeFocus();
        _state = State.Open;
        _cursorTimer.Start();
        Fade(to: 1, seconds: VisualizerFade.OpenSeconds);
        var focus = !plan.CloseOnInput ? "input is ignored" : focused ? "has focus" : "focus refused";
        Log.Info("window", $"Opened via {source} on {all.Count} window(s), {plan.Windows[0].Regions.Count} display(s) in the first: {focus}");
        Opened?.Invoke();

        if (_closesOnInput)
        {
            _dismissWatcher = new DismissWatcher(_dispatcher, Keys(), action => KeyPressed?.Invoke(action), () => Close(CloseReason.Input));
            _dismissWatcher.Start();
        }
    }

    /// <summary>Hands the PC back at once (cursor, focus, clicks) and fades the window out on top of it.</summary>
    public void Close(CloseReason reason)
    {
        // A close that can't wait cuts a fade-out short.
        if (_state == State.Closing && reason.FadeSeconds() == 0)
        {
            FinishClosing();
        }

        if (_state != State.Open)
        {
            return;
        }

        _state = State.Closing;
        Log.Info("window", $"Closing: {reason}");
        _dismissWatcher?.Dispose();
        _dismissWatcher = null;
        foreach (var window in AllWindows)
        {
            window.SetClickThrough(true);
            window.HidesCursor = false;
        }

        _cursorTimer.Stop();

        // Only if focus is still here: someone may have switched apps while it was open (no-dismiss mode).
        if (PInvoke.GetForegroundWindow() == _window.Handle && PInvoke.IsWindow(_previousWindow))
        {
            PInvoke.SetForegroundWindow(_previousWindow);
        }

        _previousWindow = HWND.Null;
        Closing?.Invoke(reason);

        if (reason.FadeSeconds() > 0)
        {
            Fade(to: 0, seconds: reason.FadeSeconds());
        }
        else
        {
            FinishClosing();
        }
    }

    public void Dispose()
    {
        _fadeTimer.Stop();
        _cursorTimer.Stop();
        _dismissWatcher?.Dispose();
        foreach (var (window, page) in _mirrors)
        {
            page.Dispose();
            window.Dispose();
        }

        _page.Dispose();
        _window.Dispose();
    }

    // One page got rid of its stuck renderer by ending all of them, so the others need new ones too.
    private void RecreatePagesExcept(PageView stuck)
    {
        foreach (var page in new[] { _page }.Concat(_mirrors.Select(mirror => mirror.Page)).Where(page => page != stuck))
        {
            page.Recreate();
        }
    }

    private void FinishClosing()
    {
        if (_state != State.Closing)
        {
            return;
        }

        _fadeTimer.Stop();
        _window.Hide();
        _page.SetVisible(false);
        foreach (var (window, page) in _mirrors)
        {
            window.Hide();
            page.SetVisible(false);
        }

        _state = State.Closed;
        Closed?.Invoke();
    }

    private void Fade(double to, double seconds)
    {
        _fadeFrom = _opacity;
        _fadeTo = to;
        _fadeSeconds = seconds;
        _fadeClock.Restart();
        _fadeTimer.Start();
    }

    private void OnFadeTick()
    {
        var progress = Math.Min(1, _fadeClock.Elapsed.TotalSeconds / _fadeSeconds);
        // Ease in and out, like the Mac's window fade.
        var eased = progress < 0.5 ? 2 * progress * progress : 1 - (Math.Pow((-2 * progress) + 2, 2) / 2);
        SetOpacity(_fadeFrom + ((_fadeTo - _fadeFrom) * eased));
        if (progress < 1)
        {
            return;
        }

        _fadeTimer.Stop();
        FinishClosing();
    }

    private void SetOpacity(double opacity)
    {
        _opacity = opacity;
        foreach (var window in AllWindows)
        {
            window.SetOpacity(opacity);
        }
    }
}
