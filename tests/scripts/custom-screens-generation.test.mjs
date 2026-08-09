import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { readFileSync } from "node:fs";
import { spawnSync } from "node:child_process";
import test from "node:test";
import path from "node:path";
import { officialScreens, packageFilenames } from "../../scripts/custom-screens/catalog.mjs";
import { validateDefinition } from "../../scripts/custom-screens/builders/validation.mjs";

const root = path.resolve(import.meta.dirname, "../..");
const bundle = path.join(root, "artifacts", "custom-screens", "voltura-official-screens.zip");

test("official custom-screen definitions cover the deterministic fourteen-screen catalog", () => {
  assert.equal(officialScreens.length, 14);
  assert.equal(packageFilenames.size, 14);
  const metadataKeys = [
    "category", "id", "longDescription", "minimumVolturaAirVersion", "name",
    "official", "optionalTargetApplication", "requiredCapabilities", "shortDescription", "tags"
  ];
  for (const definition of officialScreens) {
    validateDefinition(definition);
    assert.deepEqual(Object.keys(definition.metadata).sort(), metadataKeys);
    assert.doesNotMatch(JSON.stringify(definition), /unified\s*remote|unifiedremote/iu);
    const actions = definition.screen.sections.flatMap(section => section.buttons.map(button => button.action.kind));
    if (actions.includes("urlOpen")) assert.ok(definition.metadata.requiredCapabilities.includes("urlOpen"));
    if (actions.includes("knownApp")) assert.ok(definition.metadata.requiredCapabilities.includes("remoteAppLaunch"));
    if (actions.includes("hostAction")) assert.ok(definition.metadata.requiredCapabilities.includes("hostActions"));
  }
  assert.equal(new Set(officialScreens.map(item => item.screen.id)).size, 14);
});

test("official bundle generation is byte deterministic", () => {
  const generate = () => {
    const result = spawnSync(process.execPath, ["scripts/generate-custom-screens.mjs", "--official"], { cwd: root, encoding: "utf8" });
    assert.equal(result.status, 0, result.stderr);
    return createHash("sha256").update(readFileSync(bundle)).digest("hex");
  };
  assert.equal(generate(), generate());
});

test("official application action maps match their current desktop targets", () => {
  const screen = id => officialScreens.find(item => item.screen.id === id).screen;
  const buttons = id => screen(id).sections.flatMap(section => section.buttons);
  const button = (id, suffix) => buttons(id).find(item => item.id === `${id}.${suffix}`);

  assert.deepEqual(button("official.vlc", "back10").action, {
    kind: "shortcut", key: "ArrowLeft", modifiers: ["Shift"]
  });
  assert.deepEqual(button("official.vlc", "ahead10").action.modifiers, ["Shift"]);
  assert.deepEqual(
    ["previous", "playPause", "next", "stop"].map(suffix => button("official.vlc", suffix).action.key),
    ["P", "Space", "N", "S"]);

  assert.deepEqual(
    screen("official.plex").sections.map(section => section.kind),
    ["buttons", "buttons", "buttons", "volume"]);
  assert.equal(button("official.plex", "select"), undefined);
  assert.equal(button("official.plex", "info"), undefined);
  assert.deepEqual(button("official.plex", "back").action.modifiers, ["Alt"]);
  assert.equal(button("official.plex", "back").action.key, "ArrowLeft");
  assert.equal(button("official.plex", "forward").action.key, "ArrowRight");
  assert.equal(button("official.plex", "fullscreen").action.key, "F11");

  assert.deepEqual(button("official.zoom", "share").action.modifiers, ["Alt"]);
  assert.equal(button("official.zoom", "record").label, "Local record");

  for (const id of ["official.netflix", "official.primeVideo"]) {
    assert.equal(screen(id).sections.some(section => section.kind === "dpad"), false);
    assert.deepEqual(
      ["playPause", "seekBack", "seekForward", "fullscreen"].map(suffix => button(id, suffix).action.key),
      ["Space", "ArrowLeft", "ArrowRight", "F"]);
  }

  for (const id of ["official.disneyPlus", "official.twitch"]) {
    assert.deepEqual(screen(id).sections.map(section => section.kind),
      ["buttons", "collapsibleTrackpad", "volume"]);
    assert.equal(buttons(id).some(item => item.action.kind === "shortcut"), false);
    assert.equal(buttons(id).some(item => item.action.kind === "urlOpen"), true);
  }

  assert.equal(button("official.windowsPhotos", "launch").action.actionId, "windowsPhotos");
});
