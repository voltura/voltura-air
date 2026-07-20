import assert from "node:assert/strict";
import test from "node:test";

import { runFullRelease, useAutomaticPublication } from "../../scripts/release-full.mjs";

test("full release uses automatic publication only when requested", () => {
  assert.equal(useAutomaticPublication([]), false);
  assert.equal(useAutomaticPublication(["auto"]), true);
});

test("full release rejects unsupported arguments", () => {
  assert.throws(() => useAutomaticPublication(["--auto"]), /Usage/u);
  assert.throws(() => useAutomaticPublication(["auto", "extra"]), /Usage/u);
});

test("full release runs release preparation, branding, and site publication in order", async () => {
  const calls = [];
  const run = (command, args, options) => {
    calls.push([command, args]);
    return options?.captureOutput ? "" : undefined;
  };

  await runFullRelease([], { run });

  assert.deepEqual(calls, [
    ["git", ["status", "--porcelain=v1"]],
    ["npm", ["run", "release:bump"]],
    ["npm", ["run", "branding:generate"]],
    ["npm", ["run", "publish:site"]]
  ]);
});

test("full release stops before any release action when the working tree is dirty", async () => {
  const calls = [];
  const run = (command, args, options) => {
    calls.push([command, args]);
    return options?.captureOutput ? " M package.json" : undefined;
  };

  await assert.rejects(() => runFullRelease([], { run }), /clean Git working tree/u);
  assert.deepEqual(calls, [["git", ["status", "--porcelain=v1"]]]);
});

test("automatic full release commits the resolved version and pushes after publication", async () => {
  const calls = [];
  const run = (command, args, options) => {
    calls.push([command, args]);
    return options?.captureOutput ? "" : undefined;
  };

  await runFullRelease(["auto"], { run, readVersion: async () => "0.6.8" });

  assert.deepEqual(calls, [
    ["git", ["status", "--porcelain=v1"]],
    ["git", ["rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{upstream}"]],
    ["git", ["var", "GIT_AUTHOR_IDENT"]],
    ["npm", ["run", "release:bump"]],
    ["npm", ["run", "branding:generate"]],
    ["npm", ["run", "publish:site"]],
    ["git", ["add", "--all"]],
    ["git", ["commit", "-m", "Release version 0.6.8"]],
    ["git", ["push"]]
  ]);
});
