import { createAudioState, decodeAudioFrame } from "./audio-frame.js";
import { PluginFrame } from "./plugin-frame.js";
import { describeError } from "./plugin-runner-core.js";
import {
  DEFAULT_SETTINGS,
  FailureLog,
  MAX_RENDER_WIDTH,
  Rotation,
  ShuffleBag,
  collectPresets,
  isPreset,
  mergeLibrary,
  parseCustomPresets,
  parsePresetSettings,
  renderSize,
  renderWidthCap,
  shouldRender,
  shufflePool,
  skipBlendSeconds,
} from "./visualizer-state.js";

/** @typedef {import("./visualizer-state.js").PresetEntry} PresetEntry */

const PACKS = ["butterchurnPresets", "butterchurnPresetsExtra", "butterchurnPresetsExtra2", "butterchurnPresetsMD1"];
/** After a failed build (no WebGL right after wake, for example), wait this long before trying again. */
const REBUILD_WAIT_MS = 1000;
/** The longest step a single frame may take, so reopening the window doesn't jump time forward. */
const MAX_FRAME_SECONDS = 0.1;

const host = /** @type {HTMLElement} */ (document.getElementById("viz"));
const globals = /** @type {any} */ (window);
const butterchurn = globals.butterchurn?.default ?? globals.butterchurn;

const bundled = collectPresets(PACKS.map((pack) => globals[pack]?.getPresets?.()));
/** Everything that can be shown: the bundled presets, plus the plugins and custom presets Swift sends. */
let presets = bundled;
let byId = new Map(presets.map((entry) => [entry.id, entry]));
/** The preset controls. Swift sends the stored ones right after the page loads. */
let settings = parsePresetSettings(DEFAULT_SETTINGS);
let pool = new Set(shufflePool(presets, settings));
const shuffle = new ShuffleBag([...pool]);
const rotation = new Rotation(settings.secondsPerPreset);
/** Presets and plugins that failed; skipped until their file changes. */
const failures = new FailureLog();

// The only audio the visuals ever see: frames from Swift's tap on Spotify, silence until one arrives.
const audioState = createAudioState();
// Butterchurn only reads the sample rate from this context, to place its bass, mid and treble
// ranges. It stays suspended and unconnected: the audio arrives as plain arrays through
// render({ audioLevels }).
let audioContext = new AudioContext({ sampleRate: audioState.sampleRate });
/** The tap rate the context was last matched to. */
let contextRate = audioState.sampleRate;

/** @type {HTMLCanvasElement | null} */
let canvas = null;
/** @type {any} */
let visualizer = null;
/** What is on screen. @type {PresetEntry | null} */
let current = null;
/** The Butterchurn preset loaded into the canvas, which may be hidden under a plugin. @type {PresetEntry | null} */
let loadedPreset = null;
/** The plugin on screen, if the current entry is one. @type {PluginFrame | null} */
let plugin = null;
/** Plugins that are fading out. They keep getting frames until they're gone. @type {Set<PluginFrame>} */
const leaving = new Set();
/** While the plugin on screen fades in, Butterchurn keeps drawing underneath until this time (ms). */
let coveredAt = 0;
/** Goes up with every change of preset, so a slow load that was overtaken can tell. */
let showToken = 0;
/** True while the next preset is being picked and loaded. */
let advancing = false;
/** Set when the settings or the library changed while that was going on. */
let reconcileAgain = false;
let lastRender = 0;
let nextBuildAt = 0;
let frames = 0;
let audioFrames = 0;
/** The widest the canvas may be. The Windows app raises it when the window spans several displays. */
let maxRenderWidth = MAX_RENDER_WIDTH;

function targetSize() {
  // The web view has no size until the window first opens; build for a common one meanwhile.
  return renderSize(window.innerWidth || 1920, window.innerHeight || 1080, window.devicePixelRatio, maxRenderWidth);
}

/** Creates a fresh canvas and Butterchurn instance, and loads the preset it had again. */
function build() {
  canvas?.remove();
  visualizer = null;
  canvas = document.createElement("canvas");
  canvas.addEventListener("webglcontextlost", onContextLost);
  // Plugin frames sit on top of the canvas.
  host.prepend(canvas);
  const { width, height } = targetSize();
  canvas.width = width;
  canvas.height = height;
  visualizer = butterchurn.createVisualizer(audioContext, canvas, { width, height, pixelRatio: 1 });
  if (loadedPreset?.preset) {
    try {
      visualizer.loadPreset(loadedPreset.preset, 0);
    } catch (error) {
      fail(loadedPreset, error);
    }
  }
  if (!current) showNext(0);
}

