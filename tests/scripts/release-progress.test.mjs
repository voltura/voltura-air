import assert from "node:assert/strict";
import test from "node:test";

import { createReleaseProgress, formatDuration } from "../../scripts/release-progress.mjs";

test("release durations remain concise and readable", () => {
  assert.equal(formatDuration(499), "0s");
  assert.equal(formatDuration(65_000), "1m 05s");
  assert.equal(formatDuration(3_725_000), "1h 02m 05s");
});

test("release progress separates steps and reports successful total time", () => {
  let output = "";
  const times = [0, 0, 0, 65_000, 125_000];
  const progress = createReleaseProgress({
    totalSteps: 2,
    stream: { write: (value) => { output += value; } },
    clock: () => times.shift() ?? 125_000,
    useColor: false
  });

  progress.start("Creating installation packages", "Building both installers.");
  progress.complete();
  progress.success("Published as GitHub Latest: https://example.test/v1");

  assert.match(output, /Performing step 1 out of 2: Creating installation packages/u);
  assert.match(output, /Step 1 completed in 1m 05s/u);
  assert.match(output, /GREEN = SUCCESS/u);
  assert.match(output, /Total release time: 2m 05s/u);
});

test("release progress identifies the failed step in red issue output", () => {
  let output = "";
  const progress = createReleaseProgress({
    totalSteps: 6,
    stream: { write: (value) => { output += value; } },
    clock: () => 0,
    useColor: false
  });

  progress.start("Testing release", "Running checks.");
  progress.issue(new Error("Tests failed."));

  assert.match(output, /RED = ISSUE/u);
  assert.match(output, /Stopped during step 1 of 6: Testing release/u);
  assert.match(output, /Tests failed\./u);
});
