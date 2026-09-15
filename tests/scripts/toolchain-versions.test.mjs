import assert from "node:assert/strict";
import test from "node:test";
import { supportsToolVersion } from "../../scripts/toolchain-versions.mjs";

test("patch updates stay on the supported stable line and respect its minimum", () => {
  for (const version of ["7.6.6", "7.6.7", "7.6.20"]) {
    assert.equal(supportsToolVersion(version, [7, 6, 6], "patch-line"), true, version);
  }
  for (const version of ["7.6.5", "7.5.20", "7.7.0", "8.0.0", "7.6.7-preview.1", "nonsense"]) {
    assert.equal(supportsToolVersion(version, [7, 6, 6], "patch-line"), false, version);
  }
  assert.equal(supportsToolVersion("12.0.3", [12, 0, 2], "patch-line"), true);
  assert.equal(supportsToolVersion("12.1.0", [12, 0, 2], "patch-line"), false);
});

test("Node LTS updates stay on the supported major and SDK updates stay in their feature band", () => {
  for (const version of ["24.20.0", "24.20.1", "24.21.0"]) {
    assert.equal(supportsToolVersion(version, [24, 20, 0], "major-line"), true, version);
  }
  for (const version of ["24.19.9", "22.21.0", "25.0.0", "24.21.0-rc.1"]) {
    assert.equal(supportsToolVersion(version, [24, 20, 0], "major-line"), false, version);
  }
  assert.equal(supportsToolVersion("10.0.401", [10, 0, 400], "feature-band"), true);
  assert.equal(supportsToolVersion("10.0.500", [10, 0, 400], "feature-band"), false);
  assert.equal(supportsToolVersion("10.0.399", [10, 0, 400], "feature-band"), false);
  assert.equal(supportsToolVersion("10.0.12", [10, 0, 11], "patch-line"), true);
  assert.equal(supportsToolVersion("11.0.0", [10, 0, 11], "patch-line"), false);
});

test("minimum and exact tool requirements retain their boundaries", () => {
  assert.equal(supportsToolVersion("v3.12", [3, 12, 0], "minimum"), true);
  assert.equal(supportsToolVersion("18.10.12201.205", [18, 9, 0], "minimum"), true);
  assert.equal(supportsToolVersion("8.5.10", [8, 5, 9], "minimum"), true);
  assert.equal(supportsToolVersion("8.5.8", [8, 5, 9], "minimum"), false);
  assert.equal(supportsToolVersion("12.0.2", [12, 0, 2]), true);
  assert.equal(supportsToolVersion("12.0.3", [12, 0, 2]), false);
});
