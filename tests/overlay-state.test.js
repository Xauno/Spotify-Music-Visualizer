import { describe, expect, it } from "vitest";
import {
  clampBrightness,
  formatTime,
  fraction,
  layoutFor,
  likePlacement,
  overlayRegions,
  parseNowPlaying,
  positionAt,
} from "../IdleViz/web/overlay-state.js";

const song = {
  id: "spotify:track:a",
  state: "playing",
  content: "song",
  title: "Riot",
  artist: "Hollywood Undead",
  artwork: null,
  artworkPending: false,
  durationMs: 228000,
  position: 17,
};

describe("layoutFor", () => {
  it("matches the state table in project.md", () => {
    expect(layoutFor(song)).toBe("full");
    expect(layoutFor({ ...song, state: "paused" })).toBe("progress");
    expect(layoutFor({ ...song, content: "musicAd" })).toBe("ad");
    expect(layoutFor({ ...song, content: "musicAd", state: "paused" })).toBe("ad");
    expect(layoutFor({ ...song, content: "podcast" })).toBe("none");
    expect(layoutFor({ ...song, content: "podcastAd" })).toBe("none");
    expect(layoutFor(null)).toBe("none");
  });

  it("shows nothing in any state while the overlay is switched off", () => {
    expect(layoutFor(song, false)).toBe("none");
    expect(layoutFor({ ...song, state: "paused" }, false)).toBe("none");
    expect(layoutFor({ ...song, content: "musicAd" }, false)).toBe("none");
    expect(layoutFor(song, true)).toBe("full");
  });
});

describe("likePlacement", () => {
  it("puts the heart beside the title only while the track block is showing", () => {
    expect(likePlacement(layoutFor(song))).toBe("title");
    expect(likePlacement(layoutFor({ ...song, state: "paused" }))).toBe("corner");
    expect(likePlacement(layoutFor({ ...song, content: "musicAd" }))).toBe("corner");
    expect(likePlacement(layoutFor({ ...song, content: "podcast" }))).toBe("corner");
    expect(likePlacement(layoutFor(null))).toBe("corner");
    expect(likePlacement(layoutFor(song, false))).toBe("corner");
  });
});

describe("clampBrightness", () => {
  it("keeps values inside the slider's range", () => {
    expect(clampBrightness(0.7)).toBe(0.7);
    expect(clampBrightness(1)).toBe(1);
    expect(clampBrightness(0.5)).toBe(0.5);
  });

  it("clamps values outside it", () => {
    expect(clampBrightness(0.1)).toBe(0.5);
    expect(clampBrightness(3)).toBe(1);
  });

  it("falls back to the default for anything that isn't a number", () => {
    expect(clampBrightness("0.9")).toBe(0.7);
    expect(clampBrightness(NaN)).toBe(0.7);
    expect(clampBrightness(undefined)).toBe(0.7);
  });
});

describe("positionAt", () => {
  it("advances while playing", () => {
    expect(positionAt(song, 1000, 3500)).toBeCloseTo(19.5);
  });

  it("stands still while paused", () => {
    expect(positionAt({ ...song, state: "paused" }, 1000, 60000)).toBe(17);
  });

  it("stops at the end of the track", () => {
    expect(positionAt(song, 0, 10_000_000)).toBe(228);
  });

  it("keeps counting when the length is unknown", () => {
    expect(positionAt({ ...song, durationMs: 0 }, 0, 5000)).toBe(22);
  });
});

describe("positionAt with an audio delay", () => {
  const item = /** @type {any} */ ({ state: "playing", position: 60, durationMs: 200_000 });

  it("shows the position you hear while playing", () => {
    expect(positionAt(item, 1000, 3000, 0.25)).toBeCloseTo(61.75);
    expect(positionAt(item, 1000, 3000)).toBeCloseTo(62);
  });

  it("never goes below zero at the start of a track", () => {
    expect(positionAt({ ...item, position: 0.5 }, 1000, 1000, 2)).toBe(0);
  });

  it("shows the exact position while paused", () => {
    expect(positionAt({ ...item, state: "paused" }, 1000, 9000, 2)).toBe(60);
  });

  it("ignores a delay that isn't a sensible number", () => {
    expect(positionAt(item, 1000, 1000, NaN)).toBe(60);
    expect(positionAt(item, 1000, 1000, -3)).toBe(60);
  });
});

