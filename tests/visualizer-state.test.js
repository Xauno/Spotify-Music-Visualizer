import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { runInNewContext } from "node:vm";
import { describe, expect, it } from "vitest";
import {
  DEFAULT_SETTINGS,
  FailureLog,
  MAX_RENDER_WIDTH,
  MAX_SPAN_RENDER_WIDTH,
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
} from "../IdleViz/web/visualizer-state.js";

/** A repeatable stand-in for Math.random. */
function seeded(seed) {
  let state = seed;
  return () => {
    state = (state * 1664525 + 1013904223) % 4294967296;
    return state / 4294967296;
  };
}

describe("collectPresets", () => {
  it("merges packs, sorted by name without regard to case", () => {
    const list = collectPresets([{ beta: { b: 1 }, Alpha: { a: 1 } }, { gamma: { c: 1 } }]);
    expect(list.map((p) => p.name)).toEqual(["Alpha", "beta", "gamma"]);
    expect(list[0]).toEqual({
      id: "bundled:Alpha",
      name: "Alpha",
      source: "bundled",
      kind: "preset",
      preset: { a: 1 },
    });
  });

  it("keeps a repeated name once, from the first pack", () => {
    const list = collectPresets([{ same: { from: 1 } }, { same: { from: 2 } }]);
    expect(list).toHaveLength(1);
    expect(list[0].preset).toEqual({ from: 1 });
  });

  it("skips missing packs and entries that aren't objects", () => {
    expect(collectPresets([undefined, null, { ok: {}, bad: "text", none: null }]).map((p) => p.name)).toEqual(["ok"]);
  });

  it("tags the source", () => {
    expect(collectPresets([{ mine: {} }], "custom")[0]).toMatchObject({ id: "custom:mine", source: "custom" });
  });
});

describe("parseCustomPresets", () => {
  const preset = {
    id: "custom:Pack/Tunnel.json",
    name: "Tunnel",
    source: "custom",
    kind: "preset",
    url: "idleviz-app://presets/Pack/Tunnel.json",
    version: "120-5",
  };
  const plugin = {
    id: "bundled:visuals/aurora.js",
    name: "Aurora Ring",
    source: "bundled",
    kind: "plugin",
    url: "idleviz-app://app/visuals/aurora.js",
    version: "1",
  };

  it("keeps well-formed entries and the hung list", () => {
    expect(parseCustomPresets({ entries: [preset, plugin], hung: ["custom:loop.js"] })).toEqual({
      entries: [preset, plugin],
      hung: ["custom:loop.js"],
    });
  });

  it("drops entries of the wrong shape and repeated ids", () => {
    const entries = [
      null,
      "text",
      { ...preset, id: "" },
      { ...preset, name: 5 },
      { ...preset, kind: "milk" },
      { ...preset, source: "elsewhere" },
      preset,
      { ...preset, name: "Tunnel again" },
    ];
    expect(parseCustomPresets({ entries }).entries).toEqual([preset]);
  });

  it("only lets each source load from its own place", () => {
    const entries = [
      { ...preset, url: "https://example.com/x.json" },
      { ...preset, url: "idleviz-app://app/overlay.js" },
      { ...plugin, url: "idleviz-app://presets/aurora.js" },
      { ...preset, id: "bundled:Tunnel" },
      { ...plugin, id: "custom:aurora.js" },
    ];
    expect(parseCustomPresets({ entries }).entries).toEqual([]);
  });

  it("accepts the addresses the Windows app serves, under the same rule", () => {
    const windowsPreset = { ...preset, url: "https://presets.idleviz.invalid/Pack/Tunnel.json" };
    const windowsPlugin = { ...plugin, url: "https://app.idleviz.invalid/visuals/aurora.js" };
    const entries = [
      windowsPreset,
      windowsPlugin,
      { ...preset, id: "custom:a.json", url: "https://app.idleviz.invalid/visuals/aurora.js" },
      { ...plugin, id: "bundled:b.js", url: "https://presets.idleviz.invalid/aurora.js" },
      { ...plugin, id: "bundled:c.js", url: "https://app.idleviz.invalid/overlay.js" },
      { ...preset, id: "custom:d.json", url: "https://presets.idleviz.invalid.example.com/x.json" },
      { ...preset, id: "custom:e.json", url: "http://presets.idleviz.invalid/x.json" },
    ];
    expect(parseCustomPresets({ entries }).entries).toEqual([windowsPreset, windowsPlugin]);
  });

  it("reads anything else as empty", () => {
    expect(parseCustomPresets(null)).toEqual({ entries: [], hung: [] });
    expect(parseCustomPresets({ entries: "x", hung: [3, "", "custom:a.js"] })).toEqual({
      entries: [],
      hung: ["custom:a.js"],
    });
    expect(parseCustomPresets({ entries: [{ ...preset, version: 7 }] }).entries[0].version).toBe("");
  });
});

