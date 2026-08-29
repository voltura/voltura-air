import { createRequire } from "node:module";
import { fileURLToPath } from "node:url";
import { existsSync } from "node:fs";
import fs from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import sharp from "sharp";
import { spawn } from "node:child_process";
import { stopChild, stopExistingHost } from "./dev-shared.mjs";

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const uiTokensPath = path.join(repoRoot, "assets", "ui-tokens.json");
const assetsDir = path.join(repoRoot, "apps", "public-site", "assets");
const tempDir = path.join(os.tmpdir(), "voltura-air-site-screenshots");
const tempAppDataDir = path.join(tempDir, "appdata");
const pairingUrlFile = path.join(tempDir, "pairing-url.txt");
const hostExe = path.join(
  repoRoot,
  "apps",
  "windows-host",
  "bin",
  "cli",
  "Debug",
  "net10.0-windows",
  "VolturaAir.Host.exe",
);

const outputs = {
  hostLight: path.join(assetsDir, "voltura-air-host.png"),
  hostDark: path.join(assetsDir, "voltura-air-host-dark.png"),
  hostCustomScreensLight: path.join(assetsDir, "voltura-air-host-custom-screens.png"),
  hostCustomScreensDark: path.join(assetsDir, "voltura-air-host-custom-screens-dark.png"),
  iphoneLight: path.join(assetsDir, "voltura-air-iphone.png"),
  iphoneDark: path.join(assetsDir, "voltura-air-iphone-dark.png"),
  iphoneKodiDark: path.join(assetsDir, "voltura-air-iphone-kodi-dark.png"),
  iphoneKodiDarkForum: path.join(assetsDir, "voltura-air-iphone-kodi-dark-forum.png"),
  split: path.join(assetsDir, "voltura-air-split.png"),
  filesLight: path.join(assetsDir, "voltura-air-files.png"),
  filesDark: path.join(assetsDir, "voltura-air-files-dark.png"),
  screenView: path.join(assetsDir, "voltura-air-screen-view.png"),
};

const screenViewOnly = process.argv.slice(2).includes("--screen-view-only");
const hostDarkPalette = JSON.parse(await fs.readFile(uiTokensPath, "utf8")).color.dark;

main().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});

async function main() {
  const unknownArguments = process.argv
    .slice(2)
    .filter((argument) => argument !== "--screen-view-only");
  if (unknownArguments.length > 0) {
    throw new Error(`Unknown screenshot capture argument: ${unknownArguments.join(", ")}`);
  }

  if (!screenViewOnly && process.platform !== "win32") {
    throw new Error("Site screenshot capture must run on Windows because it renders the WPF host.");
  }

  await fs.mkdir(tempDir, { recursive: true });
  await fs.mkdir(tempAppDataDir, { recursive: true });
  await fs.mkdir(assetsDir, { recursive: true });
  await ensureCaptureDependencies();

  try {
    const requireFromTemp = createRequire(path.join(tempDir, "package.json"));
    const { chromium } = requireFromTemp("playwright");

    if (screenViewOnly) {
      let mobilePreview;
      try {
        mobilePreview = await launchFilePreview();
        await captureScreenViewShowcase(chromium, mobilePreview.url);
      } finally {
        if (mobilePreview) await stopPreviewProcess(mobilePreview.process);
      }
      console.log(`Screen View screenshot written to ${outputs.screenView}`);
      return;
    }

    await stopRunningHost();
    await run("npm", ["run", "build", "--workspace", "apps/mobile-web"]);
    await run("dotnet", ["build", "VolturaAir.slnx"]);

    await renderHostScreenshot("Dark", outputs.hostDark);
    await renderHostScreenshot("Dark", outputs.hostCustomScreensDark, true);

    const lightHost = await launchHost("Light", outputs.hostLight);
    try {
      let filePreview;
      try {
        filePreview = await launchFilePreview();
        await captureMobileScreens(chromium, lightHost.pairingUrl, filePreview.url);
        await captureScreenViewShowcase(chromium, filePreview.url);
        await sharp(outputs.iphoneKodiDark)
          .resize({ width: 350 })
          .png()
          .toFile(outputs.iphoneKodiDarkForum);
      } finally {
        if (filePreview) await stopPreviewProcess(filePreview.process);
      }
    } finally {
      await stopProcess(lightHost.process);
    }

    await renderHostScreenshot("Light", outputs.hostCustomScreensLight, true);

    console.log(`Site screenshots written to ${assetsDir}`);
  } finally {
    if (!screenViewOnly) await stopRunningHost();
  }
}

