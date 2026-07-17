import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const packageJson = JSON.parse(readFileSync(new URL("../../package.json", import.meta.url), "utf8"));
const releaseWorkflow = readFileSync(new URL("../../.github/workflows/release.yml", import.meta.url), "utf8");
const qualityWorkflow = readFileSync(new URL("../../.github/workflows/quality.yml", import.meta.url), "utf8");

test("documentation coverage runs in the root and pull-request quality gates", () => {
  assert.equal(packageJson.scripts.test.split(" && ")[0], "npm run docs:check");
  assert.match(qualityWorkflow, /run: npm run docs:check/u);
});

test("full maintenance stops the host before deleting locked build outputs", () => {
  assert.equal(packageJson.scripts.clean, undefined);

  const steps = packageJson.scripts["maintenance:full"].split(" && ");

  assert.equal(steps[0], "npm run cache:purge");
  assert.ok(steps.indexOf("npm run cache:purge") < steps.indexOf("npm run clean:temp"));
  assert.ok(steps.indexOf("npm run clean:temp") < steps.indexOf("npm run clean:git"));
  assert.ok(steps.indexOf("npm run clean:git") < steps.indexOf("npm run deps:update"));
});

test("release publication rejects existing tags and asset replacement", () => {
  assert.match(releaseWorkflow, /Reject an existing release tag/u);
  assert.match(releaseWorkflow, /"release", "create"/u);
  assert.match(releaseWorkflow, /--prerelease/u);
  assert.doesNotMatch(releaseWorkflow, /force-with-lease|tag --force|--clobber|release upload/u);
});
