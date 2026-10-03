import {
  NO_TRACK_GRACE_MS,
  STAGE_WIDTH,
  clampBrightness,
  formatTime,
  fraction,
  layoutFor,
  likePlacement,
  overlayRegions,
  parseNowPlaying,
  positionAt,
} from "./overlay-state.js";

const NOTE_PATH = "M9 18V5.5l12-2.5v12.5a3 3 0 1 1-2-2.83V7.4l-8 1.66v9.44a3 3 0 1 1-2-2.83z";
const HEART_PATH = "M12 21C7 17 3 13 3 8.5a4.5 4.5 0 0 1 9 0 4.5 4.5 0 0 1 9 0C21 13 17 17 12 21z";

/**
 * One display's part of the window, with its own copy of the overlay.
 * @typedef {object} RegionView
 * @property {HTMLElement} root
 * @property {HTMLElement} stage
 * @property {HTMLElement} tracks
 * @property {HTMLElement} played
 * @property {HTMLElement} elapsed
 * @property {HTMLElement} duration
 * @property {SVGElement} cornerHeart   For when the track block isn't showing. It sits outside the stage, which may be hidden.
 * @property {HTMLElement | null} currentTrack
 * @property {boolean} overlay          Whether the overlay shows on this display.
 */

const firstRegion = /** @type {HTMLElement} */ (document.querySelector(".region"));
/** An untouched copy, for the regions of further displays. */
const blankRegion = /** @type {HTMLElement} */ (firstRegion.cloneNode(true));
/** The last `setOverlayRegions` payload, kept for when the window changes size. @type {unknown} */
let regionLayout = null;

/** @type {import("./overlay-state.js").NowPlaying | null} */
let current = null;
let receivedAt = 0;
/** @type {ReturnType<typeof setTimeout> | undefined} */
let hideTimer;
/** Seconds the speakers lag behind Spotify, from Swift. */
let audioDelay = 0;
/** The "Show Spotify overlay" setting, from Swift. */
let overlayEnabled = true;

/**
 * @param {HTMLElement} root
 * @returns {RegionView}
 */
function makeView(root) {
  const find = (selector) => /** @type {HTMLElement} */ (root.querySelector(selector));
  const cornerHeart = makeHeart();
  cornerHeart.classList.add("like-corner");
  root.append(cornerHeart);
  return {
    root,
    stage: find(".stage"),
    tracks: find(".tracks"),
    played: find(".played"),
    elapsed: find(".elapsed"),
    duration: find(".duration"),
    cornerHeart,
    currentTrack: null,
    overlay: true,
  };
}

/** Places one region over each display the window covers and scales its stage to the display's width. */
function fitRegions() {
  const boxes = overlayRegions(regionLayout, window.innerWidth, window.innerHeight);
  while (views.length < boxes.length) {
    const root = /** @type {HTMLElement} */ (blankRegion.cloneNode(true));
    document.body.append(root);
    views.push(makeView(root));
  }
  while (views.length > boxes.length) views.pop()?.root.remove();
  boxes.forEach((box, index) => {
    const view = views[index];
    view.overlay = box.overlay;
    Object.assign(view.root.style, {
      left: `${box.left}px`,
      top: `${box.top}px`,
      right: "auto",
      bottom: "auto",
      width: `${box.width}px`,
      height: `${box.height}px`,
    });
    view.root.style.setProperty("--region-width", `${box.width}px`);
    view.stage.style.transform = `scale(${box.width / STAGE_WIDTH})`;
  });
}

/** The heart that confirms the like key. It stays hidden until `showLike` starts its animation. */
function makeHeart() {
  const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
  svg.setAttribute("viewBox", "0 0 24 24");
  svg.classList.add("like");
  const path = document.createElementNS("http://www.w3.org/2000/svg", "path");
  path.setAttribute("d", HEART_PATH);
  svg.append(path);
  svg.addEventListener("animationend", () => svg.classList.remove("liked", "unliked"));
  return svg;
}

/** @type {RegionView[]} */
const views = [makeView(firstRegion)];

function makeTrack(item) {
  const track = document.createElement("div");
  track.className = "track entering";
  track.dataset.id = item.id;

  const art = document.createElement("div");
  art.className = "art";
  const img = document.createElement("img");
  img.alt = "";
  img.addEventListener("load", () => img.classList.add("loaded"));
  const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
  svg.setAttribute("viewBox", "0 0 24 24");
  svg.classList.add("note");
  const path = document.createElementNS("http://www.w3.org/2000/svg", "path");
  path.setAttribute("d", NOTE_PATH);
  svg.append(path);
  art.append(img, svg);

  const text = document.createElement("div");
  text.className = "text";
  const title = document.createElement("div");
  title.className = "title";
  const artist = document.createElement("div");
  artist.className = "artist";
  const titleRow = document.createElement("div");
  titleRow.className = "title-row";
  titleRow.append(title, makeHeart());
  text.append(titleRow, artist);

  track.append(art, text);
  return track;
}

