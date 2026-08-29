import assert from "node:assert/strict";
import { readFile, readdir } from "node:fs/promises";
import test from "node:test";

const expectedScreenshots = [
  "voltura-air-host-custom-screens-dark.png",
  "voltura-air-host-custom-screens.png",
  "voltura-air-host-dark.png",
  "voltura-air-host.png",
  "voltura-air-files-dark.png",
  "voltura-air-files.png",
  "voltura-air-iphone-dark.png",
  "voltura-air-iphone-kodi-dark.png",
  "voltura-air-iphone.png",
  "voltura-air-screen-view.png",
  "voltura-air-split.png",
].sort();

const expectedSiteAssets = [
  ...expectedScreenshots,
  "voltura-air-iphone-kodi-dark-forum.png",
].sort();

const screenshotPattern = /voltura-air-(?:files|host|iphone|screen-view|split)[a-z-]*\.png/gu;

function extractScreenshots(contents) {
  return [...new Set(contents.match(screenshotPattern) ?? [])].sort();
}

test("public screenshot inventory stays curated and aligned", async () => {
  const [
    captureScript,
    hostProgram,
    hostScreenshot,
    wpfRenderer,
    runbook,
    readme,
    marketingPage,
    uiTokens,
    assetFiles,
  ] = await Promise.all([
    readFile(new URL("../../scripts/capture-site-screenshots.mjs", import.meta.url), "utf8"),
    readFile(new URL("../../apps/windows-host/Program.cs", import.meta.url), "utf8"),
    readFile(new URL("../../apps/windows-host/MainWindow.xaml.cs", import.meta.url), "utf8"),
    readFile(new URL("../../apps/windows-host/WpfPngRenderer.cs", import.meta.url), "utf8"),
    readFile(new URL("../../docs/screenshots.md", import.meta.url), "utf8"),
    readFile(new URL("../../README.md", import.meta.url), "utf8"),
    readFile(new URL("../../apps/public-site/index.php", import.meta.url), "utf8"),
    readFile(new URL("../../assets/ui-tokens.json", import.meta.url), "utf8"),
    readdir(new URL("../../apps/public-site/assets/", import.meta.url)),
  ]);

  assert.deepEqual(extractScreenshots(captureScript), expectedSiteAssets);
  assert.deepEqual(extractScreenshots(runbook), expectedSiteAssets);
  assert.deepEqual(extractScreenshots(assetFiles.join("\n")), expectedSiteAssets);

  assert.deepEqual(extractScreenshots(`${readme}\n${marketingPage}`), expectedScreenshots);
  assert.match(captureScript, /\.resize\(\{ width: 350 \}\)[\s\S]*outputs\.iphoneKodiDarkForum/u);
  assert.match(captureScript, /"bin",\s*"cli",\s*"Debug",\s*"net10\.0-windows"/u);
  assert.match(
    captureScript,
    /"--site-screenshot-mode"[\s\S]*"--site-screenshot-output"[\s\S]*"--isolated-test-mode"/u,
  );
  assert.match(
    hostProgram,
    /SetHighDpiMode\(Forms\.HighDpiMode\.PerMonitorV2\)[\s\S]*BeginIsolatedScope\(\)/u,
  );
  assert.match(
    hostProgram,
    /offscreenSiteScreenshot[\s\S]*if \(!offscreenSiteScreenshot\)[\s\S]*startupWindow\.Show\(\)/u,
  );
  assert.match(hostProgram, /RenderSiteScreenshotAsync\(args, siteScreenshotOutput\)/u);
  assert.match(
    hostScreenshot,
    /WpfPngRenderer\.SaveAsync\(WindowScrollViewer, Background, outputPath, size\)/u,
  );
  assert.match(wpfRenderer, /RenderTargetBitmap/u);
  assert.match(wpfRenderer, /VisualBrush\(visual\)/u);
  assert.match(wpfRenderer, /PngBitmapEncoder/u);
  assert.doesNotMatch(
    captureScript,
    /capture-window\.ps1|CopyFromScreen|DwmGetWindowAttribute|MainWindowHandle|SetForegroundWindow/u,
  );
  assert.match(captureScript, /waitForTrackpadVolume\(page\)[\s\S]*iphoneLight/u);
  assert.match(captureScript, /\.trackpad-mode \.volume-control/u);
  assert.match(captureScript, /getByRole\("button", \{ name: "Remote", exact: true \}\)/u);
  assert.match(captureScript, /mode: "kodi", startKodi: false/u);
  assert.match(
    captureScript,
    /getByText\("Switch modes from here\.", \{ exact: true \}\)[\s\S]*state: "visible"[\s\S]*state: "hidden"[\s\S]*page\.screenshot\(\{ path: outputs\.iphoneKodiDark \}\)/u,
  );
  assert.match(captureScript, /filesPreview=1[\s\S]*file-manager-workspace/u);
  assert.match(captureScript, /--screen-view-only/u);
  assert.match(captureScript, /screenPreview=1[\s\S]*screen-view-workspace/u);
  assert.match(captureScript, /voltura-air-screen-view\.png/u);
  assert.match(captureScript, /roundScreenshot\(desktop, 1280, 720, 18\)/u);
  assert.match(captureScript, /\{ input: framedDesktop, left: 160, top: 110 \}/u);
  assert.match(
    captureScript,
    /font-family="Inter,ui-sans-serif,system-ui,-apple-system,BlinkMacSystemFont,&quot;Segoe UI&quot;,sans-serif" fill="\$\{accent\}" font-size="48" font-weight="800" letter-spacing="5\.76"><text x="70" y="76">WINDOWS 11 PC<\/text><text x="70" y="914">VIEW PC SCREEN ON PHONE<\/text><\/g><rect x="70" y="940"/u,
  );
  assert.match(
    captureScript,
    /uiTokensPath = path\.join\(repoRoot, "assets", "ui-tokens\.json"\)[\s\S]*hostDarkPalette = JSON\.parse\(await fs\.readFile\(uiTokensPath, "utf8"\)\)\.color\.dark/u,
  );
  for (const token of [
    "accent",
    "accentStrong",
    "bg",
    "border",
    "muted",
    "surface",
    "surfaceRaised",
    "text",
  ]) {
    assert.equal(typeof JSON.parse(uiTokens).color.dark[token], "string");
  }
  assert.match(
    captureScript,
    /finally \{\s*if \(!screenViewOnly\) await stopRunningHost\(\);\s*\}/u,
  );
  assert.match(
    captureScript,
    /const lightHost = await launchHost[\s\S]*try \{[\s\S]*filePreview = await launchFilePreview[\s\S]*finally \{[\s\S]*stopPreviewProcess[\s\S]*finally \{[\s\S]*stopProcess\(lightHost\.process\)/u,
  );
  assert.match(
    captureScript,
    /\[\s*\["light", outputs\.filesLight\],\s*\["dark", outputs\.filesDark\],?\s*\]/u,
  );
  assert.match(captureScript, /viewport: \{ width: 1180, height: 820 \}[\s\S]*hasTouch: true/u);
  assert.equal(marketingPage.match(/<figure class="screen-card/gu)?.length, 7);
  assert.equal(marketingPage.match(/<picture>/gu)?.length, 4);
  assert.equal(readme.match(/<picture>/gu)?.length, 4);
});
