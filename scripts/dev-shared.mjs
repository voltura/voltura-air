import { spawnSync } from "node:child_process";
import { networkInterfaces } from "node:os";

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

export function stopExistingHost() {
  if (process.platform !== "win32") {
    return;
  }

  spawnSync("taskkill", ["/IM", "VolturaAir.Host.exe", "/F", "/T"], { stdio: "ignore" });
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
