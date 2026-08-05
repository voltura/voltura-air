import { createRequire } from "node:module";
import { fileURLToPath } from "node:url";
import { existsSync } from "node:fs";
import fs from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import sharp from "sharp";
import { spawn } from "node:child_process";
import { stopExistingHost } from "./dev-shared.mjs";

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const assetsDir = path.join(repoRoot, "docs", "site", "assets");
const tempDir = path.join(os.tmpdir(), "voltura-air-site-screenshots");
const tempAppDataDir = path.join(tempDir, "appdata");
const pairingUrlFile = path.join(tempDir, "pairing-url.txt");
const hostExe = path.join(repoRoot, "apps", "windows-host", "bin", "cli", "Debug", "net10.0-windows", "VolturaAir.Host.exe");

const outputs = {
  hostLight: path.join(assetsDir, "voltura-air-host.png"),
  hostDark: path.join(assetsDir, "voltura-air-host-dark.png"),
  hostCustomScreensLight: path.join(assetsDir, "voltura-air-host-custom-screens.png"),
  hostCustomScreensDark: path.join(assetsDir, "voltura-air-host-custom-screens-dark.png"),
  iphoneLight: path.join(assetsDir, "voltura-air-iphone.png"),
  iphoneDark: path.join(assetsDir, "voltura-air-iphone-dark.png"),
  iphoneKodiDark: path.join(assetsDir, "voltura-air-iphone-kodi-dark.png"),
  iphoneKodiDarkForum: path.join(assetsDir, "voltura-air-iphone-kodi-dark-forum.png"),
  split: path.join(assetsDir, "voltura-air-split.png")
};

main().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});

async function main() {
  if (process.platform !== "win32") {
    throw new Error("Site screenshot capture must run on Windows because it renders the WPF host.");
  }

  await fs.mkdir(tempDir, { recursive: true });
  await fs.mkdir(tempAppDataDir, { recursive: true });
  await fs.mkdir(assetsDir, { recursive: true });
  await ensureCaptureDependencies();

  try {
    const requireFromTemp = createRequire(path.join(tempDir, "package.json"));
    const { chromium } = requireFromTemp("playwright");

    await stopRunningHost();
    await run("npm", ["run", "build", "--workspace", "apps/mobile-web"]);
    await run("dotnet", ["build", "VolturaAir.slnx"]);

    await renderHostScreenshot("Dark", outputs.hostDark);
    await renderHostScreenshot("Dark", outputs.hostCustomScreensDark, true);

    const lightHost = await launchHost("Light", outputs.hostLight);
    try {
      await captureMobileScreens(chromium, lightHost.pairingUrl);
      await sharp(outputs.iphoneKodiDark)
        .resize({ width: 350 })
        .png()
        .toFile(outputs.iphoneKodiDarkForum);
    } finally {
      await stopProcess(lightHost.process);
    }

    await renderHostScreenshot("Light", outputs.hostCustomScreensLight, true);

    console.log(`Site screenshots written to ${assetsDir}`);
  } finally {
    await stopRunningHost();
  }
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

  await run("npm", ["install", "--no-audit", "--no-fund", "--no-save", ...modules], { cwd: tempDir });
}

async function captureMobileScreens(chromium, pairingUrl) {
  const browser = await launchBrowser(chromium);
  try {
    const context = await browser.newContext({
      viewport: { width: 393, height: 852 },
      deviceScaleFactor: 3,
      isMobile: true,
      hasTouch: true
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
        localStorage.setItem(trackpadKey, JSON.stringify({ ...trackpadSettings, enableSplitMode: true }));
        localStorage.setItem(keyboardKey, JSON.stringify({ ...keyboardSettings, enableSplitMode: true }));
      }
    });
    await page.reload({ waitUntil: "networkidle" });
    await page.locator(".split-mode-shell").waitFor({ timeout: 5000 });
    await page.screenshot({ path: outputs.split });
  } finally {
    await browser.close();
  }
}

async function captureKodiRemote(page) {
  await page.setViewportSize({ width: 393, height: 852 });
  await page.evaluate(() => {
    localStorage.setItem("voltura-air.themeMode", "dark");
    const clientId = localStorage.getItem("voltura-air.clientId");
    const pcId = localStorage.getItem("voltura-air.activePcId");
    if (clientId) {
      localStorage.setItem(`voltura-air.remoteSettings.${clientId}`, JSON.stringify({ navigationRing: true, mode: "kodi" }));
    }
    if (clientId && pcId) {
      localStorage.setItem(`voltura-air.remoteSettings.${clientId}.${pcId}`, JSON.stringify({ navigationRing: true, mode: "kodi" }));
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
    timeout: 7000
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
  await page.evaluate((nextTheme) => localStorage.setItem("voltura-air.themeMode", nextTheme), theme);
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
    pairingUrlFile
  ];
  if (customScreens) {
    hostArgs.push("--site-screenshot-custom-screens");
  }
  const child = spawn(hostExe, hostArgs, {
    cwd: path.dirname(hostExe),
    env: {
      ...process.env,
      APPDATA: tempAppDataDir
    },
    stdio: ["ignore", "inherit", "inherit"],
    windowsHide: true
  });

  const [pairingUrl] = await Promise.all([
    waitForTextFile(pairingUrlFile, 15000, child),
    waitForNonEmptyFile(outputPath, 15000, child)
  ]);
  return { process: child, pairingUrl };
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
  if (!await waitForProcessExit(child, 2500)) {
    throw new Error("Timed out waiting for the screenshot host to exit.");
  }
}

async function waitForProcessExit(child, timeoutMs) {
  if (child.exitCode !== null) {
    return true;
  }

  return Promise.race([
    new Promise((resolve) => child.once("exit", () => resolve(true))),
    delay(timeoutMs).then(() => false)
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
    throw new Error(`Voltura Air host exited with code ${child.exitCode} before writing ${awaitedPath}`);
  }
}

function delay(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

async function run(command, args, options = {}) {
  const [executable, spawnArgs] = process.platform === "win32" && (command === "npm" || command === "npx")
    ? ["cmd.exe", ["/d", "/s", "/c", command, ...args]]
    : [resolveCommand(command), args];
  console.log(`> ${command} ${args.join(" ")}`);
  await new Promise((resolve, reject) => {
    const child = spawn(executable, spawnArgs, {
      cwd: options.cwd ?? repoRoot,
      stdio: "inherit",
      windowsHide: true
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
