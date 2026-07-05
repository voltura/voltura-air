import { createRequire } from "node:module";
import { fileURLToPath } from "node:url";
import { existsSync } from "node:fs";
import fs from "node:fs/promises";
import { networkInterfaces, tmpdir } from "node:os";
import path from "node:path";
import { spawn, spawnSync } from "node:child_process";
import { devUiDevices, getDevUiDevice } from "./dev-ui-devices.mjs";

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const tempDir = path.join(tmpdir(), "voltura-air-dev-ui");
const tempNodeDir = path.join(tempDir, "node");
const tempAppDataDir = path.join(tempDir, "appdata");
const browserProfileDir = path.join(tempDir, "chrome-profile");
const pairingUrlFile = path.join(tempDir, "pairing-url.txt");
const clientPort = readPreferredClientPort();
const clientUrl = process.env.VOLTURA_AIR_CLIENT_URL ?? `http://${getLanAddress()}:${clientPort}`;
const debugDevice = getDevUiDevice();
const childEnv = {
  ...process.env,
  VOLTURA_AIR_CLIENT_PORT: String(clientPort),
  VOLTURA_AIR_CLIENT_URL: clientUrl,
  VOLTURA_AIR_USE_VITE_CLIENT: "1"
};
const children = [];
let browserContext = null;
let shuttingDown = false;

main().catch((error) => {
  console.error(error);
  shutdown("SIGTERM", 1);
});

for (const signal of ["SIGINT", "SIGTERM"]) {
  process.once(signal, () => shutdown(signal));
}

async function main() {
  if (process.platform !== "win32") {
    throw new Error("Voltura Air UI debug sessions must run on Windows because they launch the WPF host.");
  }

  await fs.mkdir(tempNodeDir, { recursive: true });
  await fs.mkdir(tempAppDataDir, { recursive: true });
  await fs.rm(path.join(tempAppDataDir, "Voltura Air"), { recursive: true, force: true });
  await fs.rm(browserProfileDir, { recursive: true, force: true });
  await fs.rm(pairingUrlFile, { force: true });
  await seedBrowserProfile(debugDevice);

  await ensureDebugDependencies();
  stopWindowsNodeListenersOnDevPorts(clientPort, 20);
  stopExistingHost();

  console.log("Starting Voltura Air UI debug session...");
  console.log(`Vite client: ${clientUrl}`);
  console.log(`Chrome device: ${debugDevice.title}`);
  console.log(`Debug storage: ${tempDir}`);

  children.push(spawnCommand(
    "node",
    ["../../node_modules/vite/bin/vite.js", "--host", "0.0.0.0", "--strictPort", "--port", String(clientPort)],
    childEnv,
    { cwd: path.join(repoRoot, "apps", "mobile-web") }));

  await waitForHttp(clientUrl, 30000);

  children.push(spawnCommand("dotnet", [
    "run",
    "--project",
    "apps/windows-host/VolturaAir.Host.csproj",
    "--",
    "--client-url",
    clientUrl,
    "--pairing-store-root",
    tempAppDataDir,
    "--pairing-url-file",
    pairingUrlFile
  ], {
    ...childEnv,
    APPDATA: tempAppDataDir
  }, { cwd: repoRoot }));

  const pairingUrl = await waitForTextFile(pairingUrlFile, 30000);
  const requireFromTemp = createRequire(path.join(tempNodeDir, "package.json"));
  const { chromium } = requireFromTemp("playwright");
  const page = await launchConnectedBrowser(chromium, pairingUrl);

  console.log("Chrome is connected to the local Voltura Air host.");
  console.log("Use Ctrl+Shift+M in DevTools to toggle the device toolbar.");
  console.log("Close Chrome or press Ctrl+C here to stop the debug session.");

  page.context().once("close", () => shutdown("SIGTERM", 0));
}

