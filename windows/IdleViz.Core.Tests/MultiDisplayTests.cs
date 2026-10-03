namespace IdleViz.Core.Tests;

public sealed class MultiDisplayTests
{
    // The owner's desk: a wide primary display with a smaller one to its left.
    private static readonly Display s_wide = new(@"\\.\DISPLAY1", 0, 0, 3440, 1440, 96);
    private static readonly Display s_side = new(@"\\.\DISPLAY2", -1920, 0, 1920, 1080, 96);
    private static readonly Display s_third = new(@"\\.\DISPLAY3", 3440, 200, 2560, 1440, 144);
    private static readonly IReadOnlyList<Display> s_two = [s_wide, s_side];
    private static readonly IReadOnlyList<Display> s_three = [s_wide, s_side, s_third];

    [Fact]
    public void NothingStoredCoversThePrimaryDisplayAndClosesOnInput()
    {
        var settings = MultiDisplaySettings.Read(new SettingsStore());
        Assert.Equal(new MultiDisplaySettings(), settings with { OtherDisplays = null });
        Assert.Null(settings.OtherDisplays);

        var plan = DisplayPlan.For(settings, s_two);
        var window = Assert.Single(plan.Windows);
        Assert.Equal((0, 0, 3440, 1440), (window.Left, window.Top, window.Width, window.Height));
        Assert.Equal([new DisplayRegion(0, 0, 3440, 1440, Overlay: true)], window.Regions);
        Assert.Equal(DisplayPlan.RenderWidthCap, window.RenderWidthCap);
        Assert.True(plan.CloseOnInput);
    }

    [Fact]
    public void TheSettingsAreReadFromTheStore()
    {
        var store = new SettingsStore();
        store.SetString(MultiDisplaySettings.MainDisplayKey, s_side.Name);
        store.SetBool(MultiDisplaySettings.EnabledKey, true);
        store.SetStringList(MultiDisplaySettings.OtherDisplaysKey, [s_wide.Name]);
        store.SetBool(MultiDisplaySettings.CloseOnInputKey, false);
        store.SetString(MultiDisplaySettings.PlacementKey, "extend");
        store.SetString(MultiDisplaySettings.OverlayDisplayKey, MultiDisplaySettings.OverlayOnAll);

        var settings = MultiDisplaySettings.Read(store);
        Assert.Equal(s_side.Name, settings.MainDisplay);
        Assert.True(settings.Enabled);
        Assert.Equal([s_wide.Name], settings.OtherDisplays);
        Assert.False(settings.CloseOnInput);
        Assert.Equal(DisplayPlacement.Extend, settings.Placement);
        Assert.Equal(MultiDisplaySettings.OverlayOnAll, settings.OverlayDisplay);
    }

    [Fact]
    public void AnUnknownPlacementIsMirror()
    {
        var store = new SettingsStore();
        store.SetString(MultiDisplaySettings.PlacementKey, "diagonal");
        Assert.Equal(DisplayPlacement.Mirror, MultiDisplaySettings.Read(store).Placement);
        Assert.Equal("extend", MultiDisplaySettings.PlacementName(DisplayPlacement.Extend));
    }

    [Theory]
    [InlineData(MultiDisplaySettings.MainDisplayKey, true)]
    [InlineData(MultiDisplaySettings.OverlayDisplayKey, true)]
    [InlineData(MultiDisplaySettings.CloseOnInputKey, true)]
    [InlineData(OverlaySetting.Key, false)]
    public void KnowsItsOwnKeys(string key, bool expected) => Assert.Equal(expected, MultiDisplaySettings.IsKey(key));

    [Fact]
    public void TheMainDisplayCanBeAnotherThanThePrimary()
    {
        var plan = DisplayPlan.For(new MultiDisplaySettings(MainDisplay: s_side.Name), s_two);
        var window = Assert.Single(plan.Windows);
        Assert.Equal((-1920, 0, 1920, 1080), (window.Left, window.Top, window.Width, window.Height));
        Assert.True(window.Regions[0].Overlay);
    }

