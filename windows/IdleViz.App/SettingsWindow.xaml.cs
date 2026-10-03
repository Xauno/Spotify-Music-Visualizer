using System.Globalization;
using IdleViz.Core;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace IdleViz.App;

/// <summary>
/// The settings window: small, portrait, fixed size, one page that scrolls. Closing it only closes
/// the window; the app keeps running in the tray.
/// </summary>
public sealed partial class SettingsWindow : Window
{
    private const double WidthInPixels = 440;
    private const double HeightInPixels = 680;

    // Segoe Fluent Icons: Heart and HeartFill.
    private const int HeartGlyph = 0xE006;
    private const int FilledHeartGlyph = 0xE00B;

    private readonly App _app;

    // Set while the rows are being filled in from the settings, so the pickers' own events aren't taken as changes.
    // It starts set: a slider reports a change as soon as its range is, while the window is still being built.
    private bool _showing = true;

    // What the Visualizer picker was last filled with: the page's list and a stored preset missing from it.
    private IReadOnlyList<PresetInfo>? _singleChoices;
    private string? _singleExtra;

    // The ids behind the Visualizer picker's names, in the same order.
    private List<string> _singleIds = [];

    // What the Failed to load list shows, so it is only rebuilt when that changes.
    private IReadOnlyList<PresetFailure> _shownFailures = [];