async function ensureDebugDependencies() {
  const packageJson = path.join(tempNodeDir, "package.json");
  if (!existsSync(packageJson)) {
    await fs.writeFile(packageJson, JSON.stringify({ private: true, type: "commonjs" }, null, 2));
  }

  if (existsSync(path.join(tempNodeDir, "node_modules", "playwright"))) {
    return;
  }

  await run("npm", ["install", "--no-audit", "--no-fund", "--no-save", "playwright"], { cwd: tempNodeDir });
}

async function launchConnectedBrowser(chromium, pairingUrl) {
  const url = new URL(pairingUrl);
  url.searchParams.set("debug", "1");

  browserContext = await launchPersistentContext(chromium);
  const page = browserContext.pages()[0] ?? await browserContext.newPage();
  await applyDeviceEmulation(page, debugDevice);
  await page.goto(url.href, { waitUntil: "networkidle" });
  await clickPairIfPresent(page);
  await waitForConnected(page);
  return page;
}

async function launchPersistentContext(chromium) {
  const options = {
    channel: "chrome",
    headless: false,
    devtools: true,
    viewport: null,
    args: ["--start-maximized", "--auto-open-devtools-for-tabs", "--test-type"]
  };

  try {
    return await chromium.launchPersistentContext(browserProfileDir, options);
  } catch {
    console.log("Chrome channel was not available; installing Playwright Chromium.");
    await run("npx", ["playwright", "install", "chromium"], { cwd: tempNodeDir });
    const { channel, ...fallbackOptions } = options;
    return chromium.launchPersistentContext(browserProfileDir, fallbackOptions);
  }
}

async function seedBrowserProfile(device) {
  const preferences = {
    browser: {
      window_placement: {
        maximized: true
      }
    },
    devtools: {
      preferences: {
        currentDockState: JSON.stringify("right"),
        "custom-emulated-device-list": JSON.stringify(devUiDevices),
        customEmulatedDeviceList: JSON.stringify(devUiDevices),
        "emulation.device-mode-value": JSON.stringify({
          device: device.title,
          orientation: "vertical",
          mode: ""
        }),
        "emulation.device-scale": "0.86",
        "emulation.show-device-mode": "true"
      }
    }
  };

  await writeJson(path.join(browserProfileDir, "Preferences"), preferences);
  await writeJson(path.join(browserProfileDir, "Default", "Preferences"), preferences);
}

async function applyDeviceEmulation(page, device) {
  const vertical = device.screen.vertical;
  const client = await page.context().newCDPSession(page);
  await client.send("Emulation.setDeviceMetricsOverride", {
    width: vertical.width,
    height: vertical.height,
    deviceScaleFactor: device.screen["device-pixel-ratio"],
    mobile: device.capabilities.includes("mobile"),
    screenWidth: vertical.width,
    screenHeight: vertical.height
  });
  await client.send("Emulation.setTouchEmulationEnabled", {
    enabled: device.capabilities.includes("touch"),
    maxTouchPoints: device.capabilities.includes("touch") ? 1 : 0
  });
}

async function clickPairIfPresent(page) {
  const pair = page.getByRole("button", { name: "Pair" });
  if (await pair.isVisible({ timeout: 10000 }).catch(() => false)) {
    await pair.evaluate((button) => button.click());
  }
}

