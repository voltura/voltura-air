import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const packageJson = JSON.parse(readFileSync(new URL("../../package.json", import.meta.url), "utf8"));
const mobilePackageJson = JSON.parse(readFileSync(new URL("../../apps/mobile-web/package.json", import.meta.url), "utf8"));
const releaseWorkflow = readFileSync(new URL("../../.github/workflows/release.yml", import.meta.url), "utf8");
const qualityWorkflow = readFileSync(new URL("../../.github/workflows/quality.yml", import.meta.url), "utf8");
const devScript = readFileSync(new URL("../../scripts/dev.mjs", import.meta.url), "utf8");
const devHostScript = readFileSync(new URL("../../scripts/dev-host.mjs", import.meta.url), "utf8");

test("documentation coverage runs in the root and pull-request quality gates", () => {
  assert.equal(packageJson.scripts.test.split(" && ")[0], "npm run docs:check");
  assert.match(qualityWorkflow, /run: npm run docs:check/u);
});

test("strong source-size warnings require current reviews in pull-request quality", () => {
  assert.equal(packageJson.scripts["size:check"], "node scripts/report-source-sizes.mjs --check");
  assert.match(qualityWorkflow, /run: npm run size:check/u);
});

test("host partial ownership runs in root and pull-request quality gates", () => {
  assert.equal(packageJson.scripts["host:ownership:check"], "node scripts/check-host-partial-ownership.mjs");
  assert.match(packageJson.scripts.test, /npm run host:ownership:check/u);
  assert.match(qualityWorkflow, /run: npm run host:ownership:check/u);
});

test("the production mobile build enforces its measured JavaScript budget", () => {
  assert.match(mobilePackageJson.scripts.build, /vite build && npm run bundle:check/u);
  assert.equal(mobilePackageJson.scripts["bundle:check"], "node ../../scripts/check-mobile-bundle-size.mjs");
});

test("quick phone development rebuilds the host-served client without validation", () => {
  assert.equal(packageJson.scripts["dev:quick"], "node scripts/dev.mjs --quick");
  assert.equal(mobilePackageJson.scripts["build:quick"], "vite build");
  assert.match(devScript, /process\.argv\.includes\("--quick"\)/u);
  assert.match(devScript, /childEnv\.VOLTURA_AIR_USE_VITE_CLIENT = "0"/u);
  assert.match(devScript, /delete childEnv\.VOLTURA_AIR_CLIENT_URL/u);
  assert.match(devScript, /if \(quickStart\)[\s\S]*runCommand\("npm", \["run", "build:quick"/u);
  assert.match(devScript, /if \(!quickStart\)[\s\S]*vite\.js/u);
  assert.match(devHostScript, /if \(useViteClient\)[\s\S]*else \{\s*await waitForClientFiles\(\)/u);
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
