import assert from "node:assert/strict";
import { readFile, readdir } from "node:fs/promises";
import test from "node:test";

const expectedScreenshots = [
  "voltura-air-host-dark.png",
  "voltura-air-host.png",
  "voltura-air-iphone-dark.png",
  "voltura-air-iphone-kodi-dark.png",
  "voltura-air-iphone.png",
  "voltura-air-split.png"
];

const screenshotPattern = /voltura-air-(?:host|iphone|split)[a-z-]*\.png/gu;

function extractScreenshots(contents) {
  return [...new Set(contents.match(screenshotPattern) ?? [])].sort();
}

test("public screenshot inventory stays curated and aligned", async () => {
  const [captureScript, hostProgram, runbook, readme, marketingPage, assetFiles] = await Promise.all([
    readFile(new URL("../../scripts/capture-site-screenshots.mjs", import.meta.url), "utf8"),
    readFile(new URL("../../apps/windows-host/Program.cs", import.meta.url), "utf8"),
    readFile(new URL("../../docs/screenshots.md", import.meta.url), "utf8"),
    readFile(new URL("../../README.md", import.meta.url), "utf8"),
    readFile(new URL("../../docs/site/index.php", import.meta.url), "utf8"),
    readdir(new URL("../../docs/site/assets/", import.meta.url))
  ]);

  assert.deepEqual(extractScreenshots(captureScript), expectedScreenshots);
  assert.deepEqual(extractScreenshots(runbook), expectedScreenshots);
  assert.deepEqual(extractScreenshots(assetFiles.join("\n")), expectedScreenshots);

  assert.deepEqual(extractScreenshots(`${readme}\n${marketingPage}`), expectedScreenshots);
  assert.match(captureScript, /"bin", "cli", "Debug", "net10\.0-windows"/u);
  assert.match(captureScript, /"--site-screenshot-mode"[\s\S]*"--isolated-test-mode"/u);
  assert.match(hostProgram, /BeginIsolatedScope\(\)[\s\S]*SetHighDpiMode/u);
  assert.match(captureScript, /getByRole\("button", \{ name: "Remote", exact: true \}\)/u);
  assert.match(captureScript, /DwmGetWindowAttributeUInt\(\$hwnd, 37,/u);
  assert.match(captureScript, /\$rect\.Left \+= \$borderInset/u);
  assert.match(captureScript, /\$rect\.Bottom -= \$borderInset/u);
  assert.equal(marketingPage.match(/<figure class="screen-card/gu)?.length, 4);
  assert.equal(marketingPage.match(/<picture>/gu)?.length, 2);
  assert.equal(readme.match(/<picture>/gu)?.length, 2);
});