    [Fact]
    public void AMainDisplayThatIsNotConnectedFallsBackToThePrimary()
    {
        var plan = DisplayPlan.For(new MultiDisplaySettings(MainDisplay: @"\\.\DISPLAY9"), s_two);
        Assert.Equal(0, Assert.Single(plan.Windows).Left);
    }

    [Fact]
    public void MirrorGivesEachDisplayItsOwnWindowTheMainOneFirst()
    {
        var plan = DisplayPlan.For(new MultiDisplaySettings(MainDisplay: s_side.Name, Enabled: true), s_three);
        Assert.Equal([-1920, 0, 3440], plan.Windows.Select(window => window.Left));
        Assert.All(plan.Windows, window =>
        {
            Assert.Equal(new DisplayRegion(0, 0, window.Width, window.Height, window.Left == -1920), Assert.Single(window.Regions));
            Assert.Equal(DisplayPlan.RenderWidthCap, window.RenderWidthCap);
        });
    }

    [Fact]
    public void OnlyTheChosenOtherDisplaysAreCovered()
    {
        var settings = new MultiDisplaySettings(Enabled: true, OtherDisplays: [s_third.Name, @"\\.\DISPLAY9"]);
        Assert.Equal([0, 3440], DisplayPlan.For(settings, s_three).Windows.Select(window => window.Left));

        // None chosen: the switch is on, but only the main display is covered.
        Assert.Single(DisplayPlan.For(settings with { OtherDisplays = [] }, s_three).Windows);
    }

    [Fact]
    public void TheSwitchOffCoversOnlyTheMainDisplayWhateverElseIsStored()
    {
        var settings = new MultiDisplaySettings(
            Enabled: false, CloseOnInput: false, Placement: DisplayPlacement.Extend, OverlayDisplay: s_side.Name);
        var plan = DisplayPlan.For(settings, s_two);
        var window = Assert.Single(plan.Windows);
        Assert.Equal(3440, window.Width);
        Assert.True(window.Regions[0].Overlay);
        // Input only stays ignored while more than one display is switched on.
        Assert.True(plan.CloseOnInput);
    }

    [Fact]
    public void InputCanBeIgnoredWhileTheSwitchIsOn()
    {
        Assert.False(DisplayPlan.For(new MultiDisplaySettings(Enabled: true, CloseOnInput: false), s_two).CloseOnInput);
        Assert.True(DisplayPlan.For(new MultiDisplaySettings(Enabled: true), s_two).CloseOnInput);
    }

    [Fact]
    public void ExtendIsOneWindowOverTheEnclosingRectangle()
    {
        var plan = DisplayPlan.For(new MultiDisplaySettings(Enabled: true, Placement: DisplayPlacement.Extend), s_two);
        var window = Assert.Single(plan.Windows);
        Assert.Equal((-1920, 0, 5360, 1440), (window.Left, window.Top, window.Width, window.Height));
        Assert.Equal(
            [new DisplayRegion(1920, 0, 3440, 1440, Overlay: true), new DisplayRegion(0, 0, 1920, 1080, Overlay: false)],
            window.Regions);
        // 2560 px for the 3440 px display's share of the 5360 px span.
        Assert.Equal(3989, window.RenderWidthCap);
    }

    [Fact]
    public void ExtendWithASmallMainDisplayStopsAtThePagesLimit()
    {
        var settings = new MultiDisplaySettings(MainDisplay: s_side.Name, Enabled: true, Placement: DisplayPlacement.Extend);
        var window = Assert.Single(DisplayPlan.For(settings, s_three).Windows);
        Assert.Equal((-1920, 0, 7920, 1640), (window.Left, window.Top, window.Width, window.Height));
        Assert.Equal(DisplayPlan.MaxRenderWidthCap, window.RenderWidthCap);
        Assert.Equal(new DisplayRegion(5360, 200, 2560, 1440, Overlay: false), window.Regions[2]);
    }