/** Fills a track block. Text uses textContent, so titles can't inject markup. */
function fillTrack(track, item) {
  /** @type {HTMLElement} */ (track.querySelector(".title")).textContent = item.title;
  /** @type {HTMLElement} */ (track.querySelector(".artist")).textContent = item.artist;
  const art = /** @type {HTMLElement} */ (track.querySelector(".art"));
  const img = /** @type {HTMLImageElement} */ (art.querySelector("img"));
  art.classList.toggle("missing", !item.artwork && !item.artworkPending);
  if (item.artwork && img.getAttribute("src") !== item.artwork) {
    img.classList.remove("loaded");
    img.src = item.artwork;
  } else if (!item.artwork) {
    img.removeAttribute("src");
    img.classList.remove("loaded");
  }
}

/**
 * Crossfades to a new track block when the item changes; updates in place otherwise.
 * @param {RegionView} view
 */
function showTrack(view, item) {
  if (view.currentTrack && view.currentTrack.dataset.id === item.id) {
    fillTrack(view.currentTrack, item);
    return;
  }
  const old = view.currentTrack;
  const next = makeTrack(item);
  fillTrack(next, item);
  view.tracks.append(next);
  view.currentTrack = next;
  // Start the fade-in on the next frame so the transition runs from the "entering" state.
  requestAnimationFrame(() => requestAnimationFrame(() => next.classList.remove("entering")));
  if (old) {
    old.classList.add("leaving");
    old.addEventListener("transitionend", () => old.remove(), { once: true });
  }
}

function renderProgress() {
  if (!current) return;
  const position = positionAt(current, receivedAt, performance.now(), audioDelay);
  for (const view of views) {
    view.played.style.transform = `scaleX(${fraction(position, current.durationMs)})`;
    view.elapsed.textContent = formatTime(position);
    view.duration.textContent = formatTime(current.durationMs / 1000);
  }
}

/** @param {RegionView} view */
function layoutOn(view) {
  return layoutFor(current, overlayEnabled && view.overlay);
}

function render() {
  for (const view of views) {
    const { stage } = view;
    const layout = layoutOn(view);
    if (layout === "none") {
      // Hide the overlay but keep the last content in place while it fades.
      stage.classList.add("hidden");
      continue;
    }
    stage.classList.remove("hidden", "progress-only", "ad");
    if (layout === "progress") stage.classList.add("progress-only");
    if (layout === "ad") stage.classList.add("ad");
    if (layout !== "ad") showTrack(view, current);
  }
  renderProgress();
}

/**
 * Called by Swift with the current Spotify item, or null when there's no track.
 * @param {unknown} payload
 */
function nowPlaying(payload) {
  const item = parseNowPlaying(typeof payload === "string" ? safeParse(payload) : payload);
  clearTimeout(hideTimer);
  if (!item) {
    // Spotify reports "no track" for a moment when switching context, so wait before hiding.
    hideTimer = setTimeout(() => {
      current = null;
      render();
    }, NO_TRACK_GRACE_MS);
    return;
  }
  current = item;
  receivedAt = performance.now();
  render();
}

function safeParse(text) {
  try {
    return JSON.parse(text);
  } catch {
    return null;
  }
}

window.addEventListener("resize", fitRegions);
fitRegions();
// Only the time labels and bar move between readings; a few updates a second is plenty.
setInterval(renderProgress, 250);

/**
 * Called by Swift with the audio delay of the current speakers or headphones.
 * @param {unknown} seconds
 */
function setAudioDelay(seconds) {
  audioDelay = typeof seconds === "number" && Number.isFinite(seconds) ? Math.min(Math.max(seconds, 0), 2.5) : 0;
  renderProgress();
}

/**
 * Called by Swift with the "Show Spotify overlay" setting.
 * @param {unknown} enabled
 */
function setOverlayEnabled(enabled) {
  overlayEnabled = enabled !== false;
  render();
}

/**
 * Called by Swift with the brightness setting. The dim layer's opacity follows the variable.
 * @param {unknown} value
 */
function setBrightness(value) {
  document.documentElement.style.setProperty("--viz-brightness", String(clampBrightness(value)));
}

/**
 * Called by Swift when the like key added the preset on screen to the favorites (a filled heart)
 * or took it off again (an outline).
 * @param {unknown} liked
 */
function showLike(liked) {
  for (const other of document.querySelectorAll(".like")) other.classList.remove("liked", "unliked");
  for (const view of views) {
    const beside = likePlacement(layoutOn(view)) === "title" ? view.currentTrack?.querySelector(".like") : null;
    const heart = beside ?? view.cornerHeart;
    // Reading the layout lets the animation start over when the key is pressed again mid-fade.
    heart.getBoundingClientRect();
    heart.classList.add(liked === false ? "unliked" : "liked");
  }
}

/**
 * Called by the Windows app when its window covers several displays: where each display is
 * inside the window, and which of them show the overlay.
 * @param {unknown} layout
 */
function setOverlayRegions(layout) {
  regionLayout = layout;
  fitRegions();
  render();
}

Object.assign(window, { nowPlaying, setAudioDelay, setOverlayEnabled, setOverlayRegions, setBrightness, showLike });
