import { createRequire } from "node:module";
import { fileURLToPath } from "node:url";
import { existsSync } from "node:fs";
import fs from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import { spawn, spawnSync } from "node:child_process";

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
  ipadLight: path.join(assetsDir, "voltura-air-ipad.png"),
  ipadDark: path.join(assetsDir, "voltura-air-ipad-dark.png"),
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
  const originalSettings = readHostCaptureSettings();

  try {
    const requireFromTemp = createRequire(path.join(tempDir, "package.json"));
    const { chromium } = requireFromTemp("playwright");

    await stopRunningHost();
    await run("npm", ["run", "build", "--workspace", "apps/mobile-web"]);
    await run("dotnet", ["build", "VolturaAir.slnx"]);

    await setHostTheme("Light");
    const lightHost = await launchHost();
    try {
      await captureHostWindow(outputs.hostLight);
      await captureMobileScreens(chromium, lightHost.pairingUrl);
    } finally {
      await stopProcess(lightHost.process);
    }

    await setHostTheme("Dark");
    const darkHost = await launchHost();
    try {
      await captureHostWindow(outputs.hostDark);
    } finally {
      await stopProcess(darkHost.process);
    }

    console.log(`Site screenshots written to ${assetsDir}`);
  } finally {
    await stopRunningHost();
    await restoreHostCaptureSettings(originalSettings);
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

    await page.setViewportSize({ width: 820, height: 1180 });
    await setMobileTheme(page, "light");
    await page.screenshot({ path: outputs.ipadLight });

    await setMobileTheme(page, "dark");
    await page.screenshot({ path: outputs.ipadDark });

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

async function launchHost() {
  await fs.rm(pairingUrlFile, { force: true });
  await fs.rm(path.join(tempAppDataDir, "Voltura Air"), { recursive: true, force: true });
  const child = spawn(hostExe, [
    "--site-screenshot-mode",
    "--pairing-store-root",
    tempAppDataDir,
    "--pairing-url-file",
    pairingUrlFile
  ], {
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
  await runPowerShell("Stop-Process -Name VolturaAir.Host -Force -ErrorAction SilentlyContinue; exit 0");
}

async function stopProcess(child) {
  if (child.exitCode !== null) {
    return;
  }

  child.kill();
  await Promise.race([
    new Promise((resolve) => child.once("exit", resolve)),
    delay(2500).then(() => {
      if (child.exitCode === null) {
        child.kill("SIGKILL");
      }
    })
  ]);
}

async function setHostTheme(theme) {
  await runPowerShell(`
    New-Item -Path HKCU:\\Software\\VolturaAir -Force | Out-Null
    New-ItemProperty -Path HKCU:\\Software\\VolturaAir -Name ThemeMode -Value ${JSON.stringify(theme)} -PropertyType String -Force | Out-Null
    New-ItemProperty -Path HKCU:\\Software\\VolturaAir -Name EnableGestureDebug -Value 0 -PropertyType DWord -Force | Out-Null
    New-ItemProperty -Path HKCU:\\Software\\VolturaAir -Name ShowConnectionStatusNotifications -Value 0 -PropertyType DWord -Force | Out-Null
    New-ItemProperty -Path HKCU:\\Software\\VolturaAir -Name ShowPairingWindowOnDisconnect -Value 0 -PropertyType DWord -Force | Out-Null
  `);
}

function readHostCaptureSettings() {
  return {
    themeMode: readRegistryValue("ThemeMode"),
    enableGestureDebug: readRegistryValue("EnableGestureDebug"),
    showConnectionStatusNotifications: readRegistryValue("ShowConnectionStatusNotifications"),
    showPairingWindowOnDisconnect: readRegistryValue("ShowPairingWindowOnDisconnect")
  };
}

async function restoreHostCaptureSettings(settings) {
  await restoreRegistryValue("ThemeMode", settings.themeMode, "String");
  await restoreRegistryValue("EnableGestureDebug", settings.enableGestureDebug, "DWord");
  await restoreRegistryValue("ShowConnectionStatusNotifications", settings.showConnectionStatusNotifications, "DWord");
  await restoreRegistryValue("ShowPairingWindowOnDisconnect", settings.showPairingWindowOnDisconnect, "DWord");
}

function readRegistryValue(name) {
  const result = spawnSync(
    "powershell",
    [
      "-NoProfile",
      "-Command",
      `$value = (Get-ItemProperty -Path HKCU:\\Software\\VolturaAir -Name ${JSON.stringify(name)} -ErrorAction SilentlyContinue).${name}; if ($null -ne $value) { [Console]::Out.Write($value) }`
    ],
    { encoding: "utf8", windowsHide: true }
  );

  return result.status === 0 && result.stdout.length > 0 ? result.stdout : null;
}

async function restoreRegistryValue(name, value, type) {
  if (value === null) {
    await runPowerShell(`Remove-ItemProperty -Path HKCU:\\Software\\VolturaAir -Name ${JSON.stringify(name)} -ErrorAction SilentlyContinue; exit 0`);
    return;
  }

  await runPowerShell(`
    New-Item -Path HKCU:\\Software\\VolturaAir -Force | Out-Null
    New-ItemProperty -Path HKCU:\\Software\\VolturaAir -Name ${JSON.stringify(name)} -Value ${JSON.stringify(value)} -PropertyType ${type} -Force | Out-Null
  `);
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
if ($width -lt 1160 -or $height -lt 760) {
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

async function runPowerShell(script) {
  await run("powershell", ["-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", script]);
}

async function run(command, args, options = {}) {
  const useShell = process.platform === "win32" && (command === "npm" || command === "npx");
  const executable = useShell ? command : resolveCommand(command);
  console.log(`> ${command} ${args.join(" ")}`);
  await new Promise((resolve, reject) => {
    const child = spawn(executable, args, {
      cwd: options.cwd ?? repoRoot,
      stdio: "inherit",
      shell: useShell,
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