describe("isPreset", () => {
  it("wants baseVals, shapes and waves", () => {
    expect(isPreset({ baseVals: {}, shapes: [], waves: [] })).toBe(true);
    expect(isPreset({ baseVals: {}, shapes: [] })).toBe(false);
    expect(isPreset({ baseVals: 1, shapes: [], waves: [] })).toBe(false);
    expect(isPreset([])).toBe(false);
    expect(isPreset(null)).toBe(false);
    expect(isPreset("preset")).toBe(false);
  });
});

describe("mergeLibrary", () => {
  const bundled = collectPresets([{ Beta: {}, delta: {} }]);
  const entry = (name, version = "1") => ({
    id: `custom:${name}.json`,
    name,
    source: /** @type {const} */ ("custom"),
    kind: /** @type {const} */ ("preset"),
    url: `idleviz-app://presets/${name}.json`,
    version,
  });

  it("sorts bundled and custom together by name", () => {
    expect(mergeLibrary(bundled, [entry("gamma"), entry("Alpha")]).map((p) => p.name)).toEqual([
      "Alpha",
      "Beta",
      "delta",
      "gamma",
    ]);
  });

  it("keeps a fetched preset while its file is unchanged", () => {
    const loaded = { ...entry("Alpha"), preset: { baseVals: {} } };
    const previous = new Map([[loaded.id, loaded]]);
    expect(mergeLibrary(bundled, [entry("Alpha")], previous)[0].preset).toBe(loaded.preset);
    expect(mergeLibrary(bundled, [entry("Alpha", "2")], previous)[0].preset).toBeUndefined();
  });

  it("never lets a custom entry replace a bundled one", () => {
    const imposter = { ...entry("x"), id: "bundled:Beta", name: "Imposter" };
    expect(mergeLibrary(bundled, [imposter]).map((p) => p.name)).toEqual(["Beta", "delta"]);
  });
});

describe("FailureLog", () => {
  const entry = (id, version) => ({ id, name: id, source: "custom", kind: "preset", url: "", version });

  it("lists failures with their errors, cut short", () => {
    const log = new FailureLog();
    log.add(entry("custom:a.json", "1"), "Not a Butterchurn preset");
    log.add(entry("custom:b.js", "1"), "x".repeat(1000));
    expect(log.has("custom:a.json")).toBe(true);
    expect(log.has("custom:c.json")).toBe(false);
    expect(log.list()[0]).toEqual({ id: "custom:a.json", error: "Not a Butterchurn preset" });
    expect(log.list()[1].error).toHaveLength(300);
  });

  it("forgets a failure when the file changes or goes away, and keeps it otherwise", () => {
    const log = new FailureLog();
    log.add(entry("custom:same.json", "1"), "bad");
    log.add(entry("custom:edited.json", "1"), "bad");
    log.add(entry("custom:deleted.json", "1"), "bad");
    log.add({ id: "bundled:Old", name: "Old", source: "bundled", kind: "preset", preset: {} }, "bad");
    const library = new Map(
      [entry("custom:same.json", "1"), entry("custom:edited.json", "2"), { id: "bundled:Old" }].map((e) => [e.id, e]),
    );
    log.prune(/** @type {any} */ (library));
    expect(log.list().map((f) => f.id)).toEqual(["custom:same.json", "bundled:Old"]);
  });
});

describe("the bundled packs", () => {
  const vendor = resolve(import.meta.dirname, "..", "IdleViz", "web", "vendor");
  const load = (file, name) => {
    const sandbox = { self: {} };
    runInNewContext(readFileSync(resolve(vendor, file), "utf8"), sandbox);
    return sandbox.self[name].getPresets();
  };

  it("give 395 presets, each with the parts Butterchurn needs", () => {
    const list = collectPresets([
      load("butterchurnPresets.min.js", "butterchurnPresets"),
      load("butterchurnPresetsExtra.min.js", "butterchurnPresetsExtra"),
      load("butterchurnPresetsExtra2.min.js", "butterchurnPresetsExtra2"),
      load("butterchurnPresetsMD1.min.js", "butterchurnPresetsMD1"),
    ]);
    expect(list).toHaveLength(395);
    expect(new Set(list.map((p) => p.id)).size).toBe(395);
    // The packs run in their own VM context, so check shapes rather than `instanceof`.
    const incomplete = list.filter(
      ({ preset }) =>
        typeof preset.baseVals !== "object" || !Array.isArray(preset.shapes) || !Array.isArray(preset.waves),
    );
    expect(incomplete.map((p) => p.name)).toEqual([]);
  });
});