/** WebGL contexts can be lost after sleep and wake. The old canvas is gone for good, so start over. */
function onContextLost(event) {
  event.preventDefault();
  visualizer = null;
  nextBuildAt = 0;
}

function resize() {
  if (!visualizer || !canvas) return;
  const { width, height } = targetSize();
  if (canvas.width === width && canvas.height === height) return;
  canvas.width = width;
  canvas.height = height;
  visualizer.setRendererSize(width, height);
}

/** Records a failure, and moves on if the failed entry is the one on screen. */
function fail(entry, error) {
  const message = describeError(error);
  failures.add(entry, message);
  console.warn(`Failed to load ${entry.name}: ${message}`);
  if (loadedPreset?.id === entry.id) loadedPreset = null;
  if (current?.id === entry.id) {
    current = null;
    showNext(settings.blendSeconds);
  }
}

/** Fetches a custom preset from the presets folder and checks its shape. */
async function fetchPreset(entry) {
  const response = await fetch(entry.url);
  if (!response.ok) throw new Error(`Couldn't read the file (${response.status})`);
  let preset;
  try {
    preset = JSON.parse(await response.text());
  } catch {
    throw new Error("Not valid JSON");
  }
  if (!isPreset(preset)) throw new Error("Not a Butterchurn preset");
  return preset;
}

/** Fades the plugin on screen out and removes it once it's gone. */
function retirePlugin(blendSeconds) {
  const old = plugin;
  plugin = null;
  if (!old) return;
  old.fade(false, blendSeconds);
  leaving.add(old);
  setTimeout(() => {
    leaving.delete(old);
    old.remove();
  }, blendSeconds * 1000);
}

/**
 * Puts a preset or plugin on screen.
 * @param {PresetEntry | null} entry
 * @param {number} blendSeconds
 * @returns {Promise<boolean>} False if there was nothing to show or it failed to load.
 */
async function show(entry, blendSeconds) {
  if (!entry || failures.has(entry.id)) return false;
  const token = ++showToken;

  if (entry.kind === "plugin") {
    const frame = new PluginFrame(host, /** @type {string} */ (entry.url), (error) => {
      if (plugin === frame) plugin = null;
      leaving.delete(frame);
      fail(entry, error);
    });
    if (!(await frame.ready)) return false;
    if (token !== showToken) {
      // Something else was chosen while this plugin was starting.
      frame.remove();
      return true;
    }
    retirePlugin(blendSeconds);
    plugin = frame;
    frame.fade(true, blendSeconds);
    coveredAt = performance.now() + blendSeconds * 1000;
    current = entry;
    rotation.reset();
    return true;
  }

  if (!entry.preset) {
    try {
      entry.preset = await fetchPreset(entry);
    } catch (error) {
      fail(entry, error);
      return false;
    }
    if (token !== showToken) return true;
  }
  if (!visualizer) return false;
  try {
    // Under a plugin the canvas is hidden, so the blend is the plugin fading out instead.
    visualizer.loadPreset(entry.preset, plugin ? 0 : blendSeconds);
  } catch (error) {
    fail(entry, error);
    return false;
  }
  loadedPreset = entry;
  current = entry;
  rotation.reset();
  retirePlugin(blendSeconds);
  return true;
}

/** Shows the next preset from the shuffle pool, skipping ones that fail. */
async function showNext(blendSeconds) {
  if (advancing) return;
  advancing = true;
  try {
    // Bounded, so a library where everything fails can't loop forever.
    for (let tries = 0; tries < presets.length; tries++) {
      const id = shuffle.next();
      if (id !== null && (await show(byId.get(id) ?? null, blendSeconds))) return;
    }
  } finally {
    advancing = false;
    if (reconcileAgain) reconcile();
  }
}

/** The preset single mode asks for, or null in shuffle mode or when that preset is gone. */
function wanted() {
  return settings.mode === "single" ? (byId.get(settings.single) ?? null) : null;
}

/** Brings what's on screen in line with the settings and the library, after either changed. */
async function reconcile() {
  pool = new Set(shufflePool(presets, settings));
  shuffle.setIds([...pool], current?.id ?? null);
  // A pick that is under way may be from the old pool. Look again once it has landed.
  reconcileAgain = advancing;
  if (advancing) return;
  // Before the first frame nothing is on screen yet, so there's nothing to blend from.
  const blend = frames === 0 ? 0 : settings.blendSeconds;
  const single = wanted();
  if (single) {
    if (single.id !== current?.id && !(await show(single, blend))) showNext(blend);
  } else if (!current || !pool.has(current.id) || failures.has(current.id)) {
    // The preset on screen was just blocked, filtered out or deleted (or single mode points at nothing).
    showNext(blend);
  }
}