async function captureScreenViewShowcase(chromium, previewUrl) {
  const browser = await launchBrowser(chromium);
  try {
    const desktop = await sharp(Buffer.from(fictionalWindowsDesktopSvg())).png().toBuffer();
    const desktopDataUrl = `data:image/png;base64,${desktop.toString("base64")}`;
    const phone = await captureScreenViewPhone(browser, previewUrl, desktopDataUrl);
    const [framedDesktop, framedPhone] = await Promise.all([
      roundScreenshot(desktop, 1280, 720, 18),
      roundScreenshot(phone, 1392, 616, 108),
    ]);
    await sharp(Buffer.from(screenViewShowcaseSvg()))
      .composite([
        { input: framedDesktop, left: 160, top: 110 },
        { input: framedPhone, left: 104, top: 968 },
      ])
      .png({ compressionLevel: 9 })
      .toFile(outputs.screenView);
  } finally {
    await browser.close();
  }
}

async function roundScreenshot(input, width, height, radius) {
  const mask = Buffer.from(
    `<svg width="${width}" height="${height}"><rect width="100%" height="100%" rx="${radius}" fill="white"/></svg>`,
  );
  return sharp(input)
    .resize(width, height, { fit: "fill" })
    .composite([{ input: mask, blend: "dest-in" }])
    .png()
    .toBuffer();
}

async function captureScreenViewPhone(browser, previewUrl, desktopDataUrl) {
  const context = await browser.newContext({
    viewport: { width: 844, height: 390 },
    deviceScaleFactor: 2,
    isMobile: true,
    hasTouch: true,
  });
  await context.addInitScript(() => {
    localStorage.setItem("voltura-air.themeMode", "dark");
  });
  try {
    const page = await context.newPage();
    await page.goto(`${previewUrl}/?screenPreview=1`, { waitUntil: "networkidle" });
    await page.locator(".screen-view-workspace").waitFor({ timeout: 5000 });
    await page.addStyleTag({
      content: `
        .screen-view-browser-preview .screen-view-video {
          background: ${hostDarkPalette.bg} url("${desktopDataUrl}") center / contain no-repeat !important;
        }
      `,
    });
    return await page.screenshot({ type: "png" });
  } finally {
    await context.close();
  }
}

