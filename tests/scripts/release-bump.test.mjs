import assert from "node:assert/strict";
import test from "node:test";

import { getNextReleaseVersion } from "../../scripts/bump-release.mjs";

test("release bump advances the patch component below nine", () => {
  assert.equal(getNextReleaseVersion("0.6.7"), "0.6.8");
});

test("release bump carries patch and minor components at nine", () => {
  assert.equal(getNextReleaseVersion("0.6.9"), "0.7.0");
  assert.equal(getNextReleaseVersion("0.9.9"), "1.0.0");
  assert.equal(getNextReleaseVersion("12.9.9"), "13.0.0");
});

test("release bump requires an unambiguous stable odometer version", () => {
  assert.throws(() => getNextReleaseVersion("0.6.7-beta.1"), /stable version/u);
  assert.throws(() => getNextReleaseVersion("0.6.10"), /single-digit/u);
});