    [Fact]
    public void ExtendWithOneDisplayIsThatDisplay()
    {
        var plan = DisplayPlan.For(new MultiDisplaySettings(Enabled: true, Placement: DisplayPlacement.Extend), [s_wide]);
        var window = Assert.Single(plan.Windows);
        Assert.Equal(DisplayPlan.RenderWidthCap, window.RenderWidthCap);
        Assert.Single(window.Regions);
    }

    [Fact]
    public void TheOverlayGoesOnTheChosenDisplayOrOnAll()
    {
        var mirror = new MultiDisplaySettings(Enabled: true, OverlayDisplay: s_side.Name);
        Assert.Equal([false, true], DisplayPlan.For(mirror, s_two).Windows.Select(window => window.Regions[0].Overlay));

        var all = mirror with { OverlayDisplay = MultiDisplaySettings.OverlayOnAll };
        Assert.Equal([true, true], DisplayPlan.For(all, s_two).Windows.Select(window => window.Regions[0].Overlay));

        var extended = all with { Placement = DisplayPlacement.Extend };
        Assert.All(Assert.Single(DisplayPlan.For(extended, s_two).Windows).Regions, region => Assert.True(region.Overlay));
    }

    [Fact]
    public void AnOverlayDisplayThatIsNotCoveredFallsBackToTheMainOne()
    {
        var settings = new MultiDisplaySettings(Enabled: true, OtherDisplays: [s_third.Name], OverlayDisplay: s_side.Name);
        Assert.Equal([true, false], DisplayPlan.For(settings, s_three).Windows.Select(window => window.Regions[0].Overlay));
    }

    [Fact]
    public void NoDisplayAtAllStillPlansAWindow()
    {
        var window = Assert.Single(DisplayPlan.For(new MultiDisplaySettings(Enabled: true), []).Windows);
        Assert.Equal((1920, 1080), (window.Width, window.Height));
    }

    [Fact]
    public void TheScriptTellsThePageItsCapAndItsRegions()
    {
        var plan = DisplayPlan.For(new MultiDisplaySettings(Enabled: true, Placement: DisplayPlacement.Extend), s_two);
        Assert.Equal(
            "window.setRenderWidthCap?.(3989);window.setOverlayRegions?.({\"height\":1440,\"regions\":["
            + "{\"height\":1440,\"overlay\":true,\"width\":3440,\"x\":1920,\"y\":0},"
            + "{\"height\":1080,\"overlay\":false,\"width\":1920,\"x\":0,\"y\":0}],\"width\":5360})",
            plan.Windows[0].Script);
    }

    [Fact]
    public void DisplaysAreLabelledByTheirWindowsNumberAndSize()
    {
        Assert.Equal("Display 1 (3440 × 1440)", DisplayLabel.For(s_wide));
        Assert.Equal("Display 12", DisplayLabel.Brief(@"\\.\DISPLAY12"));
        Assert.Equal("Projector", DisplayLabel.Brief("Projector"));
    }

    [Fact]
    public void AFollowingPageIsHeldOnTheMainPagesPreset()
    {
        var settings = new PresetSettings { BlendSeconds = 5 };
        var script = settings.FollowScript("bundled:Tunnel \"2\"");
        Assert.Contains("\"mode\":\"single\"", script, StringComparison.Ordinal);
        Assert.Contains("\"single\":\"bundled:Tunnel \\u00222\\u0022\"", script, StringComparison.Ordinal);
        Assert.Contains("\"blendSeconds\":5", script, StringComparison.Ordinal);
        // The main page's own settings are untouched.
        Assert.Contains("\"mode\":\"shuffle\"", settings.Script, StringComparison.Ordinal);
    }
}