function fictionalWindowsDesktopSvg() {
  const { accent, accentStrong, bg, border, muted, surface, surfaceRaised, text } = hostDarkPalette;
  return String.raw`<svg xmlns="http://www.w3.org/2000/svg" width="1440" height="810" viewBox="0 0 1440 810"><defs><linearGradient id="wall" x2="1" y2="1"><stop stop-color="${bg}"/><stop offset=".48" stop-color="${surfaceRaised}"/><stop offset="1" stop-color="${accentStrong}"/></linearGradient><linearGradient id="tile" x2="1" y2="1"><stop stop-color="${surfaceRaised}"/><stop offset="1" stop-color="${accentStrong}"/></linearGradient><filter id="shadow"><feDropShadow dx="0" dy="18" stdDeviation="22" flood-color="${bg}" flood-opacity=".7"/></filter></defs><rect width="1440" height="810" fill="url(#wall)"/><ellipse cx="1030" cy="180" rx="500" ry="220" fill="${accent}" opacity=".22"/><ellipse cx="510" cy="760" rx="650" ry="310" fill="${accentStrong}" opacity=".34"/><g font-family="Segoe UI,Arial" text-anchor="middle" fill="${text}" font-size="13"><g transform="translate(36 30)"><rect width="48" height="48" rx="10" fill="url(#tile)" stroke="${border}"/><text x="24" y="32" fill="${text}" font-size="24">▣</text><text x="24" y="70">This PC</text></g><g transform="translate(36 124)"><rect width="48" height="48" rx="10" fill="url(#tile)" stroke="${border}"/><text x="24" y="32" fill="${text}" font-size="24">⌑</text><text x="24" y="70">Documents</text></g><g transform="translate(36 218)"><rect width="48" height="48" rx="10" fill="url(#tile)" stroke="${border}"/><text x="24" y="32" fill="${text}" font-size="24">♲</text><text x="24" y="70">Recycle Bin</text></g></g><g filter="url(#shadow)"><rect x="270" y="90" width="900" height="560" rx="12" fill="${surface}"/><path d="M282 90h876a12 12 0 0 1 12 12v38H270v-38a12 12 0 0 1 12-12" fill="${surfaceRaised}"/><rect x="288" y="104" width="22" height="22" rx="5" fill="${accent}"/><g font-family="Segoe UI,Arial" fill="${text}"><text x="324" y="121" font-size="14" font-weight="600">Workspace — Notes</text><text x="1010" y="121">―</text><text x="1060" y="121">□</text><text x="1110" y="121" font-size="18">×</text><text x="342" y="235" font-size="38" font-weight="700">Today’s workspace</text><text x="342" y="270" fill="${muted}" font-size="16">A clean, fictional desktop prepared for the Voltura Air preview.</text></g><g font-family="Segoe UI,Arial" fill="${text}"><g transform="translate(342 310)"><rect width="350" height="128" rx="12" fill="${surfaceRaised}" stroke="${border}"/><text x="22" y="36" font-weight="700">Review project plan</text><text x="22" y="70" fill="${muted}" font-size="14">Check milestones and collect open questions.</text></g><g transform="translate(710 310)"><rect width="350" height="128" rx="12" fill="${surfaceRaised}" stroke="${border}"/><text x="22" y="36" font-weight="700">Organize documents</text><text x="22" y="70" fill="${muted}" font-size="14">Keep working files grouped and easy to find.</text></g><g transform="translate(342 456)"><rect width="350" height="128" rx="12" fill="${surfaceRaised}" stroke="${border}"/><text x="22" y="36" font-weight="700">Prepare presentation</text><text x="22" y="70" fill="${muted}" font-size="14">Refine the outline and confirm the sequence.</text></g><g transform="translate(710 456)"><rect width="350" height="128" rx="12" fill="${surfaceRaised}" stroke="${border}"/><text x="22" y="36" font-weight="700">Finish the day</text><text x="22" y="70" fill="${muted}" font-size="14">Capture progress and note the next action.</text></g></g></g><rect y="754" width="1440" height="56" fill="${surface}" opacity=".96"/><g transform="translate(625 766)"><g fill="${accent}"><rect width="8" height="8"/><rect x="10" width="8" height="8"/><rect y="10" width="8" height="8"/><rect x="10" y="10" width="8" height="8"/></g><rect x="35" y="-2" width="190" height="34" rx="17" fill="${surfaceRaised}"/><text x="58" y="20" font-family="Segoe UI,Arial" fill="${muted}" font-size="13">⌕  Search</text><rect x="242" width="22" height="22" rx="5" fill="${muted}"/><rect x="280" width="22" height="22" rx="5" fill="${accent}"/><rect x="318" width="22" height="22" rx="5" fill="${accentStrong}"/></g><text x="1408" y="777" text-anchor="end" font-family="Segoe UI,Arial" fill="${muted}" font-size="12">10:24</text><text x="1408" y="793" text-anchor="end" font-family="Segoe UI,Arial" fill="${muted}" font-size="12">27/08/2026</text></svg>`;
}

function screenViewShowcaseSvg() {
  const { accent, accentStrong, bg, border, muted, surface, surfaceRaised, text } = hostDarkPalette;
  return String.raw`<svg xmlns="http://www.w3.org/2000/svg" width="1600" height="1612"><defs><linearGradient id="bg" x2="1" y2="1"><stop stop-color="${surfaceRaised}"/><stop offset=".38" stop-color="${bg}"/><stop offset="1" stop-color="${accentStrong}" stop-opacity=".34"/></linearGradient><filter id="shadow"><feDropShadow dx="0" dy="24" stdDeviation="28" flood-color="${bg}" flood-opacity=".78"/></filter></defs><rect width="1600" height="1612" fill="url(#bg)"/><g font-family="Inter,ui-sans-serif,system-ui,-apple-system,BlinkMacSystemFont,&quot;Segoe UI&quot;,sans-serif" fill="${accent}" font-size="48" font-weight="800" letter-spacing="5.76"><text x="70" y="76">WINDOWS 11 PC</text><text x="70" y="914">VIEW PC SCREEN ON PHONE</text></g><rect x="70" y="940" width="1460" height="672" rx="142" fill="${bg}" stroke="${border}" stroke-width="4" filter="url(#shadow)"/><rect x="101" y="965" width="1398" height="622" rx="112" fill="${surface}" stroke="${surfaceRaised}" stroke-width="4"/><rect x="62" y="1112" width="8" height="112" rx="4" fill="${surfaceRaised}"/><rect x="1530" y="1112" width="8" height="112" rx="4" fill="${surfaceRaised}"/><rect x="118" y="1221" width="30" height="110" rx="20" fill="${surface}" stroke="${border}" stroke-width="2"/><circle cx="133" cy="1243" r="5" fill="${accentStrong}"/><rect x="1486" y="1217" width="5" height="118" rx="3" fill="${text}" opacity=".72"/><circle cx="1454" cy="1538" r="5" fill="${accent}"/></svg>`;
}