describe("ShuffleBag", () => {
  const ids = ["a", "b", "c", "d", "e"];

  it("goes through every id before repeating one", () => {
    const bag = new ShuffleBag(ids, seeded(1));
    for (let round = 0; round < 4; round++) {
      const seen = ids.map(() => bag.next());
      expect([...seen].sort()).toEqual(ids);
    }
  });

  it("never shows the same id twice in a row across rounds", () => {
    for (let seed = 1; seed <= 50; seed++) {
      const bag = new ShuffleBag(ids, seeded(seed));
      let last = null;
      for (let i = 0; i < 40; i++) {
        const id = bag.next();
        expect(id).not.toBe(last);
        last = id;
      }
    }
  });

  it("mixes the order", () => {
    const bag = new ShuffleBag(ids, seeded(7));
    const first = ids.map(() => bag.next());
    const second = ids.map(() => bag.next());
    expect(first).not.toEqual(second);
  });

  it("repeats a single id and returns null when empty", () => {
    const one = new ShuffleBag(["only"]);
    expect([one.next(), one.next()]).toEqual(["only", "only"]);
    expect(new ShuffleBag([]).next()).toBeNull();
  });
});

describe("ShuffleBag.setIds", () => {
  it("starts a fresh round from the new ids without repeating the one on screen", () => {
    for (let seed = 1; seed <= 30; seed++) {
      const bag = new ShuffleBag(["a", "b", "c"], seeded(seed));
      bag.next();
      bag.setIds(["x", "y"], "x");
      expect(bag.next()).toBe("y");
      expect(bag.next()).toBe("x");
    }
  });

  it("keeps its own last id when none is given", () => {
    const bag = new ShuffleBag(["a", "b"], seeded(3));
    const last = bag.next();
    bag.setIds(["a", "b"]);
    expect(bag.next()).not.toBe(last);
  });
});

describe("parsePresetSettings", () => {
  it("keeps a full payload", () => {
    const payload = {
      mode: "single",
      single: "bundled:A",
      shuffleFrom: "favorites",
      secondsPerPreset: 120,
      blendSeconds: 0,
      favorites: ["bundled:A"],
      blocked: ["bundled:B"],
    };
    expect(parsePresetSettings(payload)).toEqual(payload);
  });

  it("falls back to the defaults for anything missing or wrong", () => {
    expect(parsePresetSettings(null)).toEqual(DEFAULT_SETTINGS);
    expect(parsePresetSettings("text")).toEqual(DEFAULT_SETTINGS);
    expect(
      parsePresetSettings({
        mode: "sideways",
        single: 7,
        shuffleFrom: "everything",
        secondsPerPreset: 0,
        blendSeconds: -1,
        favorites: "bundled:A",
        blocked: [1, "bundled:B", null],
      }),
    ).toEqual({ ...DEFAULT_SETTINGS, blocked: ["bundled:B"] });
    expect(parsePresetSettings({ secondsPerPreset: NaN, blendSeconds: Infinity })).toEqual(DEFAULT_SETTINGS);
  });

  it("matches the defaults in mockups.html", () => {
    expect(DEFAULT_SETTINGS).toMatchObject({
      mode: "shuffle",
      shuffleFrom: "all",
      secondsPerPreset: 30,
      blendSeconds: 2.7,
    });
  });
});