    public SettingsWindow(App app)
    {
        _app = app;
        InitializeComponent();
        _showing = false;

        Title = "IdleViz Settings";
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "IdleViz.ico"));
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBar);

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
        }

        var scale = PInvoke.GetDpiForWindow(Handle) / 96.0;
        AppWindow.Resize(new SizeInt32((int)Math.Round(WidthInPixels * scale), (int)Math.Round(HeightInPixels * scale)));

        ShowTimes();
        Recorder.Attach(_app);
        Recorder.HotkeyChanged += ShowHotkeyState;
        ShowHotkeyState();

        ShowKeys();
        ShowDisplayOptions();
        MultiDisplayWarning.Message = MultiDisplaySettings.GpuWarning;
        ShowDisplays();
        // Read again whenever the window comes forward: the entry can be switched off in Windows too,
        // and a display may have been plugged in.
        Activated += (_, e) =>
        {
            if (e.WindowActivationState != WindowActivationState.Deactivated)
            {
                ShowStartup();
                ShowDisplays();
            }
        };
        ShowStartup();

        FillPresetChoices();
        Presets.Changed += ShowPresets;
        ShowPresets();

        ShowMicrophones();
        AudioDelay.Changed += ShowDelay;
        ShowDelay();
        Closed += (_, _) =>
        {
            Presets.Changed -= ShowPresets;
            AudioDelay.Changed -= ShowDelay;
            // The window can be closed with the test's dialog still open.
            AudioDelay.StopTest();
        };
    }

    private PresetController Presets => _app.Presets;

    private AudioDelayController AudioDelay => _app.AudioDelay;

    private HWND Handle => new(WinRT.Interop.WindowNative.GetWindowHandle(this));

    /// <summary>Shows the window, or brings it forward if it is already open.</summary>
    /// <summary>Shows a warning's details: what it means, the error text, and a way to the log.</summary>
    public async void ShowWarning(Warning warning)
    {
        // A window that was only just made has nothing to hang a dialog on until its content is loaded.
        if (Content is FrameworkElement { IsLoaded: false } content)
        {
            content.Loaded += (_, _) => ShowWarning(warning);
            return;
        }

        var text = new StackPanel { Spacing = 12 };
        text.Children.Add(new TextBlock { Text = warning.Explanation, TextWrapping = TextWrapping.Wrap });
        text.Children.Add(new TextBlock
        {
            Text = warning.Details,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
        });
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = warning.Title,
            Content = text,
            PrimaryButtonText = "Open log folder",
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close,
        };
        try
        {
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                Log.OpenFolder();
            }
        }
        catch (Exception error) when (error is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            // Another dialog is already open on this window.
            Log.Info("settings", $"Couldn't show the warning's details: {error.Message}");
        }
    }

    public void BringToFront()
    {
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
        }

        Activate();
        PInvoke.SetForegroundWindow(Handle);
    }

    private void ShowHotkeyState()
    {
        if (_app.HotkeyRegistered)
        {
            HotkeyCard.ClearValue(CommunityToolkit.WinUI.Controls.SettingsCard.DescriptionProperty);
        }
        else
        {
            HotkeyCard.Description = "Another app is already using this shortcut. Pick a different one.";
        }
    }

    /// <summary>Fills the idle, keep-awake and battery rows in from the settings.</summary>
    private void ShowTimes()
    {
        var times = TimingSettings.Read(_app.Settings);
        _showing = true;
        try
        {
            ShowMinutes(IdlePicker, IdleChoices, times.IdleMinutes, IdleLabel);
            ShowMinutes(KeepAwakePicker, KeepAwakeSetting.Choices, times.KeepAwakeMinutes, KeepAwakeSetting.Label);
            ShowMinutes(BatteryIdlePicker, IdleChoices, times.BatteryIdleMinutes, IdleLabel);
            ShowMinutes(BatteryKeepAwakePicker, KeepAwakeSetting.Choices, times.BatteryKeepAwakeMinutes, KeepAwakeSetting.Label);
            BatterySwitch.IsOn = times.UseBatteryTimes;
        }
        finally
        {
            _showing = false;
        }

        BatteryCard.Visibility = _app.HasBattery ? Visibility.Visible : Visibility.Collapsed;
        BatteryIdleCard.Visibility = BatteryKeepAwakeCard.Visibility =
            _app.HasBattery && times.UseBatteryTimes ? Visibility.Visible : Visibility.Collapsed;
    }

    // 5, 10, 15 and 30 min, then Off, as on the Mac.
    private static IReadOnlyList<int> IdleChoices { get; } = [.. IdleTimeoutSetting.Choices, 0];

    private static string IdleLabel(int minutes) => minutes > 0 ? $"{minutes} min" : "Off";

    // A stored value that isn't a choice gets its own entry.
    private static void ShowMinutes(ComboBox picker, IReadOnlyList<int> choices, int current, Func<int, string> label)
    {
        var minutes = choices.ToList();
        if (!minutes.Contains(current))
        {
            minutes.Insert(0, current);
        }

        if (!minutes.SequenceEqual(picker.Items.OfType<ComboBoxItem>().Select(item => (int)item.Tag)))
        {
            picker.Items.Clear();
            foreach (var value in minutes)
            {
                picker.Items.Add(new ComboBoxItem { Content = label(value), Tag = value });
            }
        }

        Select(picker, current);
    }

    private void ChangeMinutes(ComboBox picker, string key)
    {
        if (!_showing && picker.SelectedItem is ComboBoxItem { Tag: int minutes })
        {
            _app.Settings.SetInt(key, minutes);
            ShowTimes();
        }
    }

    private void OnIdleChanged(object sender, SelectionChangedEventArgs e) => ChangeMinutes(IdlePicker, IdleTimeoutSetting.Key);

    private void OnKeepAwakeChanged(object sender, SelectionChangedEventArgs e) => ChangeMinutes(KeepAwakePicker, KeepAwakeSetting.Key);

    private void OnBatteryIdleChanged(object sender, SelectionChangedEventArgs e) => ChangeMinutes(BatteryIdlePicker, BatteryTimesSetting.IdleTimeoutKey);

    private void OnBatteryKeepAwakeChanged(object sender, SelectionChangedEventArgs e) =>
        ChangeMinutes(BatteryKeepAwakePicker, BatteryTimesSetting.KeepAwakeKey);

    private void OnBatteryTimesToggled(object sender, RoutedEventArgs e)
    {
        if (_showing)
        {
            return;
        }

        var settings = _app.Settings;
        if (BatterySwitch.IsOn)
        {
            // The first time it is turned on, the battery rows start as copies of the rows above.
            var times = TimingSettings.Read(settings);
            if (settings.GetInt(BatteryTimesSetting.IdleTimeoutKey) is null)
            {
                settings.SetInt(BatteryTimesSetting.IdleTimeoutKey, times.IdleMinutes);
            }

            if (settings.GetInt(BatteryTimesSetting.KeepAwakeKey) is null)
            {
                settings.SetInt(BatteryTimesSetting.KeepAwakeKey, times.KeepAwakeMinutes);
            }
        }

        settings.SetBool(BatteryTimesSetting.EnabledKey, BatterySwitch.IsOn);
        ShowTimes();
    }

    // Each picker leaves out the key the other one uses.
    private void ShowKeys()
    {
        var keys = VisualizerKeys.Read(_app.Settings);
        _showing = true;
        try
        {
            FillKeys(LikeKeyPicker, keys.Like, taken: keys.Skip);
            FillKeys(SkipKeyPicker, keys.Skip, taken: keys.Like);
        }
        finally
        {
            _showing = false;
        }
    }

    private static void FillKeys(ComboBox picker, ushort? current, ushort? taken)
    {
        var choices = VisualizerKey.Choices.Where(key => key.Code != taken).ToList();
        int[] codes = [VisualizerKeys.Off, .. choices.Select(key => (int)key.Code)];

        // Only the other picker's list changes with a choice. The one being used keeps its items:
        // emptying a picker from inside its own selection event takes the app down.
        if (!codes.SequenceEqual(picker.Items.OfType<ComboBoxItem>().Select(item => (int)item.Tag)))
        {
            picker.Items.Clear();
            picker.Items.Add(new ComboBoxItem { Content = "Off", Tag = VisualizerKeys.Off });
            foreach (var key in choices)
            {
                picker.Items.Add(new ComboBoxItem { Content = key.Label, Tag = (int)key.Code });
            }
        }

        Select(picker, current is { } code ? code : VisualizerKeys.Off);
    }

    private void ChangeKey(ComboBox picker, string key)
    {
        if (!_showing && picker.SelectedItem is ComboBoxItem { Tag: int code })
        {
            _app.Settings.SetInt(key, code);
            ShowKeys();
        }
    }

    private void OnLikeKeyChanged(object sender, SelectionChangedEventArgs e) => ChangeKey(LikeKeyPicker, VisualizerKeys.LikeKey);

    private void OnSkipKeyChanged(object sender, SelectionChangedEventArgs e) => ChangeKey(SkipKeyPicker, VisualizerKeys.SkipKey);

    private void ShowStartup()
    {
        var state = RunAtStartup.State;
        _showing = true;
        try
        {
            StartupSwitch.IsOn = state == StartupState.On;
        }
        finally
        {
            _showing = false;
        }

        if (state == StartupState.DisabledInWindows)
        {
            var link = new HyperlinkButton { Content = "Turned off in Windows. Open Startup apps\u2026", Padding = new Thickness(0) };
            link.Click += async (_, _) => await Windows.System.Launcher.LaunchUriAsync(StartupEntry.WindowsSettings);
            StartupCard.Description = link;
        }
        else
        {
            StartupCard.ClearValue(CommunityToolkit.WinUI.Controls.SettingsCard.DescriptionProperty);
        }
    }

    private void OnStartupToggled(object sender, RoutedEventArgs e)
    {
        if (!_showing)
        {
            RunAtStartup.Set(StartupSwitch.IsOn);
            ShowStartup();
        }
    }

    private void ShowDisplayOptions()
    {
        var brightness = BrightnessSetting.Value(_app.Settings);
        _showing = true;
        try
        {
            OverlaySwitch.IsOn = OverlaySetting.Value(_app.Settings);
            BrightnessSlider.Value = brightness;
        }
        finally
        {
            _showing = false;
        }

        BrightnessLabel.Text = BrightnessSetting.Label(brightness);
    }

    private void OnOverlayToggled(object sender, RoutedEventArgs e)
    {
        if (!_showing)
        {
            _app.Settings.SetBool(OverlaySetting.Key, OverlaySwitch.IsOn);
        }
    }

    private void OnBrightnessChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_showing)
        {
            return;
        }

        var brightness = BrightnessSetting.Normalized(e.NewValue);
        _app.Settings.SetDouble(BrightnessSetting.Key, brightness);
        BrightnessLabel.Text = BrightnessSetting.Label(brightness);
    }

    private void OnOpenNowClick(object sender, RoutedEventArgs e) => _app.OpenVisualizer(TriggerSource.Settings);

    /// <summary>Fills the Displays rows in from the settings and the displays connected right now.</summary>
    private void ShowDisplays()
    {
        var settings = MultiDisplaySettings.Read(_app.Settings);
        var displays = Displays.Current();
        var connected = displays.Select(display => (display.Name, DisplayLabel.For(display))).ToList();

        // A stored display that is unplugged right now stays in the list, so the choice isn't lost.
        IEnumerable<(string, string)> Missing(string? name) =>
            name is null || displays.Any(display => display.Name == name) ? [] : [(name, $"{DisplayLabel.Brief(name)} (not connected)")];

        var overlayIsDisplay = settings.OverlayDisplay is not (MultiDisplaySettings.OverlayOnMain or MultiDisplaySettings.OverlayOnAll);
        _showing = true;
        try
        {
            Fill(MainDisplayPicker, [(string.Empty, "Windows primary"), .. connected, .. Missing(settings.MainDisplay)], settings.MainDisplay ?? string.Empty);
            MultiDisplaySwitch.IsOn = settings.Enabled;
            Fill(
                PlacementPicker,
                [
                    (MultiDisplaySettings.PlacementName(DisplayPlacement.Mirror), "Same on each display"),
                    (MultiDisplaySettings.PlacementName(DisplayPlacement.Extend), "Extend across displays"),
                ],
                MultiDisplaySettings.PlacementName(settings.Placement));
            CloseOnInputSwitch.IsOn = settings.CloseOnInput;
            Fill(
                OverlayDisplayPicker,
                [
                    (MultiDisplaySettings.OverlayOnMain, "Main display"),
                    .. connected,
                    .. Missing(overlayIsDisplay ? settings.OverlayDisplay : null),
                    (MultiDisplaySettings.OverlayOnAll, "All displays"),
                ],
                settings.OverlayDisplay);
        }
        finally
        {
            _showing = false;
        }

        // Every display but the main one, each with a tick.
        var others = displays.Count == 0 ? [] : displays.Where(display => display != settings.Main(displays)).ToList();
        OtherDisplaysMenu.Items.Clear();
        foreach (var display in others)
        {
            var item = new ToggleMenuFlyoutItem { Text = DisplayLabel.For(display), Tag = display.Name, IsChecked = settings.Covers(display) };
            item.Click += OnOtherDisplayClick;
            OtherDisplaysMenu.Items.Add(item);
        }

        var chosen = others.Count(settings.Covers);
        OtherDisplaysButton.Content = others.Count == 0 ? "None connected" : chosen == others.Count ? "All" : chosen == 0 ? "None" : $"{chosen} of {others.Count}";

        MultiDisplayWarning.IsOpen = settings.Enabled;
        PlacementCard.IsEnabled = CloseOnInputCard.IsEnabled = OverlayDisplayCard.IsEnabled = settings.Enabled;
        OtherDisplaysCard.IsEnabled = settings.Enabled && others.Count > 0;
    }

    // A picker keeps its items unless they changed: emptying one from inside its own selection event takes the app down.
    private static void Fill(ComboBox picker, IReadOnlyList<(string Tag, string Label)> choices, string selected)
    {
        var shown = picker.Items.OfType<ComboBoxItem>().Select(item => ((string)item.Tag, (string)item.Content));
        if (!choices.SequenceEqual(shown))
        {
            picker.Items.Clear();
            foreach (var (tag, label) in choices)
            {
                picker.Items.Add(new ComboBoxItem { Content = label, Tag = tag });
            }
        }

        Select(picker, selected);
    }

    private void OnMainDisplayChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_showing || MainDisplayPicker.SelectedItem is not ComboBoxItem { Tag: string name })
        {
            return;
        }

        if (name.Length == 0)
        {
            _app.Settings.Remove(MultiDisplaySettings.MainDisplayKey);
        }
        else
        {
            _app.Settings.SetString(MultiDisplaySettings.MainDisplayKey, name);
        }

        ShowDisplays();
    }

    private void OnMultiDisplayToggled(object sender, RoutedEventArgs e)
    {
        if (!_showing)
        {
            _app.Settings.SetBool(MultiDisplaySettings.EnabledKey, MultiDisplaySwitch.IsOn);
            ShowDisplays();
        }
    }

    private void OnOtherDisplayClick(object sender, RoutedEventArgs e)
    {
        var items = OtherDisplaysMenu.Items.OfType<ToggleMenuFlyoutItem>().ToList();
        if (items.All(item => item.IsChecked))
        {
            // All of them, including a display that is plugged in later.
            _app.Settings.Remove(MultiDisplaySettings.OtherDisplaysKey);
        }
        else
        {
            _app.Settings.SetStringList(MultiDisplaySettings.OtherDisplaysKey, items.Where(item => item.IsChecked).Select(item => (string)item.Tag));
        }

        // Not from inside the menu's own click: the menu is rebuilt.
        DispatcherQueue.TryEnqueue(ShowDisplays);
    }

    private void OnPlacementChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_showing && PlacementPicker.SelectedItem is ComboBoxItem { Tag: string placement })
        {
            _app.Settings.SetString(MultiDisplaySettings.PlacementKey, placement);
        }
    }

    private void OnCloseOnInputToggled(object sender, RoutedEventArgs e)
    {
        if (!_showing)
        {
            _app.Settings.SetBool(MultiDisplaySettings.CloseOnInputKey, CloseOnInputSwitch.IsOn);
        }
    }

    private void OnOverlayDisplayChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_showing && OverlayDisplayPicker.SelectedItem is ComboBoxItem { Tag: string display })
        {
            _app.Settings.SetString(MultiDisplaySettings.OverlayDisplayKey, display);
        }
    }

    /// <summary>Fills the audio delay rows in. Runs again on every change: the slider, Detect delay, the test, another device.</summary>
    private void ShowDelay()
    {
        var delay = AudioDelay;
        _showing = true;
        try
        {
            DelaySlider.Value = delay.Delay;
        }
        finally
        {
            _showing = false;
        }

        DelayLabel.Text = AudioDelaySetting.Label(delay.Delay);
        DelayCard.Description = new TextBlock { Text = $"For {delay.DeviceName}", MaxLines = 1, TextTrimming = TextTrimming.CharacterEllipsis };

        DetectButton.Content = delay.Detecting ? "Listening…" : "Detect";
        DetectButton.IsEnabled = !delay.Detecting && !delay.Testing;
        TestButton.IsEnabled = !delay.Detecting && !delay.Testing;
        if (delay.MicrophoneDenied)
        {
            var link = new HyperlinkButton { Content = "Microphone access is off. Open Settings…", Padding = new Thickness(0) };
            link.Click += async (_, _) => await Windows.System.Launcher.LaunchUriAsync(AudioDelayController.MicrophoneSettings);
            DetectCard.Description = link;
        }
        else
        {
            DetectCard.Description = new TextBlock { Text = delay.Hint ?? "Listens with the mic for a few seconds", TextWrapping = TextWrapping.Wrap };
        }
    }

    private void OnDelayChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (!_showing)
        {
            AudioDelay.Delay = e.NewValue;
        }
    }

    private void OnDetectClick(object sender, RoutedEventArgs e) => AudioDelay.Detect();

    private async void OnTestClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ManualDelayDialog(AudioDelay) { XamlRoot = Content.XamlRoot };
        try
        {
            await dialog.ShowAsync();
        }
        catch (Exception error) when (error is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            // Only one dialog can be open in a window at a time.
            Log.Info("settings", $"Can't show the manual delay test: {error.Message}");
        }
    }

    // "Automatic" (built-in, else the default input), then every microphone Windows lists right now.
    private void ShowMicrophones()
    {
        var picked = AudioDelay.PickedMicrophone;
        _showing = true;
        try
        {
            MicrophonePicker.Items.Clear();
            MicrophonePicker.Items.Add(new ComboBoxItem { Content = "Automatic", Tag = string.Empty });
            foreach (var microphone in AudioDevices.Microphones())
            {
                MicrophonePicker.Items.Add(new ComboBoxItem { Content = microphone.Name, Tag = microphone.Id });
            }

            // A picked microphone that is unplugged right now shows as Automatic, which is what Detect delay then does.
            Select(MicrophonePicker, picked ?? string.Empty);
            if (MicrophonePicker.SelectedIndex < 0)
            {
                MicrophonePicker.SelectedIndex = 0;
            }
        }
        finally
        {
            _showing = false;
        }
    }

    // Microphones come and go, so the list is read again each time it is opened.
    private void OnMicrophonesOpened(object? sender, object e) => ShowMicrophones();

    private void OnMicrophoneChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_showing && MicrophonePicker.SelectedItem is ComboBoxItem { Tag: string id })
        {
            AudioDelay.PickedMicrophone = id.Length > 0 ? id : null;
        }
    }

    private void FillPresetChoices()
    {
        ModePicker.Items.Add(new ComboBoxItem { Content = "Shuffle", Tag = PresetMode.Shuffle });
        ModePicker.Items.Add(new ComboBoxItem { Content = "Single", Tag = PresetMode.Single });
        foreach (var source in Enum.GetValues<ShuffleSource>())
        {
            ShuffleFromPicker.Items.Add(new ComboBoxItem { Content = source.ToString(), Tag = source });
        }

        foreach (var seconds in PresetSettings.SecondsChoices)
        {
            SecondsPicker.Items.Add(new ComboBoxItem { Content = seconds.ToString(CultureInfo.InvariantCulture), Tag = seconds });
        }

        foreach (var blend in PresetSettings.BlendChoices)
        {
            BlendPicker.Items.Add(new ComboBoxItem { Content = PresetSettings.BlendLabel(blend), Tag = blend });
        }
    }

    /// <summary>Fills the preset rows in from the settings. Runs again on every change, from here or elsewhere.</summary>
    private void ShowPresets()
    {
        var settings = Presets.Settings;
        _showing = true;
        try
        {
            Select(ModePicker, settings.Mode);
            var shuffle = settings.Mode == PresetMode.Shuffle;
            ShuffleFromCard.Visibility = SecondsCard.Visibility = BlendCard.Visibility = shuffle ? Visibility.Visible : Visibility.Collapsed;
            SingleCard.Visibility = shuffle ? Visibility.Collapsed : Visibility.Visible;

            Select(ShuffleFromPicker, settings.ShuffleFrom);
            if (settings.EmptySourceHint(Presets.HasCustomPresets) is { } hint)
            {
                ShuffleFromCard.Description = hint;
            }
            else
            {
                ShuffleFromCard.ClearValue(CommunityToolkit.WinUI.Controls.SettingsCard.DescriptionProperty);
            }

            Select(SecondsPicker, settings.SecondsPerPreset);
            Select(BlendPicker, settings.BlendSeconds);
            ShowSingleChoices(settings.SinglePreset);

            if (Presets.LastShown is { } id)
            {
                LastShownCard.Visibility = Visibility.Visible;
                LastShownCard.Description = Presets.Name(id);
                var favorite = settings.IsFavorite(id);
                FavoriteButton.IsChecked = favorite;
                FavoriteIcon.Glyph = char.ConvertFromUtf32(favorite ? FilledHeartGlyph : HeartGlyph);
                Label(FavoriteButton, favorite ? "Remove from favorites" : "Add to favorites");
                var blocked = settings.IsBlocked(id);
                BlockButton.IsChecked = blocked;
                Label(BlockButton, blocked ? "Remove from the blocklist" : "Add to the blocklist");
            }
            else
            {
                // Hidden until a preset has been on screen.
                LastShownCard.Visibility = Visibility.Collapsed;
            }

            FavoritesButton.Content = $"Manage ({settings.Favorites.Count})";
            BlocklistButton.Content = $"Manage ({settings.Blocked.Count})";

            FolderCard.Description = $"{Presets.BundledCount} bundled, {Presets.Library.CustomCount} custom";
            ShowFailures(Presets.Failures);
        }
        finally
        {
            _showing = false;
        }
    }

    // The page's list has a few hundred entries, so the picker is only refilled when it changed, and gets
    // them as a plain list of names, which it can virtualize.
    private void ShowSingleChoices(string selected)
    {
        var presets = Presets.Presets;
        // Keeps the stored choice selectable while the list is still loading or the preset is gone.
        var extra = presets.Any(preset => preset.Id == selected) ? null : selected;
        if (!ReferenceEquals(presets, _singleChoices) || extra != _singleExtra)
        {
            _singleChoices = presets;
            _singleExtra = extra;
            var choices = (extra is null ? [] : new[] { new PresetInfo(extra, Presets.Name(extra), "bundled") }).Concat(presets).ToList();
            _singleIds = [.. choices.Select(preset => preset.Id)];
            SinglePicker.ItemsSource = choices.Select(preset => preset.Name).ToList();
        }

        var index = _singleIds.IndexOf(selected);
        if (SinglePicker.SelectedIndex != index)
        {
            SinglePicker.SelectedIndex = index;
        }
    }

    private void ShowFailures(IReadOnlyList<PresetFailure> failures)
    {
        if (failures.SequenceEqual(_shownFailures))
        {
            return;
        }

        _shownFailures = failures;
        FailedHeader.Text = $"Failed to load ({failures.Count})";
        FailedHeader.Visibility = failures.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        FailedList.Children.Clear();
        foreach (var failure in failures)
        {
            var card = new CommunityToolkit.WinUI.Controls.SettingsCard
            {
                Header = new TextBlock { Text = Presets.Name(failure.Id), TextTrimming = TextTrimming.CharacterEllipsis },
                Description = new TextBlock { Text = failure.Error, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis },
            };
            if (Presets.Library.FileFor(failure.Id) is not null)
            {
                var id = failure.Id;
                var reveal = new Button { Content = "Reveal" };
                AutomationProperties.SetName(reveal, $"Reveal {Presets.Name(id)}");
                reveal.Click += (_, _) => Presets.Library.Reveal(id);
                card.Content = reveal;
            }

            FailedList.Children.Add(card);
        }
    }

    private async void OnImportFilesClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new Microsoft.Windows.Storage.Pickers.FileOpenPicker(AppWindow.Id);
            foreach (var extension in PresetImport.Extensions)
            {
                picker.FileTypeFilter.Add(extension);
            }

            var files = await picker.PickMultipleFilesAsync();
            if (files is { Count: > 0 })
            {
                Presets.Library.Import(files.Select(file => file.Path));
            }
        }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            Log.Info("settings", $"The file picker failed: {error.Message}");
        }
    }

    private async void OnImportFolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new Microsoft.Windows.Storage.Pickers.FolderPicker(AppWindow.Id);
            if (await picker.PickSingleFolderAsync() is { } folder)
            {
                Presets.Library.Import([folder.Path]);
            }
        }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            Log.Info("settings", $"The folder picker failed: {error.Message}");
        }
    }

    private void OnOpenFolderClick(object sender, RoutedEventArgs e) => Presets.Library.OpenFolder();

    private void OnReloadClick(object sender, RoutedEventArgs e) => Presets.Library.Reload();

    private static void Select<T>(ComboBox picker, T value)
    {
        var item = picker.Items.OfType<ComboBoxItem>().FirstOrDefault(item => item.Tag is T tag && EqualityComparer<T>.Default.Equals(tag, value));
        if (item is not null && !ReferenceEquals(picker.SelectedItem, item))
        {
            picker.SelectedItem = item;
        }
    }

    private static void Label(FrameworkElement element, string text)
    {
        ToolTipService.SetToolTip(element, text);
        AutomationProperties.SetName(element, text);
    }

    private void Change<T>(ComboBox picker, Action<PresetSettings, T> apply)
    {
        if (!_showing && picker.SelectedItem is ComboBoxItem { Tag: T value })
        {
            Presets.Update(settings => apply(settings, value));
        }
    }

    private void OnModeChanged(object sender, SelectionChangedEventArgs e) =>
        Change<PresetMode>(ModePicker, (settings, mode) => settings.Mode = mode);

    private void OnShuffleFromChanged(object sender, SelectionChangedEventArgs e) =>
        Change<ShuffleSource>(ShuffleFromPicker, (settings, source) => settings.ShuffleFrom = source);

    private void OnSecondsChanged(object sender, SelectionChangedEventArgs e) =>
        Change<int>(SecondsPicker, (settings, seconds) => settings.SecondsPerPreset = seconds);

    private void OnBlendChanged(object sender, SelectionChangedEventArgs e) =>
        Change<double>(BlendPicker, (settings, blend) => settings.BlendSeconds = blend);

    private void OnSingleChanged(object sender, SelectionChangedEventArgs e)
    {
        var index = SinglePicker.SelectedIndex;
        if (!_showing && index >= 0 && index < _singleIds.Count)
        {
            var id = _singleIds[index];
            Presets.Update(settings => settings.SinglePreset = id);
        }
    }

    // The toggle buttons flip themselves on click; ShowPresets then sets them from the settings.
    private void OnFavoriteClick(object sender, RoutedEventArgs e)
    {
        if (Presets.LastShown is { } id)
        {
            Presets.Update(settings => settings.SetFavorite(id, !settings.IsFavorite(id)));
        }
    }

    private void OnBlockClick(object sender, RoutedEventArgs e)
    {
        if (Presets.LastShown is { } id)
        {
            Presets.Update(settings => settings.SetBlocked(id, !settings.IsBlocked(id)));
        }
    }

    private async void OnFavoritesClick(object sender, RoutedEventArgs e) => await ShowList(PresetList.Favorites);

    private async void OnBlocklistClick(object sender, RoutedEventArgs e) => await ShowList(PresetList.Blocklist);

    private async Task ShowList(PresetList list)
    {
        var dialog = new PresetListDialog(list, Presets) { XamlRoot = Content.XamlRoot };
        try
        {
            await dialog.ShowAsync();
        }
        catch (Exception error) when (error is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            // Only one dialog can be open in a window at a time.
            Log.Info("settings", $"Can't show the {list} dialog: {error.Message}");
        }
    }
}
