import test from "node:test";
import { readFileSync, existsSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import assert from "node:assert/strict";
import {
  features,
  devices,
  modes,
  goals,
  permissions,
  evaluate,
  getFeatures,
  cleanState,
  setupSteps,
  permissionState,
} from "../../apps/public-site/feature-explorer/catalog.mjs";
const feature = (id) => features.find((f) => f.id === id);
const state = (patch) => ({ ...cleanState(), ...patch });
test("Standard Local preserves useful tools but blocks secure browser APIs", () => {
  for (const id of ["screen", "trackpad", "files", "change", "upload", "photo", "terminal"])
    assert.notEqual(evaluate(feature(id), state({ mode: "local" })).status, "blocked", id);
  for (const id of ["gyro", "webcam", "microphone", "download", "screenshot", "recording"])
    assert.ok(
      evaluate(feature(id), state({ mode: "local" })).blockers.some((b) => b.code === "https"),
      id,
    );
});
test("Remote controls permissions remain separate from capabilities and optional access", () => {
  for (const id of ["trackpad", "apps", "presentation", "power"])
    assert.notEqual(evaluate(feature(id), state({ profile: "remote" })).status, "blocked", id);
  for (const id of ["screen", "files", "upload", "terminal", "webcam", "awake", "diagnostics"])
    assert.equal(evaluate(feature(id), state({ profile: "remote" })).status, "blocked", id);
  assert.equal(permissionState("screen", "remote"), "Blocked");
  assert.equal(permissionState("launch", "remote"), "Allowed");
});
test("Assistant requires exactly My device and local recording does not require file transfer", () => {
  for (const profile of ["custom", "remote"])
    assert.ok(
      evaluate(feature("assistant"), state({ profile })).blockers.some((b) => b.code === "profile"),
    );
  assert.deepEqual(feature("recording").permissions, ["screen"]);
  assert.deepEqual(feature("screenshot").permissions, ["screen", "transfer"]);
  assert.deepEqual(feature("upload").permissions, ["browse", "change", "transfer"]);
});
test("Device limitations and Custom permissions are expressed honestly", () => {
  assert.ok(
    evaluate(feature("gyro"), state({ device: "computer" })).blockers.some(
      (b) => b.code === "device",
    ),
  );
  assert.equal(evaluate(feature("desktop"), state({ device: "iphone" })).status, "conditional");
  assert.equal(evaluate(feature("screen"), state({ profile: "custom" })).status, "conditional");
  assert.equal(permissionState("screen", "custom"), "Check on PC");
});
test("Every combination has coherent results, permissions and a complete setup path", () => {
  let combinations = 0;
  for (const device of Object.keys(devices))
    for (const mode of Object.keys(modes))
      for (const profile of ["my", "remote", "custom"])
        for (const goal of goals) {
          const s = { device, mode, profile, goal: goal.id };
          const list = getFeatures(s);
          assert.ok(list.length);
          for (const f of list) {
            const r = evaluate(f, s);
            assert.ok(["available", "conditional", "blocked"].includes(r.status));
            assert.equal(r.status === "blocked", r.blockers.length > 0);
            assert.ok(setupSteps(f, s).length >= 5);
            for (const key of f.permissions) assert.ok(permissions[key]);
          }
          combinations++;
        }
  assert.equal(combinations, 360);
});
test("Search respects goal and matches permissions; malformed URLs reset safely", () => {
  assert.ok(getFeatures(state({ goal: "files" }), "photo").some((f) => f.id === "photo"));
  assert.equal(getFeatures(state({ goal: "camera" }), "PowerShell").length, 0);
  assert.ok(getFeatures(state(), "Transfer files").some((f) => f.id === "screenshot"));
  assert.deepEqual(
    cleanState({ device: "bogus", mode: "bogus", profile: "bogus", goal: "bogus" }),
    cleanState(),
  );
  assert.deepEqual(cleanState({ device: "__proto__", mode: "constructor" }), cleanState());
  assert.equal(new Set(features.map((f) => f.id)).size, features.length);
});

test("public route is self-contained, indexable and linked from the site", () => {
  const root = fileURLToPath(new URL("../../", import.meta.url));
  const directory = path.join(root, "apps/public-site/feature-explorer");
  const html = readFileSync(path.join(directory, "index.html"), "utf8");
  assert.match(
    html,
    /<link rel="canonical" href="https:\/\/voltura\.se\/air\/feature-explorer\/"/u,
  );
  assert.doesNotMatch(html, /noindex|chatgpt\.site|Sign in with ChatGPT/iu);
  assert.match(html, /<noscript\b/u);
  for (const [, target] of html.matchAll(/(?:src|href)="([^"#]+)"/gu)) {
    if (/^[a-z]+:/iu.test(target)) continue;
    assert.ok(existsSync(path.resolve(directory, target)), target);
  }
  const config = readFileSync(path.join(directory, ".htaccess"), "utf8");
  assert.match(config, /DirectoryIndex index\.html/u);
  assert.match(config, /AddType text\/javascript \.mjs/u);
  for (const file of ["index.php", "sitemap.php", "sitemap.xml", "llms.txt"]) {
    assert.ok(
      readFileSync(path.join(root, "apps/public-site", file), "utf8").includes("feature-explorer/"),
      file,
    );
  }
});
