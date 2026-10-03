// Pure visualizer logic, kept free of the DOM and WebGL so it runs under Vitest.

/** Butterchurn renders at most this wide; the GPU scales it up on larger displays. */
export const MAX_RENDER_WIDTH = 2560;
/** The widest the app may ask for, when one window spans several displays. */
export const MAX_SPAN_RENDER_WIDTH = 7680;
/** Frames closer together than this are skipped, which halves 120 Hz to 60 fps. A strict
 *  16.7 ms would also drop about a third of the frames on a 60 Hz display (timestamp jitter). */
export const MIN_FRAME_GAP_MS = 12;
/** The skip key blends this fast whatever the blend time setting is, so the key feels immediate. */
export const SKIP_BLEND_SECONDS = 0.5;

/**
 * The preset controls from the settings window, as Swift sends them.
 * @typedef {object} PresetSettings
 * @property {"shuffle" | "single"} mode
 * @property {string} single            Id of the preset shown in single mode.
 * @property {"all" | "bundled" | "custom" | "favorites"} shuffleFrom
 * @property {number} secondsPerPreset
 * @property {number} blendSeconds
 * @property {string[]} favorites       Preset ids.
 * @property {string[]} blocked         Preset ids left out of shuffle.
 */

/** @type {Readonly<PresetSettings>} */
export const DEFAULT_SETTINGS = Object.freeze({
  mode: "shuffle",
  single: "",
  shuffleFrom: "all",
  secondsPerPreset: 30,
  blendSeconds: 2.7,
  favorites: [],
  blocked: [],
});

const idList = (value) => (Array.isArray(value) ? value.filter((id) => typeof id === "string") : []);

/**
 * Checks the shape of a `setPresetSettings` payload. Anything missing or out of range gets its default.
 * @param {unknown} value
 * @returns {PresetSettings}
 */
export function parsePresetSettings(value) {
  const item = /** @type {Record<string, unknown>} */ (value && typeof value === "object" ? value : {});
  const seconds = Number(item.secondsPerPreset);
  const blend = Number(item.blendSeconds);
  const sources = ["all", "bundled", "custom", "favorites"];
  return {
    mode: item.mode === "single" ? "single" : "shuffle",
    single: typeof item.single === "string" ? item.single : "",
    shuffleFrom: /** @type {PresetSettings["shuffleFrom"]} */ (
      sources.includes(/** @type {string} */ (item.shuffleFrom)) ? item.shuffleFrom : "all"
    ),
    secondsPerPreset:
      Number.isFinite(seconds) && seconds >= 1 ? Math.min(seconds, 86_400) : DEFAULT_SETTINGS.secondsPerPreset,
    blendSeconds: Number.isFinite(blend) && blend >= 0 ? Math.min(blend, 60) : DEFAULT_SETTINGS.blendSeconds,
    favorites: idList(item.favorites),
    blocked: idList(item.blocked),
  };
}

/**
 * The ids shuffle may pick from: the chosen source, minus blocked presets.
 * An empty choice (no favorites yet, no custom presets, everything blocked) would leave the
 * screen black, so it widens: first to every preset that isn't blocked, then to all of them.
 * @param {PresetEntry[]} presets
 * @param {PresetSettings} settings
 * @returns {string[]}
 */
export function shufflePool(presets, settings) {
  const blocked = new Set(settings.blocked);
  const favorites = new Set(settings.favorites);
  const allowed = presets.filter((entry) => !blocked.has(entry.id));
  const chosen = allowed.filter((entry) => {
    switch (settings.shuffleFrom) {
      case "bundled":
      case "custom":
        return entry.source === settings.shuffleFrom;
      case "favorites":
        return favorites.has(entry.id);
      default:
        return true;
    }
  });
  const pool = chosen.length > 0 ? chosen : allowed.length > 0 ? allowed : presets;
  return pool.map((entry) => entry.id);
}

/**
 * @typedef {object} PresetEntry
 * @property {string} id       Stable key: the source, a colon, then the name (or the file's path).
 * @property {string} name
 * @property {"bundled" | "custom"} source
 * @property {"preset" | "plugin"} kind  A Butterchurn preset, or a JavaScript plugin run in a frame.
 * @property {object} [preset]   The Butterchurn preset itself. Custom ones are fetched when first shown.
 * @property {string} [url]      Where a custom preset or a plugin is loaded from.
 * @property {string} [version]  Changes when the file does.
 */

const byName = (a, b) => a.name.localeCompare(b.name, "en", { sensitivity: "base" });

