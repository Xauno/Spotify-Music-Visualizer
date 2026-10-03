using System.Globalization;
using System.Text.Json.Nodes;

namespace IdleViz.Core;

/// <summary>How the visualizer is placed on more than one display.</summary>
public enum DisplayPlacement
{
    /// <summary>Each display runs its own visualizer, with the same preset and the same audio.</summary>
    Mirror,

    /// <summary>One picture runs across all the displays.</summary>
    Extend,
}

/// <summary>
/// The "Displays" settings: which display is the main one, whether further displays are covered,
/// which ones, how, whether input closes the visualizer, and where the Spotify overlay shows.
/// Displays are stored by the name Windows gives them, for example <c>\\.\DISPLAY1</c>.
/// </summary>
/// <param name="MainDisplay">The main display, or null for the one Windows calls primary.</param>
/// <param name="Enabled">Whether more than one display is covered.</param>
/// <param name="OtherDisplays">The further displays to cover, or null for all of them.</param>
/// <param name="CloseOnInput">Whether input closes the visualizer. Only read while <paramref name="Enabled"/> is on.</param>
/// <param name="Placement">Mirror or extend.</param>
/// <param name="OverlayDisplay"><see cref="OverlayOnMain"/>, <see cref="OverlayOnAll"/> or a display's name.</param>
public sealed record MultiDisplaySettings(
    string? MainDisplay = null,
    bool Enabled = false,
    IReadOnlyList<string>? OtherDisplays = null,
    bool CloseOnInput = true,
    DisplayPlacement Placement = DisplayPlacement.Mirror,
    string OverlayDisplay = MultiDisplaySettings.OverlayOnMain)
{
    public const string MainDisplayKey = "mainDisplay";
    public const string EnabledKey = "multiDisplay";
    public const string OtherDisplaysKey = "multiDisplayOthers";
    public const string CloseOnInputKey = "multiDisplayCloseOnInput";
    public const string PlacementKey = "multiDisplayPlacement";
    public const string OverlayDisplayKey = "overlayDisplay";

    public const string OverlayOnMain = "main";
    public const string OverlayOnAll = "all";

    /// <summary>Shown while more than one display is covered.</summary>
    public const string GpuWarning =
        "Using more than one display takes more GPU power. The visualizer may run less smoothly, at a lower frame rate.";

    /// <summary>Whether a changed setting is one of these.</summary>
    public static bool IsKey(string key) =>
        key is MainDisplayKey or EnabledKey or OtherDisplaysKey or CloseOnInputKey or PlacementKey or OverlayDisplayKey;

    /// <summary>Reads the stored settings. Missing or unusable values fall back to the defaults.</summary>
    public static MultiDisplaySettings Read(SettingsStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        return new MultiDisplaySettings(
            store.GetString(MainDisplayKey) is { Length: > 0 } main ? main : null,
            store.GetBool(EnabledKey) ?? false,
            store.GetStringList(OtherDisplaysKey),
            store.GetBool(CloseOnInputKey) ?? true,
            store.GetString(PlacementKey) == PlacementName(DisplayPlacement.Extend) ? DisplayPlacement.Extend : DisplayPlacement.Mirror,
            store.GetString(OverlayDisplayKey) is { Length: > 0 } overlay ? overlay : OverlayOnMain);
    }

    /// <summary>The stored name: "mirror" or "extend".</summary>
    public static string PlacementName(DisplayPlacement placement) => PresetSettings.Name(placement);

    /// <summary>The main display among the connected ones (the primary one first): the stored one, or the primary.</summary>
    public Display Main(IReadOnlyList<Display> displays)
    {
        ArgumentNullException.ThrowIfNull(displays);
        return displays.FirstOrDefault(display => display.Name == MainDisplay) ?? displays[0];
    }

    /// <summary>Whether a display other than the main one is covered while the switch is on.</summary>
    public bool Covers(Display display)
    {
        ArgumentNullException.ThrowIfNull(display);
        return OtherDisplays is null || OtherDisplays.Contains(display.Name);
    }
}

/// <summary>Names the displays in the settings window.</summary>
public static class DisplayLabel
{
    /// <summary>"Display 1 (3440 × 1440)". The number is the one in the name Windows gives the display.</summary>
    public static string For(Display display)
    {
        ArgumentNullException.ThrowIfNull(display);
        return $"{Brief(display.Name)} ({display.Width} × {display.Height})";
    }

    /// <summary>"Display 1", for a display that may not be connected.</summary>
    public static string Brief(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var digits = new string([.. name.Reverse().TakeWhile(char.IsAsciiDigit).Reverse()]);
        return digits.Length > 0 ? $"Display {digits}" : name;
    }
}

