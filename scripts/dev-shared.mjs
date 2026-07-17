import { spawnSync } from "node:child_process";
import { networkInterfaces } from "node:os";

const windowsHostImage = "VolturaAir.Host.exe";
const cursorWatchdogImage = "VolturaAir.CursorWatchdog.exe";
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
  const waitForProcessExit = options.waitForProcessExit ?? waitForWindowsProcessExit;
  if (platform !== "win32") {
    return;
  }

  run("taskkill", ["/IM", windowsHostImage, "/F"], { stdio: "ignore" });
  if (!waitForProcessExit(cursorWatchdogImage, { run })) {
    throw new Error("Timed out waiting for the cursor watchdog to restore the Windows cursor scheme.");
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

function isWindowsProcessRunning(imageName, run) {
  const result = run("tasklist", ["/FI", `IMAGENAME eq ${imageName}`, "/FO", "CSV", "/NH"], {
    encoding: "utf8",
    stdio: ["ignore", "pipe", "ignore"]
  });
  return result.stdout?.trim().match(/^"([^"]+)"/)?.[1]?.toLowerCase() === imageName.toLowerCase();
}

function sleepSynchronously(milliseconds) {
  Atomics.wait(synchronousWaitBuffer, 0, 0, milliseconds);
}