/**
 * One sorted list from several preset packs. A name that appears in more than one pack
 * is kept once, from the first pack that has it.
 * @param {Array<Record<string, object> | null | undefined>} packs
 * @param {"bundled" | "custom"} [source]
 * @returns {PresetEntry[]}
 */
export function collectPresets(packs, source = "bundled") {
  /** @type {Map<string, PresetEntry>} */
  const seen = new Map();
  for (const pack of packs) {
    if (!pack || typeof pack !== "object") continue;
    for (const [name, preset] of Object.entries(pack)) {
      if (!preset || typeof preset !== "object" || seen.has(name)) continue;
      seen.set(name, { id: `${source}:${name}`, name, source, kind: "preset", preset });
    }
  }
  return [...seen.values()].sort(byName);
}

const MAX_CUSTOM_ENTRIES = 6000;
/**
 * Where each source may load from. The Mac app serves the page from its own `idleviz-app:` scheme.
 * The Windows app can't: WebView2 blocks a sandboxed frame from loading scripts off a custom
 * scheme, so it answers these `https:` addresses itself. `.invalid` never exists on the network.
 */
const URL_PREFIXES = {
  custom: ["idleviz-app://presets/", "https://presets.idleviz.invalid/"],
  bundled: ["idleviz-app://app/visuals/", "https://app.idleviz.invalid/visuals/"],
};

/**
 * Checks the shape of a `setCustomPresets` payload: the plugins bundled with the app and the
 * files in the custom presets folder. Entries that don't fit are dropped.
 * @param {unknown} value
 * @returns {{ entries: PresetEntry[], hung: string[] }}
 */
export function parseCustomPresets(value) {
  const payload = /** @type {Record<string, unknown>} */ (value && typeof value === "object" ? value : {});
  const text = (v, max = 400) => typeof v === "string" && v.length > 0 && v.length <= max;
  /** @type {Map<string, PresetEntry>} */
  const entries = new Map();
  for (const raw of Array.isArray(payload.entries) ? payload.entries.slice(0, MAX_CUSTOM_ENTRIES) : []) {
    if (!raw || typeof raw !== "object") continue;
    const { id, name, source, kind, url, version } = raw;
    if (!text(id) || !text(name) || !text(url, 2000) || entries.has(id)) continue;
    if (kind !== "preset" && kind !== "plugin") continue;
    // Each source may only load from its own place: the presets folder, or the app's bundled plugins.
    if (!(source in URL_PREFIXES) || !id.startsWith(`${source}:`)) continue;
    if (!URL_PREFIXES[source].some((prefix) => url.startsWith(prefix))) continue;
    entries.set(id, { id, name, source, kind, url, version: typeof version === "string" ? version : "" });
  }
  const hung = Array.isArray(payload.hung) ? payload.hung.filter((item) => text(item)) : [];
  return { entries: [...entries.values()], hung };
}

/**
 * Whether a fetched value has the parts Butterchurn needs from a preset.
 * @param {any} value
 */
export function isPreset(value) {
  return (
    Boolean(value) &&
    typeof value === "object" &&
    Boolean(value.baseVals) &&
    typeof value.baseVals === "object" &&
    Array.isArray(value.shapes) &&
    Array.isArray(value.waves)
  );
}

/**
 * The whole library, sorted by name: the bundled Butterchurn presets plus what Swift sent.
 * An entry that is unchanged keeps the preset it already fetched.
 * @param {PresetEntry[]} bundled
 * @param {PresetEntry[]} custom
 * @param {Map<string, PresetEntry>} [previous]  The library before, by id.
 */
export function mergeLibrary(bundled, custom, previous = new Map()) {
  const taken = new Set(bundled.map((entry) => entry.id));
  const added = custom
    .filter((entry) => !taken.has(entry.id))
    .map((entry) => {
      const before = previous.get(entry.id);
      return before?.preset && before.version === entry.version ? { ...entry, preset: before.preset } : entry;
    });
  return [...bundled, ...added].sort(byName);
}

/** Presets and plugins that failed, each with its error. A failed one is skipped until its file changes. */
export class FailureLog {
  constructor() {
    /** @type {Map<string, { error: string, version: string | undefined }>} */
    this.failures = new Map();
  }

  /**
   * @param {PresetEntry | { id: string, version?: string }} entry
   * @param {string} error
   */
  add(entry, error) {
    this.failures.set(entry.id, { error: String(error).slice(0, 300), version: entry.version });
  }

