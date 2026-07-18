import { createRequire } from "node:module";
import { fileURLToPath } from "node:url";
import { existsSync } from "node:fs";
import fs from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import { spawn } from "node:child_process";
import { stopExistingHost } from "./dev-shared.mjs";

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const assetsDir = path.join(repoRoot, "docs", "site", "assets");
const tempDir = path.join(os.tmpdir(), "voltura-air-site-screenshots");
const tempAppDataDir = path.join(tempDir, "appdata");
const pairingUrlFile = path.join(tempDir, "pairing-url.txt");
const hostCaptureScript = path.join(tempDir, "capture-window.ps1");
const hostExe = path.join(repoRoot, "apps", "windows-host", "bin", "Debug", "net10.0-windows", "VolturaAir.Host.exe");

const outputs = {
  hostLight: path.join(assetsDir, "voltura-air-host.png"),
  hostDark: path.join(assetsDir, "voltura-air-host-dark.png"),
  iphoneLight: path.join(assetsDir, "voltura-air-iphone.png"),
  iphoneDark: path.join(assetsDir, "voltura-air-iphone-dark.png"),
  iphoneKodiDark: path.join(assetsDir, "voltura-air-iphone-kodi-dark.png"),
  split: path.join(assetsDir, "voltura-air-split.png")
};

main().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});

async function main() {
  if (process.platform !== "win32") {
    throw new Error("Site screenshot capture must run on Windows because it captures the WPF host window.");
  }

  await fs.mkdir(tempDir, { recursive: true });
  await fs.mkdir(tempAppDataDir, { recursive: true });
  await fs.mkdir(assetsDir, { recursive: true });
  await ensureCaptureDependencies();
  await writeHostCaptureScript();

  try {
    const requireFromTemp = createRequire(path.join(tempDir, "package.json"));
    const { chromium } = requireFromTemp("playwright");

    await stopRunningHost();
    await run("npm", ["run", "build", "--workspace", "apps/mobile-web"]);
    await run("dotnet", ["build", "VolturaAir.slnx"]);

    const lightHost = await launchHost("Light");
    try {
      await captureHostWindow(outputs.hostLight);
      await captureMobileScreens(chromium, lightHost.pairingUrl);
    } finally {
      await stopProcess(lightHost.process);
    }

    const darkHost = await launchHost("Dark");
    try {
      await captureHostWindow(outputs.hostDark);
    } finally {
      await stopProcess(darkHost.process);
    }

    console.log(`Site screenshots written to ${assetsDir}`);
  } finally {
    await stopRunningHost();
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
    const url = new URL(pairingUrl);
    url.searchParams.set("screenshot", "1");
    await page.goto(url.href, { waitUntil: "networkidle" });
    await clickPairIfPresent(page);
    await waitForConnected(page);

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
  const remoteTab = page.getByRole("button", { name: "Remote mode" });
  await remoteTab.click();
  await page.locator(".remote-navigation-ring").waitFor({ timeout: 5000 });
  await remoteTab.click();
  await page.locator(".app-shell.mode-tabs-collapsed.remote-active").waitFor({ timeout: 5000 });
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
  if (await pair.isVisible({ timeout: 5000 }).catch(() => false)) {
    await pair.click();
  }
}

async function waitForConnected(page) {
  await page.locator(".status.paired").waitFor({ state: "visible", timeout: 10000 });
}

async function setMobileTheme(page, theme) {
  await page.evaluate((nextTheme) => localStorage.setItem("voltura-air.themeMode", nextTheme), theme);
  await page.reload({ waitUntil: "networkidle" });
  await waitForConnected(page);
}

async function captureHostWindow(outputPath) {
  const rawPath = path.join(tempDir, `host-${Date.now()}.png`);
  await run("powershell", [
    "-NoProfile",
    "-ExecutionPolicy",
    "Bypass",
    "-File",
    hostCaptureScript,
    "-OutputPath",
    rawPath
  ]);
  await fs.copyFile(rawPath, outputPath);
}

async function launchHost(theme) {
  await fs.rm(pairingUrlFile, { force: true });
  await fs.rm(path.join(tempAppDataDir, "Voltura Air"), { recursive: true, force: true });
  const hostArgs = [
    "--site-screenshot-mode",
    "--site-screenshot-theme",
    theme,
    "--isolated-test-mode",
    "--pairing-store-root",
    tempAppDataDir,
    "--pairing-url-file",
    pairingUrlFile
  ];
  const child = spawn(hostExe, hostArgs, {
    cwd: path.dirname(hostExe),
    env: {
      ...process.env,
      APPDATA: tempAppDataDir
    },
    stdio: "ignore",
    windowsHide: false
  });

  const pairingUrl = await waitForTextFile(pairingUrlFile, 15000);
  await delay(1800);
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

async function writeHostCaptureScript() {
  await fs.writeFile(hostCaptureScript, String.raw`
param(
  [Parameter(Mandatory = $true)]
  [string]$OutputPath
)

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class NativeWindowCapture {
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
  [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
}
"@

$process = Get-Process VolturaAir.Host -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $process) {
  throw "Voltura Air host window was not found."
}

$hwnd = $process.MainWindowHandle
[NativeWindowCapture]::ShowWindow($hwnd, 9) | Out-Null
[NativeWindowCapture]::SetForegroundWindow($hwnd) | Out-Null
Start-Sleep -Milliseconds 450

$rect = New-Object NativeWindowCapture+RECT
$dwmResult = [NativeWindowCapture]::DwmGetWindowAttribute($hwnd, 9, [ref]$rect, [Runtime.InteropServices.Marshal]::SizeOf([type][NativeWindowCapture+RECT]))
if ($dwmResult -ne 0) {
  [NativeWindowCapture]::GetWindowRect($hwnd, [ref]$rect) | Out-Null
}
$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top
# DWM extended-frame bounds omit the invisible resize border from the
# 1160 x 760 WPF window. At 100% scaling the visible frame is typically
# 1146 x 753, while the startup window remains far below this guard.
if ($width -lt 1120 -or $height -lt 720) {
  throw "Voltura Air host window capture bounds were too small: $($width)x$($height)."
}
$bitmap = New-Object System.Drawing.Bitmap($width, $height)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, [System.Drawing.Size]::new($width, $height))
$graphics.Dispose()
$bitmap.Save($OutputPath, [System.Drawing.Imaging.ImageFormat]::Png)
$bitmap.Dispose()
`);
}

async function waitForTextFile(filePath, timeoutMs) {
  const started = Date.now();
  while (Date.now() - started < timeoutMs) {
    if (existsSync(filePath)) {
      const value = (await fs.readFile(filePath, "utf8")).trim();
      if (value) {
        return value;
      }
    }
    await delay(200);
  }

  throw new Error(`Timed out waiting for ${filePath}`);
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