async function renderHostScreenshot(theme, outputPath, customScreens = false) {
  const host = await launchHost(theme, outputPath, customScreens);
  try {
    // launchHost returns only after the off-screen PNG has been written.
  } finally {
    await stopProcess(host.process);
  }
}

async function ensureCaptureDependencies() {
  const packageJson = path.join(tempDir, "package.json");
  if (!existsSync(packageJson)) {
    await fs.writeFile(packageJson, JSON.stringify({ private: true, type: "commonjs" }, null, 2));
  }

  const modules = ["playwright"];
  if (modules.every((name) => existsSync(path.join(tempDir, "node_modules", name)))) {
    return;
  }

  await run("npm", ["install", "--no-audit", "--no-fund", "--no-save", ...modules], {
    cwd: tempDir,
  });
}

async function captureMobileScreens(chromium, pairingUrl, filePreviewUrl) {
  const browser = await launchBrowser(chromium);
  try {
    const context = await browser.newContext({
      viewport: { width: 393, height: 852 },
      deviceScaleFactor: 3,
      isMobile: true,
      hasTouch: true,
    });
    await context.addInitScript(() => {
      localStorage.setItem("voltura-air.screenshotMode", "true");
      if (!localStorage.getItem("voltura-air.themeMode")) {
        localStorage.setItem("voltura-air.themeMode", "light");
      }
    });

    const page = await context.newPage();
    await page.goto(pairingUrl, { waitUntil: "networkidle" });
    await clickPairIfPresent(page);
    await waitForConnected(page);
    await waitForTrackpadVolume(page);

    await page.screenshot({ path: outputs.iphoneLight });

    await setMobileTheme(page, "dark");
    await page.screenshot({ path: outputs.iphoneDark });

    await captureKodiRemote(page);

    await page.setViewportSize({ width: 1180, height: 820 });
    await page.evaluate(() => {
      localStorage.setItem("voltura-air.themeMode", "light");
      const clientId = localStorage.getItem("voltura-air.clientId");
      const pcId = localStorage.getItem("voltura-air.activePcId");
      if (clientId && pcId) {
        const trackpadKey = `voltura-air.trackpadSettings.${clientId}.${pcId}`;
        const keyboardKey = `voltura-air.keyboardSettings.${clientId}`;
        const trackpadSettings = JSON.parse(localStorage.getItem(trackpadKey) ?? "{}");
        const keyboardSettings = JSON.parse(localStorage.getItem(keyboardKey) ?? "{}");
        localStorage.setItem(
          trackpadKey,
          JSON.stringify({ ...trackpadSettings, enableSplitMode: true }),
        );
        localStorage.setItem(
          keyboardKey,
          JSON.stringify({ ...keyboardSettings, enableSplitMode: true }),
        );
      }
    });
    await page.reload({ waitUntil: "networkidle" });
    await page.locator(".split-mode-shell").waitFor({ timeout: 5000 });
    await page.screenshot({ path: outputs.split });

    await captureFilesPreview(browser, filePreviewUrl);
  } finally {
    await browser.close();
  }
}

async function captureFilesPreview(browser, previewUrl) {
  for (const [theme, outputPath] of [
    ["light", outputs.filesLight],
    ["dark", outputs.filesDark],
  ]) {
    const context = await browser.newContext({
      viewport: { width: 1180, height: 820 },
      deviceScaleFactor: 1,
      isMobile: false,
      hasTouch: true,
    });
    await context.addInitScript((nextTheme) => {
      localStorage.setItem("voltura-air.themeMode", nextTheme);
    }, theme);
    try {
      const page = await context.newPage();
      await page.goto(`${previewUrl}/?filesPreview=1`, { waitUntil: "networkidle" });
      await page.locator(".file-manager-workspace").waitFor({ timeout: 5000 });
      await page.locator(".file-panel").nth(1).waitFor({ state: "visible", timeout: 5000 });
      await page
        .getByText("Files ready.", { exact: true })
        .waitFor({ state: "visible", timeout: 5000 });
      await page.screenshot({ path: outputPath });
    } finally {
      await context.close();
    }
  }
}

