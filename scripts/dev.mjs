import { spawn, spawnSync } from "node:child_process";

const clientPort = readPreferredClientPort();
const childEnv = {
  ...process.env,
  VOLTURA_AIR_CLIENT_PORT: String(clientPort)
};
const children = [];
let shuttingDown = false;

runCommand("npm", ["run", "build", "--workspace", "apps/mobile-web"], childEnv);
stopWindowsNodeListenersOnDevPorts(clientPort, 20);
children.push(spawnCommand(
  "node",
  ["../../node_modules/vite/bin/vite.js", "--host", "0.0.0.0", "--strictPort", "--port", String(clientPort)],
  childEnv,
  { cwd: "apps/mobile-web" }));
children.push(spawnCommand("npm", ["run", "dev:host"], childEnv));

for (const signal of ["SIGINT", "SIGTERM"]) {
  process.on(signal, () => shutdown(signal));
}

for (const child of children) {
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
}

function runCommand(command, args, env) {
  const commandLine = [command, ...args].join(" ");
  const result = process.platform === "win32"
    ? spawnSync("cmd.exe", ["/d", "/s", "/c", commandLine], { stdio: "inherit", env, windowsHide: false })
    : spawnSync(command, args, { stdio: "inherit", env });

  if (result.error) {
    console.error(`Failed to run ${commandLine}:`, result.error);
    process.exit(1);
  }

  if (result.signal) {
    process.kill(process.pid, result.signal);
  }

  if (result.status && result.status !== 0) {
    process.exit(result.status);
  }
}

function spawnCommand(command, args, env, options = {}) {
  const commandLine = [command, ...args].join(" ");
  const child = process.platform === "win32"
    ? spawn("cmd.exe", ["/d", "/s", "/c", commandLine], { stdio: "inherit", env, windowsHide: false, ...options })
    : spawn(command, args, { stdio: "inherit", env, ...options });

  child.commandLine = commandLine;
  return child;
}

function shutdown(signal, exitCode = 0) {
  if (shuttingDown) {
    return;
  }

  shuttingDown = true;
  for (const child of children) {
    stopChild(child, signal);
  }

  setTimeout(() => process.exit(exitCode), 500);
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

function readPreferredClientPort() {
  const value = Number.parseInt(process.env.VOLTURA_AIR_CLIENT_PORT ?? "5173", 10);
  return Number.isInteger(value) && value > 0 && value < 65536 ? value : 5173;
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
