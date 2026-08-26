import assert from "node:assert/strict";
import test from "node:test";

import { createDevProgress, writeDevReady } from "../../scripts/dev-progress.mjs";

test("quick development progress separates timed startup steps", () => {
  let output = "";
  const times = [0, 0, 0, 65_000];
  const progress = createDevProgress({
    totalSteps: 3,
    stream: {
      write: (value) => {
        output += value;
      },
    },
    clock: () => times.shift() ?? 65_000,
    useColor: false,
  });

  progress.start("Building mobile client", "Rebuilding without validation.");
  progress.complete();

  assert.match(output, /Performing step 1 out of 3: Building mobile client/u);
  assert.match(output, /Total elapsed: 0s/u);
  assert.match(output, /Step 1 completed in 1m 05s/u);
});

test("quick development readiness reports host and total startup time", () => {
  let output = "";

  writeDevReady({
    stepDurationMilliseconds: 65_000,
    totalDurationMilliseconds: 125_000,
    stream: {
      write: (value) => {
        output += value;
      },
    },
    useColor: false,
  });

  assert.match(output, /Step 3 completed in 1m 05s/u);
  assert.match(output, /Development host ready in 2m 05s total\./u);
});
