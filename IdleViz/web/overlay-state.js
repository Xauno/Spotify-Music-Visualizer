// Pure overlay logic, kept free of the DOM so it runs under Vitest.

/** How long "no track" has to last before the overlay hides. Spotify briefly reports
 *  no track while switching to a new album or playlist. */
export const NO_TRACK_GRACE_MS = 1500;
/** Matches `--viz-brightness` in overlay.css. */
export const DEFAULT_BRIGHTNESS = 0.7;

/**
 * @typedef {object} NowPlaying
 * @property {string} id          Spotify URL, used to spot track changes.
 * @property {"playing" | "paused"} state
 * @property {"song" | "podcast" | "musicAd" | "podcastAd"} content
 * @property {string} title
 * @property {string} artist
 * @property {string | null} artwork   `data:` URL, or null.
 * @property {boolean} artworkPending  True while Swift is still downloading it.
 * @property {number} durationMs
 * @property {number} position         Seconds, when Swift read it.
 */

/**
 * Which layout to show.
 * - "full": art, title, artist and progress (song, playing)
 * - "progress": progress row only (song, paused)
 * - "ad": "Advertisement" label and progress (ad between songs)
 * - "none": nothing (podcasts, podcast ads, no track)
 * @param {NowPlaying | null} item
 * @param {boolean} [overlayEnabled]  The "Show Spotify overlay" setting. Off means nothing in any state.
 * @returns {"full" | "progress" | "ad" | "none"}
 */
export function layoutFor(item, overlayEnabled = true) {
  if (!item || !overlayEnabled) return "none";
  switch (item.content) {
    case "song":
      return item.state === "paused" ? "progress" : "full";
    case "musicAd":
      return "ad";
    default:
      return "none";
  }
}

/**
 * Where the heart that confirms the like key goes: beside the title while the track block is
 * showing, and in the top-right corner in every other layout.
 * @param {ReturnType<typeof layoutFor>} layout
 * @returns {"title" | "corner"}
 */
export function likePlacement(layout) {
  return layout === "full" ? "title" : "corner";
}

/**
 * Current position in seconds, advanced locally from the last reading while playing.
 * @param {NowPlaying} item
 * @param {number} receivedAt  `performance.now()` when the reading arrived.
 * @param {number} now         `performance.now()`.
 * @param {number} [audioDelay]  Seconds the speakers lag behind Spotify (Bluetooth, AirPlay). While
 *   playing, what you hear is that far behind the position Spotify reports.
 */
export function positionAt(item, receivedAt, now, audioDelay = 0) {
  const playing = item.state === "playing";
  const elapsed = playing ? Math.max(0, now - receivedAt) / 1000 : 0;
  const lag = playing && Number.isFinite(audioDelay) ? Math.max(0, audioDelay) : 0;
  const position = Math.max(0, item.position + elapsed - lag);
  const duration = item.durationMs / 1000;
  return duration > 0 ? Math.min(position, duration) : position;
}

/** Played fraction, 0..1. A zero duration (podcasts right after they start) reads as 0. */
export function fraction(position, durationMs) {
  if (!(durationMs > 0)) return 0;
  return Math.min(1, Math.max(0, (position * 1000) / durationMs));
}

/** "00:17", "03:48", or "1:02:03" past an hour, as on the TV. */
export function formatTime(seconds) {
  const total = Math.max(0, Math.floor(Number.isFinite(seconds) ? seconds : 0));
  const h = Math.floor(total / 3600);
  const m = Math.floor((total % 3600) / 60);
  const s = total % 60;
  const pad = (n) => String(n).padStart(2, "0");
  return h > 0 ? `${h}:${pad(m)}:${pad(s)}` : `${pad(m)}:${pad(s)}`;
}

/**
 * Checks the shape of a `nowPlaying` payload from Swift. Returns null for anything else.
 * @param {unknown} value
 * @returns {NowPlaying | null}
 */
export function parseNowPlaying(value) {
  if (!value || typeof value !== "object") return null;
  const item = /** @type {Record<string, unknown>} */ (value);
  const text = (v) => (typeof v === "string" ? v : "");
  const number = (v) => (typeof v === "number" && Number.isFinite(v) ? v : 0);
  const state = item.state === "paused" ? "paused" : "playing";
  const contents = ["song", "podcast", "musicAd", "podcastAd"];
  if (!contents.includes(/** @type {string} */ (item.content))) return null;
  const artwork = typeof item.artwork === "string" && item.artwork.startsWith("data:image/") ? item.artwork : null;
  return {
    id: text(item.id),
    state,
    content: /** @type {NowPlaying["content"]} */ (item.content),
    title: text(item.title),
    artist: text(item.artist),
    artwork,
    artworkPending: item.artworkPending === true && !artwork,
    durationMs: number(item.durationMs),
    position: number(item.position),
  };
}

/**
 * The brightness setting as a number the dim layer can use: 0.5 to 1, or the default for anything else.
 * @param {unknown} value
 */
export function clampBrightness(value) {
  if (typeof value !== "number" || !Number.isFinite(value)) return DEFAULT_BRIGHTNESS;
  return Math.min(Math.max(value, 0.5), 1);
}

/** The overlay is laid out on a stage this wide and scaled to the display. */
export const STAGE_WIDTH = 1920;
const MAX_REGIONS = 16;

/**
 * One display's part of the window.
 * @typedef {object} Region
 * @property {number} left
 * @property {number} top
 * @property {number} width
 * @property {number} height
 * @property {boolean} overlay  Whether the Spotify overlay shows on this display.
 */

/**
 * Where the displays are inside the window, in CSS pixels. A window that covers one display, as
 * on the Mac, never gets a layout: it is one region, the whole window, with the overlay.
 * The app sends the layout in device pixels, with the size of the whole window to scale by.
 * @param {unknown} value        A `setOverlayRegions` payload.
 * @param {number} innerWidth    The window's size in CSS pixels.
 * @param {number} innerHeight
 * @returns {Region[]}
 */
export function overlayRegions(value, innerWidth, innerHeight) {
  const whole = [{ left: 0, top: 0, width: innerWidth, height: innerHeight, overlay: true }];
  const layout = /** @type {Record<string, unknown>} */ (value && typeof value === "object" ? value : {});
  const width = Number(layout.width);
  if (!Number.isFinite(width) || width <= 0 || !Array.isArray(layout.regions)) return whole;
  const scale = innerWidth / width;
  const regions = [];
  for (const raw of layout.regions.slice(0, MAX_REGIONS)) {
    if (!raw || typeof raw !== "object") continue;
    const box = [raw.x, raw.y, raw.width, raw.height].map(Number);
    if (!box.every(Number.isFinite) || box[2] <= 0 || box[3] <= 0) continue;
    regions.push({
      left: box[0] * scale,
      top: box[1] * scale,
      width: box[2] * scale,
      height: box[3] * scale,
      overlay: raw.overlay === true,
    });
  }
  return regions.length > 0 ? regions : whole;
}
