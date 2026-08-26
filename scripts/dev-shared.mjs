import { spawnSync } from "node:child_process";
import { networkInterfaces } from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

const windowsHostImage = "VolturaAir.Host.exe";
const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const synchronousWaitBuffer = new Int32Array(new SharedArrayBuffer(4));

export function getLanAddress() {
  for (const items of Object.values(networkInterfaces())) {
    for (const item of items ?? []) {
      if (item.family === "IPv4" && !item.internal) {
        return item.address;
      }
    }
  }

  return "127.0.0.1";
}

export function readPreferredClientPort() {
  const value = Number.parseInt(process.env.VOLTURA_AIR_CLIENT_PORT ?? "5173", 10);
  return Number.isInteger(value) && value > 0 && value < 65536 ? value : 5173;
}

export function resolveCommand(command) {
  if (process.platform !== "win32") {
    return command;
  }

  return command === "npm" || command === "npx" ? `${command}.cmd` : command;
}

export function stopChild(child, signal) {
  if (child.killed || child.exitCode !== null) {
    return;
  }

  if (process.platform === "win32" && child.pid) {
    spawnSync("taskkill", ["/PID", String(child.pid), "/T", "/F"], { stdio: "ignore" });
    return;
  }

  child.kill(signal);
}

export function stopExistingHost(options = {}) {
  const platform = options.platform ?? process.platform;
  const run = options.run ?? spawnSync;
  const listHostProcesses = options.listHostProcesses ?? (() => findWindowsHostProcesses(run));
  const waitForProcessExit = options.waitForProcessExit ?? waitForWindowsProcessExit;
  if (platform !== "win32") {
    return;
  }

  const processes = listHostProcesses();
  const unverified = processes.filter(
    (process) => !isAllowedHostExecutable(process.executablePath),
  );
  if (unverified.length > 0) {
    throw new Error(
      `Refusing to stop an unverified ${windowsHostImage} process: ${unverified.map((process) => process.executablePath || `PID ${process.pid}`).join(", ")}`,
    );
  }
  for (const process of processes) {
    const result = run("taskkill", ["/PID", String(process.pid), "/T", "/F"], { stdio: "ignore" });
    if (
      result.error ||
      (result.status !== undefined && result.status !== null && result.status !== 0)
    ) {
      throw new Error(`Could not stop the verified Voltura Air host process ${process.pid}.`);
    }
  }
  if (!waitForProcessExit(windowsHostImage, { run })) {
    throw new Error("Timed out waiting for the existing Voltura Air host to exit.");
  }
}

export function waitForWindowsProcessExit(imageName, options = {}) {
  const run = options.run ?? spawnSync;
  const now = options.now ?? Date.now;
  const sleep = options.sleep ?? sleepSynchronously;
  const timeoutMs = options.timeoutMs ?? 5000;
  const pollIntervalMs = options.pollIntervalMs ?? 50;
  const deadline = now() + timeoutMs;

  while (isWindowsProcessRunning(imageName, run)) {
    if (now() >= deadline) {
      return false;
    }

    sleep(pollIntervalMs);
  }

  return true;
}

export function stopWindowsNodeListenersOnDevPorts(startPort, count) {
  if (process.platform !== "win32") {
    return;
  }

  const ports = new Set(Array.from({ length: count }, (_, index) => startPort + index));
  const result = spawnSync("netstat", ["-ano", "-p", "tcp"], {
    encoding: "utf8",
    stdio: ["ignore", "pipe", "ignore"],
  });
  if (
    result.error ||
    (result.status !== undefined && result.status !== null && result.status !== 0) ||
    typeof result.stdout !== "string"
  ) {
    throw new Error("Could not inspect the reserved Voltura Air development ports.");
  }

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

  if (listenerPids.size > 0) {
    throw new Error(
      `Reserved Voltura Air development ports are already in use by PID(s): ${[...listenerPids].join(", ")}. Stop the owning process and retry.`,
    );
  }
}

function isWindowsProcessRunning(imageName, run) {
  const result = run("tasklist", ["/FI", `IMAGENAME eq ${imageName}`, "/FO", "CSV", "/NH"], {
    encoding: "utf8",
    stdio: ["ignore", "pipe", "ignore"],
  });
  if (
    result.error ||
    (result.status !== undefined && result.status !== null && result.status !== 0) ||
    typeof result.stdout !== "string"
  ) {
    throw new Error(`Could not inspect whether ${imageName} is still running.`);
  }
  return (
    result.stdout
      .trim()
      .match(/^"([^"]+)"/)?.[1]
      ?.toLowerCase() === imageName.toLowerCase()
  );
}

function findWindowsHostProcesses(run) {
  const script =
    "$ErrorActionPreference='Stop';@(Get-CimInstance Win32_Process -Filter \"Name='VolturaAir.Host.exe'\"|ForEach-Object{[pscustomobject]@{pid=[int]$_.ProcessId;executablePath=[string]$_.ExecutablePath}})|ConvertTo-Json -Compress";
  const result = run("powershell.exe", ["-NoProfile", "-Command", script], {
    encoding: "utf8",
    stdio: ["ignore", "pipe", "pipe"],
  });
  if (
    result.error ||
    (result.status !== undefined && result.status !== null && result.status !== 0)
  ) {
    throw new Error("Could not verify the running Voltura Air host executable path.");
  }
  const text = result.stdout?.trim();
  if (!text) return [];
  let parsed;
  try {
    parsed = JSON.parse(text);
  } catch {
    throw new Error("Could not parse the running Voltura Air host process inventory.");
  }
  return (Array.isArray(parsed) ? parsed : [parsed]).map((process) => ({
    pid: Number(process.pid),
    executablePath:
      typeof process.executablePath === "string" ? path.resolve(process.executablePath) : "",
  }));
}

function isAllowedHostExecutable(executablePath) {
  if (
    !executablePath ||
    path.basename(executablePath).toLowerCase() !== windowsHostImage.toLowerCase()
  )
    return false;
  const normalized = path.resolve(executablePath).toLowerCase();
  const repositoryHostRoot =
    path.join(repositoryRoot, "apps", "windows-host", "bin").toLowerCase() + path.sep;
  const installedHostRoot = process.env.LOCALAPPDATA
    ? path.join(process.env.LOCALAPPDATA, "Programs", "Voltura Air").toLowerCase() + path.sep
    : "";
  return (
    normalized.startsWith(repositoryHostRoot) ||
    (installedHostRoot !== "" && normalized.startsWith(installedHostRoot))
  );
}

function sleepSynchronously(milliseconds) {
  Atomics.wait(synchronousWaitBuffer, 0, 0, milliseconds);
}