describe("shufflePool", () => {
  const presets = [...collectPresets([{ A: {}, B: {}, C: {} }]), ...collectPresets([{ X: {}, Y: {} }], "custom")];
  const pool = (overrides) => shufflePool(presets, { ...DEFAULT_SETTINGS, ...overrides });

  it("takes everything by default", () => {
    expect(pool({})).toEqual(["bundled:A", "bundled:B", "bundled:C", "custom:X", "custom:Y"]);
  });

  it("filters by source", () => {
    expect(pool({ shuffleFrom: "bundled" })).toEqual(["bundled:A", "bundled:B", "bundled:C"]);
    expect(pool({ shuffleFrom: "custom" })).toEqual(["custom:X", "custom:Y"]);
  });

  it("takes only favorites", () => {
    expect(pool({ shuffleFrom: "favorites", favorites: ["custom:Y", "bundled:B", "gone:Z"] })).toEqual([
      "bundled:B",
      "custom:Y",
    ]);
  });

  it("leaves blocked presets out of every source", () => {
    const blocked = ["bundled:A", "custom:X"];
    expect(pool({ blocked })).toEqual(["bundled:B", "bundled:C", "custom:Y"]);
    expect(pool({ blocked, shuffleFrom: "custom" })).toEqual(["custom:Y"]);
    expect(pool({ blocked, shuffleFrom: "favorites", favorites: ["bundled:A", "bundled:B"] })).toEqual(["bundled:B"]);
  });

  it("widens an empty choice instead of leaving nothing to show", () => {
    expect(pool({ shuffleFrom: "favorites" })).toHaveLength(5);
    expect(pool({ shuffleFrom: "favorites", blocked: ["bundled:A"] })).toHaveLength(4);
    expect(shufflePool(collectPresets([{ A: {} }]), { ...DEFAULT_SETTINGS, shuffleFrom: "custom" })).toEqual([
      "bundled:A",
    ]);
    expect(pool({ blocked: presets.map((p) => p.id) })).toHaveLength(5);
  });
});

describe("skipBlendSeconds", () => {
  it("blends quickly in shuffle mode, whatever the blend time setting is", () => {
    expect(skipBlendSeconds(parsePresetSettings({ mode: "shuffle", blendSeconds: 8 }))).toBe(0.5);
    expect(skipBlendSeconds(parsePresetSettings({ mode: "shuffle", blendSeconds: 0 }))).toBe(0.5);
  });

  it("has nothing to skip to in single mode", () => {
    expect(skipBlendSeconds(parsePresetSettings({ mode: "single", single: "bundled:a" }))).toBeNull();
  });
});

describe("Rotation", () => {
  it("is due once per period of rendered time", () => {
    const rotation = new Rotation(30);
    let due = 0;
    for (let frame = 0; frame < 60 * 95; frame++) if (rotation.tick(1 / 60)) due++;
    expect(due).toBe(3);
  });

  it("starts over on reset", () => {
    const rotation = new Rotation(10);
    expect(rotation.tick(9)).toBe(false);
    rotation.reset();
    expect(rotation.tick(9)).toBe(false);
    expect(rotation.tick(1)).toBe(true);
  });
});

describe("renderSize", () => {
  it("uses the device pixel ratio below the cap", () => {
    expect(renderSize(1024, 640, 2)).toEqual({ width: 2048, height: 1280 });
  });

  it("scales down to the cap and keeps the aspect ratio", () => {
    // A 1512 × 982 pt MacBook Pro display at 2× would be 3024 px wide.
    expect(renderSize(1512, 982, 2)).toEqual({ width: MAX_RENDER_WIDTH, height: 1663 });
    expect(renderSize(3840, 2160, 1)).toEqual({ width: 2560, height: 1440 });
  });

  it("never returns an empty size", () => {
    expect(renderSize(0, 0, 2)).toEqual({ width: 2, height: 2 });
    expect(renderSize(100, 50, 0)).toEqual({ width: 100, height: 50 });
  });
});

describe("renderWidthCap", () => {
  it("is the normal cap unless the app asks for more", () => {
    expect(renderWidthCap(undefined)).toBe(MAX_RENDER_WIDTH);
    expect(renderWidthCap("4000")).toBe(MAX_RENDER_WIDTH);
    expect(renderWidthCap(Number.NaN)).toBe(MAX_RENDER_WIDTH);
    expect(renderWidthCap(800)).toBe(MAX_RENDER_WIDTH);
  });

  it("follows the app's cap for a span, up to a limit", () => {
    expect(renderWidthCap(3988.8)).toBe(3989);
    expect(renderWidthCap(100_000)).toBe(MAX_SPAN_RENDER_WIDTH);
    // A 5360 px span of a 3440 px and a 1920 px display: the wide display's part is still 2560 px.
    expect(renderSize(5360, 1440, 1, renderWidthCap(3989))).toEqual({ width: 3989, height: 1072 });
  });
});

describe("shouldRender", () => {
  it("renders every frame at 60 Hz, jitter included", () => {
    expect(shouldRender(16.7, 0)).toBe(true);
    expect(shouldRender(15.1, 0)).toBe(true);
  });

  it("skips every other frame at 120 Hz", () => {
    let last = 0;
    let rendered = 0;
    for (let frame = 1; frame <= 120; frame++) {
      const now = frame * (1000 / 120);
      if (shouldRender(now, last)) {
        rendered++;
        last = now;
      }
    }
    expect(rendered).toBe(60);
  });
});