async function waitForConnected(page) {
  await page.getByText(/^Connected to /).waitFor({ timeout: 15000 });
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

async function waitForHttp(url, timeoutMs) {
  const started = Date.now();
  while (Date.now() - started < timeoutMs) {
    try {
      const response = await fetch(url);
      if (response.ok) {
        return;
      }
    } catch {
    }
    await delay(250);
  }

  throw new Error(`Timed out waiting for ${url}`);
}

function spawnCommand(command, args, env, options = {}) {
  const commandLine = [command, ...args].join(" ");
  const child = process.platform === "win32"
    ? spawn("cmd.exe", ["/d", "/s", "/c", commandLine], { stdio: "inherit", env, windowsHide: false, ...options })
    : spawn(command, args, { stdio: "inherit", env, ...options });

  child.commandLine = commandLine;
  child.on("error", (error) => {
    console.error(`Failed to start ${child.commandLine}:`, error);
    shutdown("SIGTERM", 1);
  });

  child.on("exit", (code, signal) => {
    if (shuttingDown) {
      return;
    }

    if (signal) {
      shutdown("SIGTERM", 1);
      return;
    }

    shutdown("SIGTERM", code ?? 0);
  });

  return child;
}

async function run(command, args, options = {}) {
  const executable = resolveCommand(command);
  console.log(`> ${command} ${args.join(" ")}`);
  await new Promise((resolve, reject) => {
    const child = spawn(executable, args, {
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

function shutdown(signal, exitCode = 0) {
  if (shuttingDown) {
    return;
  }

  shuttingDown = true;
  Promise.resolve()
    .then(async () => {
      if (browserContext) {
        await browserContext.close().catch(() => {});
      }

      for (const child of children) {
        stopChild(child, signal);
      }
    })
    .finally(() => setTimeout(() => process.exit(exitCode), 500));
}

function stopChild(child, signal) {
  if (child.killed || child.exitCode !== null) {
    return;
  }

  if (process.platform === "win32" && child.pid) {
    spawnSync("taskkill", ["/PID", String(child.pid), "/T", "/F"], { stdio: "ignore" });
    return;
  }

  child.kill(signal);
}

function stopExistingHost() {
  if (process.platform !== "win32") {
    return;
  }

  spawnSync("taskkill", ["/IM", "VolturaAir.Host.exe", "/F", "/T"], { stdio: "ignore" });
}

function stopWindowsNodeListenersOnDevPorts(startPort, count) {
  if (process.platform !== "win32") {
    return;
  }

  const ports = new Set(Array.from({ length: count }, (_, index) => startPort + index));
  const result = spawnSync("netstat", ["-ano", "-p", "tcp"], {
    encoding: "utf8",
    stdio: ["ignore", "pipe", "ignore"]
  });

  const listenerPids = new Set();
  for (const line of result.stdout.split(/\r?\n/)) {
    const parts = line.trim().split(/\s+/);
    if (parts.length < 5 || parts[0] !== "TCP" || parts[3] !== "LISTENING") {
      continue;
    }

    const port = Number.parseInt(parts[1].slice(parts[1].lastIndexOf(":") + 1), 10);
    if (ports.has(port)) {
      listenerPids.add(parts[4]);
    }
  }

  for (const pid of listenerPids) {
    if (isNodeProcess(pid)) {
      spawnSync("taskkill", ["/PID", pid, "/T", "/F"], { stdio: "ignore" });
    }
  }
}

function isNodeProcess(pid) {
  const result = spawnSync("tasklist", ["/FI", `PID eq ${pid}`, "/FO", "CSV", "/NH"], {
    encoding: "utf8",
    stdio: ["ignore", "pipe", "ignore"]
  });

  const imageName = result.stdout.trim().match(/^"([^"]+)"/)?.[1]?.toLowerCase();
  return imageName === "node.exe";
}

async function writeJson(filePath, value) {
  await fs.mkdir(path.dirname(filePath), { recursive: true });
  await fs.writeFile(filePath, JSON.stringify(value));
}

function getLanAddress() {
  for (const items of Object.values(networkInterfaces())) {
    for (const item of items ?? []) {
      if (item.family === "IPv4" && !item.internal) {
        return item.address;
      }
    }
  }

  return "127.0.0.1";
}

function readPreferredClientPort() {
  const value = Number.parseInt(process.env.VOLTURA_AIR_CLIENT_PORT ?? "5173", 10);
  return Number.isInteger(value) && value > 0 && value < 65536 ? value : 5173;
}

function resolveCommand(command) {
  if (process.platform !== "win32") {
    return command;
  }

  return command === "npm" || command === "npx" ? `${command}.cmd` : command;
}

function delay(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}