describe("fraction", () => {
  it("is the played share, clamped", () => {
    expect(fraction(57, 228000)).toBeCloseTo(0.25);
    expect(fraction(500, 228000)).toBe(1);
    expect(fraction(-1, 228000)).toBe(0);
  });

  it("is 0 for a zero length", () => {
    expect(fraction(10, 0)).toBe(0);
  });
});

describe("formatTime", () => {
  it("formats like the TV app", () => {
    expect(formatTime(17)).toBe("00:17");
    expect(formatTime(228)).toBe("03:48");
    expect(formatTime(3723.9)).toBe("1:02:03");
    expect(formatTime(-5)).toBe("00:00");
    expect(formatTime(NaN)).toBe("00:00");
  });
});

describe("parseNowPlaying", () => {
  it("accepts a well-formed payload", () => {
    expect(parseNowPlaying(song)).toEqual(song);
  });

  it("rejects anything that isn't a known item", () => {
    expect(parseNowPlaying(null)).toBeNull();
    expect(parseNowPlaying("text")).toBeNull();
    expect(parseNowPlaying({ ...song, content: "video" })).toBeNull();
  });

  it("only takes image data URLs as artwork", () => {
    expect(parseNowPlaying({ ...song, artwork: "https://i.scdn.co/x" }).artwork).toBeNull();
    expect(parseNowPlaying({ ...song, artwork: "data:text/html,<b>" }).artwork).toBeNull();
    expect(parseNowPlaying({ ...song, artwork: "data:image/jpeg;base64,AAAA" }).artwork).toBe(
      "data:image/jpeg;base64,AAAA",
    );
  });

  it("fills bad fields with safe values", () => {
    const item = parseNowPlaying({ ...song, title: 5, durationMs: "x", position: Infinity, state: "stopped" });
    expect(item).toMatchObject({ title: "", durationMs: 0, position: 0, state: "playing" });
  });

  it("drops the pending flag once artwork is there", () => {
    expect(parseNowPlaying({ ...song, artwork: "data:image/png;base64,AA", artworkPending: true }).artworkPending).toBe(
      false,
    );
  });
});

describe("overlayRegions", () => {
  const whole = [{ left: 0, top: 0, width: 1720, height: 720, overlay: true }];

  it("is the whole window, with the overlay, until the app sends a layout", () => {
    expect(overlayRegions(null, 1720, 720)).toEqual(whole);
    expect(overlayRegions("nonsense", 1720, 720)).toEqual(whole);
    expect(overlayRegions({ width: 0, regions: [] }, 1720, 720)).toEqual(whole);
    expect(overlayRegions({ width: 3440, regions: [] }, 1720, 720)).toEqual(whole);
  });

  it("scales device pixels to CSS pixels by the window's width", () => {
    // A 1920 x 1080 display left of a 3440 x 1440 one, in a window at 200 %.
    const layout = {
      width: 5360,
      height: 1440,
      regions: [
        { x: 1920, y: 0, width: 3440, height: 1440, overlay: true },
        { x: 0, y: 360, width: 1920, height: 1080, overlay: false },
      ],
    };
    expect(overlayRegions(layout, 2680, 720)).toEqual([
      { left: 960, top: 0, width: 1720, height: 720, overlay: true },
      { left: 0, top: 180, width: 960, height: 540, overlay: false },
    ]);
  });

  it("drops regions that have no size or aren't numbers", () => {
    const layout = {
      width: 1000,
      regions: [
        null,
        { x: 0, y: 0, width: 0, height: 10 },
        { x: "a", y: 0, width: 5, height: 5 },
        { x: 0, y: 0, width: 500, height: 500 },
      ],
    };
    expect(overlayRegions(layout, 1000, 500)).toEqual([{ left: 0, top: 0, width: 500, height: 500, overlay: false }]);
  });
});