/// <summary>One display's part of a visualizer window, in pixels from the window's top left corner.</summary>
public sealed record DisplayRegion(int Left, int Top, int Width, int Height, bool Overlay);

/// <summary>One visualizer window: where it goes, in screen pixels, and the displays it covers.</summary>
/// <param name="Left">The window's left edge in screen coordinates.</param>
/// <param name="Top">Its top edge.</param>
/// <param name="Width">Its width in pixels.</param>
/// <param name="Height">Its height in pixels.</param>
/// <param name="Regions">The displays it covers, the main one first.</param>
/// <param name="RenderWidthCap">The widest the page may render the visualizer.</param>
public sealed record PlannedWindow(int Left, int Top, int Width, int Height, IReadOnlyList<DisplayRegion> Regions, int RenderWidthCap)
{
    /// <summary>The calls that tell the page in this window which displays it covers.</summary>
    public string Script
    {
        get
        {
            var regions = Regions.Select(region => (JsonNode)new JsonObject
            {
                ["height"] = region.Height,
                ["overlay"] = region.Overlay,
                ["width"] = region.Width,
                ["x"] = region.Left,
                ["y"] = region.Top,
            });
            var layout = new JsonObject { ["height"] = Height, ["regions"] = new JsonArray([.. regions]), ["width"] = Width };
            return string.Create(
                CultureInfo.InvariantCulture,
                $"window.setRenderWidthCap?.({RenderWidthCap});window.setOverlayRegions?.({layout.ToJsonString()})");
        }
    }
}

/// <summary>
/// Which visualizer windows to show for the settings and the displays connected right now. The
/// first window is the main one: it holds the page that picks the presets, and the others follow it.
/// </summary>
/// <param name="Windows">The windows, the main one first.</param>
/// <param name="CloseOnInput">False when input leaves the visualizer open; the hotkey or the tray icon closes it then.</param>
public sealed record DisplayPlan(IReadOnlyList<PlannedWindow> Windows, bool CloseOnInput)
{
    /// <summary>The page's own render width cap (<c>MAX_RENDER_WIDTH</c> in visualizer-state.js).</summary>
    public const int RenderWidthCap = 2560;

    /// <summary>The page takes no cap above this (<c>MAX_SPAN_RENDER_WIDTH</c>).</summary>
    public const int MaxRenderWidthCap = 7680;

    // What to plan for when Windows reports no display at all, as in a disconnected remote session.
    private static readonly Display s_noDisplay = new(string.Empty, 0, 0, 1920, 1080, 96);

    /// <param name="settings">The stored settings.</param>
    /// <param name="displays">The connected displays, the primary one first.</param>
    public static DisplayPlan For(MultiDisplaySettings settings, IReadOnlyList<Display> displays)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(displays);
        if (displays.Count == 0)
        {
            displays = [s_noDisplay];
        }

        var main = settings.Main(displays);
        List<Display> covered = [main];
        if (settings.Enabled)
        {
            covered.AddRange(displays.Where(display => display != main && settings.Covers(display)));
        }

        // A display that isn't covered can't show the overlay, so it falls back to the main one.
        var overlayOnAll = settings.OverlayDisplay == MultiDisplaySettings.OverlayOnAll;
        var overlayOn = covered.Any(display => display.Name == settings.OverlayDisplay) ? settings.OverlayDisplay : main.Name;
        bool Overlay(Display display) => overlayOnAll || display.Name == overlayOn;

        var closeOnInput = !settings.Enabled || settings.CloseOnInput;
        if (covered.Count == 1 || settings.Placement == DisplayPlacement.Mirror)
        {
            return new DisplayPlan(
                [.. covered.Select(display => new PlannedWindow(
                    display.Left,
                    display.Top,
                    display.Width,
                    display.Height,
                    [new DisplayRegion(0, 0, display.Width, display.Height, Overlay(display))],
                    RenderWidthCap))],
                closeOnInput);
        }

        // One window over the rectangle that encloses the displays. Parts of it that no display covers aren't seen.
        var left = covered.Min(display => display.Left);
        var top = covered.Min(display => display.Top);
        var width = covered.Max(display => display.Left + display.Width) - left;
        var height = covered.Max(display => display.Top + display.Height) - top;
        // The main display's part stays as sharp as when it is covered alone.
        var cap = (int)Math.Min(Math.Round((double)RenderWidthCap * width / main.Width), MaxRenderWidthCap);
        var regions = covered.Select(display => new DisplayRegion(display.Left - left, display.Top - top, display.Width, display.Height, Overlay(display)));
        return new DisplayPlan([new PlannedWindow(left, top, width, height, [.. regions], cap)], closeOnInput);
    }
}