  has(id) {
    return this.failures.has(id);
  }

  /**
   * Forgets failures of files that changed or are gone, so they get another try.
   * @param {Map<string, PresetEntry>} library  The current library, by id.
   */
  prune(library) {
    for (const [id, failure] of this.failures) {
      const entry = library.get(id);
      if (!entry || (entry.version ?? undefined) !== failure.version) this.failures.delete(id);
    }
  }

  /** @returns {Array<{ id: string, error: string }>} */
  list() {
    return [...this.failures].map(([id, { error }]) => ({ id, error }));
  }
}

/**
 * The blend for the skip key, or null when there is no next preset to skip to (single mode).
 * @param {PresetSettings} settings
 */
export function skipBlendSeconds(settings) {
  return settings.mode === "shuffle" ? SKIP_BLEND_SECONDS : null;
}

/**
 * Hands out ids in random order and goes through all of them before any repeats.
 */
export class ShuffleBag {
  /**
   * @param {string[]} ids
   * @param {() => number} [random]  Returns a number in [0, 1), like Math.random.
   */
  constructor(ids, random = Math.random) {
    this.ids = [...ids];
    this.random = random;
    /** @type {string[]} */
    this.bag = [];
    /** @type {string | null} */
    this.last = null;
  }

  /**
   * Switches to a new set of ids and starts a fresh round.
   * @param {string[]} ids
   * @param {string | null} [last]  The id on screen now, so the new round doesn't start with it.
   */
  setIds(ids, last = this.last) {
    this.ids = [...ids];
    this.bag = [];
    this.last = last;
  }

  /** The next id, or null when there are none. */
  next() {
    if (this.ids.length === 0) return null;
    if (this.bag.length === 0) this.refill();
    this.last = /** @type {string} */ (this.bag.pop());
    return this.last;
  }

  refill() {
    const bag = [...this.ids];
    for (let i = bag.length - 1; i > 0; i--) {
      const j = Math.floor(this.random() * (i + 1));
      [bag[i], bag[j]] = [bag[j], bag[i]];
    }
    // The end of the array is handed out first. Don't start a round with the preset just shown.
    const end = bag.length - 1;
    if (end > 0 && bag[end] === this.last) [bag[0], bag[end]] = [bag[end], bag[0]];
    this.bag = bag;
  }
}

/**
 * Counts time on screen and says when the next preset is due. It is fed the time between
 * rendered frames, so time while the window is closed doesn't count.
 */
export class Rotation {
  /** @param {number} secondsPerPreset */
  constructor(secondsPerPreset) {
    this.secondsPerPreset = secondsPerPreset;
    this.elapsed = 0;
  }

  /**
   * @param {number} seconds  Time since the last frame.
   * @returns {boolean} True once per period, when the preset should change.
   */
  tick(seconds) {
    this.elapsed += seconds;
    if (this.elapsed < this.secondsPerPreset) return false;
    this.elapsed = 0;
    return true;
  }

  reset() {
    this.elapsed = 0;
  }
}

/**
 * The canvas size in pixels: the CSS size at the device pixel ratio, scaled down to `maxWidth`.
 * @param {number} cssWidth
 * @param {number} cssHeight
 * @param {number} devicePixelRatio
 * @param {number} [maxWidth]
 */
export function renderSize(cssWidth, cssHeight, devicePixelRatio, maxWidth = MAX_RENDER_WIDTH) {
  const width = Math.max(cssWidth, 1);
  const height = Math.max(cssHeight, 1);
  const scale = Math.min(devicePixelRatio > 0 ? devicePixelRatio : 1, maxWidth / width);
  return { width: Math.max(1, Math.round(width * scale)), height: Math.max(1, Math.round(height * scale)) };
}

/**
 * The render width cap the app asks for when the window spans several displays, so the main
 * display's part stays as sharp as it is alone. Anything unusable is the normal cap.
 * @param {unknown} value
 */
export function renderWidthCap(value) {
  if (typeof value !== "number" || !Number.isFinite(value)) return MAX_RENDER_WIDTH;
  return Math.round(Math.min(Math.max(value, MAX_RENDER_WIDTH), MAX_SPAN_RENDER_WIDTH));
}

/**
 * Whether to render on this animation frame.
 * @param {number} now   `requestAnimationFrame` timestamp, ms.
 * @param {number} last  Timestamp of the last rendered frame, ms.
 */
export function shouldRender(now, last) {
  return now - last >= MIN_FRAME_GAP_MS;
}