async function captureKodiRemote(page) {
  await page.setViewportSize({ width: 393, height: 852 });
  await page.evaluate(() => {
    localStorage.setItem("voltura-air.themeMode", "dark");
    const clientId = localStorage.getItem("voltura-air.clientId");
    const pcId = localStorage.getItem("voltura-air.activePcId");
    if (clientId) {
      localStorage.setItem(
        `voltura-air.remoteSettings.${clientId}`,
        JSON.stringify({ navigationRing: true, mode: "kodi", startKodi: false }),
      );
    }
    if (clientId && pcId) {
      localStorage.setItem(
        `voltura-air.remoteSettings.${clientId}.${pcId}`,
        JSON.stringify({ navigationRing: true, mode: "kodi", startKodi: false }),
      );
    }
  });
  await page.reload({ waitUntil: "networkidle" });
  await waitForConnected(page);
  const remoteTab = page.getByRole("button", { name: "Remote", exact: true });
  await remoteTab.click();
  await page.locator(".remote-navigation-ring").waitFor({ timeout: 5000 });
  await remoteTab.click();
  await page.locator(".app-shell.mode-tabs-collapsed.remote-active").waitFor({ timeout: 5000 });
  const modeSwitchHint = page.getByText("Switch modes from here.", { exact: true });
  await modeSwitchHint.waitFor({ state: "visible", timeout: 5000 });
  await modeSwitchHint.waitFor({
    state: "hidden",
    timeout: 7000,
  });
  await page.screenshot({ path: outputs.iphoneKodiDark });
}

async function launchBrowser(chromium) {
  try {
    return await chromium.launch({ channel: "chrome" });
  } catch {
    console.log("Chrome channel was not available; installing Playwright Chromium.");
    await run("npx", ["playwright", "install", "chromium"], { cwd: tempDir });
    return chromium.launch();
  }
}

async function clickPairIfPresent(page) {
  const pair = page.getByRole("button", { name: "Pair" });
  const becameVisible = await pair
    .waitFor({ state: "visible", timeout: 10000 })
    .then(() => true)
    .catch(() => false);
  if (becameVisible) await pair.click();
}

async function waitForConnected(page) {
  await page.locator(".status.paired").waitFor({ state: "visible", timeout: 10000 });
}

async function waitForTrackpadVolume(page) {
  await page.locator(".trackpad-mode .volume-control").waitFor({ state: "visible", timeout: 5000 });
}

async function setMobileTheme(page, theme) {
  await page.evaluate(
    (nextTheme) => localStorage.setItem("voltura-air.themeMode", nextTheme),
    theme,
  );
  await page.reload({ waitUntil: "networkidle" });
  await waitForConnected(page);
  await waitForTrackpadVolume(page);
}

async function launchHost(theme, outputPath, customScreens = false) {
  await fs.rm(pairingUrlFile, { force: true });
  await fs.rm(outputPath, { force: true });
  await fs.rm(path.join(tempAppDataDir, "Voltura Air"), { recursive: true, force: true });
  const hostArgs = [
    "--site-screenshot-mode",
    "--site-screenshot-theme",
    theme,
    "--site-screenshot-output",
    outputPath,
    "--isolated-test-mode",
    "--pairing-store-root",
    tempAppDataDir,
    "--pairing-url-file",
    pairingUrlFile,
  ];
  if (customScreens) {
    hostArgs.push("--site-screenshot-custom-screens");
  }
  const child = spawn(hostExe, hostArgs, {
    cwd: path.dirname(hostExe),
    env: {
      ...process.env,
      APPDATA: tempAppDataDir,
    },
    stdio: ["ignore", "inherit", "inherit"],
    windowsHide: true,
  });

  const [pairingUrl] = await Promise.all([
    waitForTextFile(pairingUrlFile, 15000, child),
    waitForNonEmptyFile(outputPath, 15000, child),
  ]);
  return { process: child, pairingUrl };
}

