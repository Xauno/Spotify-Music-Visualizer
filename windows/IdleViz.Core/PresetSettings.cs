using System.Globalization;
using System.Text.Json.Nodes;

namespace IdleViz.Core;

public enum PresetMode
{
    Shuffle,

    /// <summary>One preset stays on screen. Named as the page and the Mac name it.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "Matches the stored and sent value \"single\".")]
    Single,
}

public enum ShuffleSource
{
    All,
    Bundled,
    Custom,
    Favorites,
}

/// <summary>
/// The preset controls from the settings window. Stored with the Mac's key names and sent to the
/// page, which applies them at once. Ported from <c>PresetSettings.swift</c>.
/// </summary>
public sealed class PresetSettings
{
    public const string ModeKey = "visualizerMode";
    public const string SingleKey = "singlePreset";
    public const string ShuffleFromKey = "shuffleFrom";
    public const string SecondsPerPresetKey = "secondsPerPreset";
    public const string BlendSecondsKey = "blendSeconds";
    public const string FavoritesKey = "favoritePresets";
    public const string BlockedKey = "blockedPresets";

    /// <summary>The preset that was last on screen, kept so it can be rated after the window closes.</summary>
    public const string LastShownKey = "lastShownPreset";

    /// <summary>A beat-driven preset that followed the music well in the Mac's step 2 spike.</summary>
    public const string DefaultSingle = "bundled:Flexi, martin + geiss - dedicated to the sherwin maxawow";

    public static IReadOnlyList<int> SecondsChoices { get; } = [15, 30, 45, 60, 120, 300];

    public static IReadOnlyList<double> BlendChoices { get; } = [0, 1, 2.7, 5, 8];

    private readonly List<string> _favorites = [];
    private readonly List<string> _blocked = [];

    public PresetMode Mode { get; set; } = PresetMode.Shuffle;

    /// <summary>The preset shown in single mode.</summary>
    public string SinglePreset { get; set; } = DefaultSingle;

    public ShuffleSource ShuffleFrom { get; set; } = ShuffleSource.All;

    public int SecondsPerPreset { get; set; } = 30;

    public double BlendSeconds { get; set; } = 2.7;

    public IReadOnlyList<string> Favorites => _favorites;

    /// <summary>Presets left out of shuffle. A preset picked in single mode is shown even if it's on this list.</summary>
    public IReadOnlyList<string> Blocked => _blocked;

    /// <summary>The call that hands the settings to the page.</summary>
    public string Script => ScriptFor(Mode, SinglePreset);

    /// <summary>
    /// The call for a page on another display, which shows whatever the main page shows: the same
    /// settings, but held on that one preset. The page blends to it like to any other change.
    /// </summary>
    public string FollowScript(string preset) => ScriptFor(PresetMode.Single, preset);

    private string ScriptFor(PresetMode mode, string single)
    {
        // Sorted keys, as the Mac sends them. The default encoder escapes quotes, backslashes, "<" and
        // every non-ASCII character (U+2028 and U+2029 included), so preset names can't break out.
        var json = new JsonObject
        {
            ["blendSeconds"] = BlendSeconds,
            ["blocked"] = new JsonArray([.. _blocked.Select(id => (JsonNode)JsonValue.Create(id))]),
            ["favorites"] = new JsonArray([.. _favorites.Select(id => (JsonNode)JsonValue.Create(id))]),
            ["mode"] = Name(mode),
            ["secondsPerPreset"] = SecondsPerPreset,
            ["shuffleFrom"] = Name(ShuffleFrom),
            ["single"] = single,
        };
        return $"window.setPresetSettings?.({json.ToJsonString()})";
    }

    /// <summary>Reads the stored settings. Missing or unusable values fall back to the defaults.</summary>
    public static PresetSettings Read(SettingsStore store)
    {
        var settings = new PresetSettings();
        if (Parse<PresetMode>(store.GetString(ModeKey)) is { } mode)
        {
            settings.Mode = mode;
        }

        if (store.GetString(SingleKey) is { Length: > 0 } single)
        {
            settings.SinglePreset = single;
        }

        if (Parse<ShuffleSource>(store.GetString(ShuffleFromKey)) is { } source)
        {
            settings.ShuffleFrom = source;
        }

        if (store.GetInt(SecondsPerPresetKey) is { } seconds && SecondsChoices.Contains(seconds))
        {
            settings.SecondsPerPreset = seconds;
        }

        if (store.GetDouble(BlendSecondsKey) is { } blend && BlendChoices.Contains(blend))
        {
            settings.BlendSeconds = blend;
        }

        settings._favorites.AddRange((store.GetStringList(FavoritesKey) ?? []).Distinct(StringComparer.Ordinal));
        settings._blocked.AddRange((store.GetStringList(BlockedKey) ?? []).Distinct(StringComparer.Ordinal).Where(id => !settings._favorites.Contains(id)));
        return settings;
    }

    public void Save(SettingsStore store)
    {
        store.SetString(ModeKey, Name(Mode));
        store.SetString(SingleKey, SinglePreset);
        store.SetString(ShuffleFromKey, Name(ShuffleFrom));
        store.SetInt(SecondsPerPresetKey, SecondsPerPreset);
        store.SetDouble(BlendSecondsKey, BlendSeconds);
        store.SetStringList(FavoritesKey, _favorites);
        store.SetStringList(BlockedKey, _blocked);
    }

    public bool IsFavorite(string id) => _favorites.Contains(id);

    public bool IsBlocked(string id) => _blocked.Contains(id);

    /// <summary>Adds or removes a favorite. A preset can't be both liked and blocked, so adding it here unblocks it.</summary>
    public void SetFavorite(string id, bool listed)
    {
        _favorites.Remove(id);
        if (listed)
        {
            _favorites.Add(id);
            _blocked.Remove(id);
        }
    }

    /// <summary>Adds or removes a blocked preset. Blocking a favorite removes it from the favorites.</summary>
    public void SetBlocked(string id, bool listed)
    {
        _blocked.Remove(id);
        if (listed)
        {
            _blocked.Add(id);
            _favorites.Remove(id);
        }
    }

    /// <summary>
    /// Shuffle widens an empty choice to all presets; the Shuffle from row says so instead of
    /// silently ignoring the setting. Null when the choice isn't empty.
    /// </summary>
    public string? EmptySourceHint(bool hasCustomPresets) => ShuffleFrom switch
    {
        ShuffleSource.Favorites when _favorites.Count == 0 => "No favorites yet, so all presets are used",
        ShuffleSource.Custom when !hasCustomPresets => "No custom presets, so all presets are used",
        _ => null,
    };

    /// <summary>"0 s", "2.7 s".</summary>
    public static string BlendLabel(double seconds) => $"{seconds.ToString("0.#", CultureInfo.InvariantCulture)} s";

    /// <summary>The name the page and the Mac use: "shuffle", "favorites".</summary>
    public static string Name<T>(T value)
        where T : struct, Enum => value.ToString().ToLowerInvariant();

    private static T? Parse<T>(string? name)
        where T : struct, Enum => Enum.GetValues<T>().Cast<T?>().FirstOrDefault(value => Name(value!.Value) == name);
}
