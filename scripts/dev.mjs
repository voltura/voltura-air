import { spawn, spawnSync } from "node:child_process";
import { readPreferredClientPort, stopChild, stopWindowsNodeListenersOnDevPorts } from "./dev-shared.mjs";

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