async function launchFilePreview() {
  const port = 5183;
  const url = `http://127.0.0.1:${port}`;
  const [executable, args] =
    process.platform === "win32"
      ? [
          "cmd.exe",
          [
            "/d",
            "/s",
            "/c",
            "npm",
            "run",
            "dev",
            "--workspace",
            "apps/mobile-web",
            "--",
            "--host",
            "127.0.0.1",
            "--port",
            String(port),
            "--strictPort",
          ],
        ]
      : [
          "npm",
          [
            "run",
            "dev",
            "--workspace",
            "apps/mobile-web",
            "--",
            "--host",
            "127.0.0.1",
            "--port",
            String(port),
            "--strictPort",
          ],
        ];
  const child = spawn(executable, args, {
    cwd: repoRoot,
    stdio: ["ignore", "inherit", "inherit"],
    windowsHide: true,
  });
  await waitForHttp(url, 15000, child);
  return { process: child, url };
}

async function stopRunningHost() {
  stopExistingHost();
}

async function stopProcess(child) {
  if (child.exitCode !== null) {
    return;
  }

  child.kill();
  if (await waitForProcessExit(child, 2500)) {
    return;
  }

  child.kill("SIGKILL");
  if (!(await waitForProcessExit(child, 2500))) {
    throw new Error("Timed out waiting for the screenshot host to exit.");
  }
}

async function stopPreviewProcess(child) {
  if (child.exitCode !== null) {
    return;
  }
  stopChild(child, "SIGTERM");
  if (!(await waitForProcessExit(child, 2500))) {
    throw new Error("Timed out waiting for the Files screenshot preview to exit.");
  }
}

async function waitForProcessExit(child, timeoutMs) {
  if (child.exitCode !== null) {
    return true;
  }

  return Promise.race([
    new Promise((resolve) => child.once("exit", () => resolve(true))),
    delay(timeoutMs).then(() => false),
  ]);
}

async function waitForTextFile(filePath, timeoutMs, child) {
  const started = Date.now();
  while (Date.now() - started < timeoutMs) {
    throwIfHostExited(child, filePath);
    if (existsSync(filePath)) {
      const value = (await fs.readFile(filePath, "utf8")).trim();
      if (value) {
        return value;
      }
    }
    await delay(100);
  }

  throw new Error(`Timed out waiting for ${filePath}`);
}

async function waitForNonEmptyFile(filePath, timeoutMs, child) {
  const started = Date.now();
  while (Date.now() - started < timeoutMs) {
    throwIfHostExited(child, filePath);
    try {
      if ((await fs.stat(filePath)).size > 0) {
        return;
      }
    } catch (error) {
      if (error.code !== "ENOENT") {
        throw error;
      }
    }
    await delay(100);
  }

  throw new Error(`Timed out waiting for ${filePath}`);
}

function throwIfHostExited(child, awaitedPath) {
  if (child.exitCode !== null) {
    throw new Error(
      `Voltura Air host exited with code ${child.exitCode} before writing ${awaitedPath}`,
    );
  }
}

async function waitForHttp(url, timeoutMs, child) {
  const started = Date.now();
  while (Date.now() - started < timeoutMs) {
    throwIfHostExited(child, url);
    try {
      const response = await fetch(url);
      if (response.ok) {
        return;
      }
    } catch {
      // The Vite listener is still starting.
    }
    await delay(100);
  }
  throw new Error(`Timed out waiting for ${url}`);
}

function delay(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

async function run(command, args, options = {}) {
  const [executable, spawnArgs] =
    process.platform === "win32" && (command === "npm" || command === "npx")
      ? ["cmd.exe", ["/d", "/s", "/c", command, ...args]]
      : [resolveCommand(command), args];
  console.log(`> ${command} ${args.join(" ")}`);
  await new Promise((resolve, reject) => {
    const child = spawn(executable, spawnArgs, {
      cwd: options.cwd ?? repoRoot,
      stdio: "inherit",
      windowsHide: true,
    });
    child.once("exit", (code) => {
      if (code === 0) {
        resolve();
      } else {
        reject(new Error(`${command} exited with code ${code}`));
      }
    });
    child.once("error", reject);
  });
}

function resolveCommand(command) {
  if (process.platform !== "win32") {
    return command;
  }

  return command === "npm" || command === "npx" ? `${command}.cmd` : command;
}