function tick(now) {
  requestAnimationFrame(tick);
  if (!shouldRender(now, lastRender)) return;
  const seconds = lastRender > 0 ? Math.min((now - lastRender) / 1000, MAX_FRAME_SECONDS) : 1 / 60;
  lastRender = now;

  if (!visualizer) {
    if (now < nextBuildAt) return;
    nextBuildAt = now + REBUILD_WAIT_MS;
    try {
      build();
    } catch (error) {
      console.warn("Butterchurn failed to start", error);
      visualizer = null;
      return;
    }
  }

  if (settings.mode === "shuffle" && rotation.tick(seconds)) showNext(settings.blendSeconds);
  // Butterchurn rests while a plugin fully covers it.
  if (!plugin || now < coveredAt) visualizer.render({ audioLevels: audioState.levels, elapsedTime: seconds });
  plugin?.post(audioState.audio);
  for (const frame of leaving) frame.post(audioState.audio);
  frames++;
}

/**
 * Called by Swift about 60 times a second with one packed, base64-encoded frame.
 * @param {unknown} base64
 */
function audioFrame(base64) {
  if (!decodeAudioFrame(base64, audioState)) return;
  audioFrames++;
  if (audioState.sampleRate !== contextRate) {
    // A different output device can change the tap's rate. Butterchurn reads it once, so start over.
    contextRate = audioState.sampleRate;
    try {
      const next = new AudioContext({ sampleRate: contextRate });
      audioContext.close();
      audioContext = next;
      visualizer = null;
      nextBuildAt = 0;
    } catch (error) {
      console.warn(`No AudioContext at ${contextRate} Hz; keeping ${audioContext.sampleRate} Hz`, error);
    }
  }
}

/**
 * Called by Swift with the preset controls, at load and whenever one changes in settings.
 * @param {unknown} value
 */
function setPresetSettings(value) {
  settings = parsePresetSettings(value);
  rotation.secondsPerPreset = settings.secondsPerPreset;
  reconcile();
}

/**
 * Called by Swift with the bundled plugins and the contents of the custom presets folder,
 * at load and whenever the folder changes.
 * @param {unknown} value
 */
function setCustomPresets(value) {
  const { entries, hung } = parseCustomPresets(value);
  const before = current ? byId.get(current.id) : undefined;
  presets = mergeLibrary(bundled, entries, byId);
  byId = new Map(presets.map((entry) => [entry.id, entry]));
  // A file that changed or went away gets a fresh start.
  failures.prune(byId);
  for (const id of hung) {
    const entry = byId.get(id);
    if (entry) failures.add(entry, "Stopped responding");
  }
  const after = current ? byId.get(current.id) : undefined;
  if (current && (!after || after.version !== before?.version)) {
    // The file on screen was deleted or edited: drop it, and show the new version if there is one.
    const replacement = after && !failures.has(after.id) ? after : null;
    current = null;
    loadedPreset = null;
    if (replacement) {
      show(replacement, settings.blendSeconds).then((shown) => {
        if (!shown) reconcile();
      });
      return;
    }
  } else if (after) {
    current = after;
    if (loadedPreset?.id === after.id) loadedPreset = after;
  }
  reconcile();
}

/** Called by Swift when the skip key is pressed. The seconds-per-preset count starts over with the new preset. */
function skipPreset() {
  const blend = skipBlendSeconds(settings);
  if (blend !== null) showNext(blend);
}

/**
 * Called by the Windows app with the render width cap for the displays the window covers.
 * @param {unknown} value
 */
function setRenderWidthCap(value) {
  maxRenderWidth = renderWidthCap(value);
  resize();
}

/** Swift asks the page how it's doing with this; the page has no way to call Swift. */
function idlevizStatus() {
  return { preset: current?.id ?? null, frames, audioFrames, presets: presets.length, failed: failures.list() };
}

/** Every preset and plugin the page can show, for the pickers in settings. */
function idlevizPresets() {
  return presets.map(({ id, name, source }) => ({ id, name, source }));
}
Object.assign(window, {
  audioFrame,
  idlevizPresets,
  idlevizStatus,
  setCustomPresets,
  setPresetSettings,
  setRenderWidthCap,
  skipPreset,
});

if (butterchurn && presets.length > 0) {
  try {
    // Built at load so the shaders are compiled before the window first opens.
    build();
  } catch (error) {
    console.warn("Butterchurn failed to start", error);
    visualizer = null;
  }
  window.addEventListener("resize", resize);
  requestAnimationFrame(tick);
} else {
  console.warn("Butterchurn or its presets are missing; the visualizer stays black");
}
