import { spawn, spawnSync } from "node:child_process";
import { randomUUID } from "node:crypto";
import { readPreferredClientPort, stopChild, stopExistingHost, stopWindowsNodeListenersOnDevPorts } from "./dev-shared.mjs";

const clientPort = readPreferredClientPort();
const quickStart = process.argv.includes("--quick");
const childEnv = {
  ...process.env,
  VOLTURA_AIR_CLIENT_PORT: String(clientPort),
  VOLTURA_AIR_WEB_BUILD_ID: process.env.VOLTURA_AIR_WEB_BUILD_ID?.trim() || randomUUID()
};
if (quickStart) {
  childEnv.VOLTURA_AIR_USE_VITE_CLIENT = "0";
  delete childEnv.VOLTURA_AIR_CLIENT_URL;
}
const children = [];
let shuttingDown = false;

if (quickStart) {
  console.log("Quick phone development: building current mobile sources without validation.");
  runCommand("npm", ["run", "build:quick", "--workspace", "apps/mobile-web"], childEnv);
} else {
  runCommand("npm", ["run", "build", "--workspace", "apps/mobile-web"], childEnv);
}
stopWindowsNodeListenersOnDevPorts(clientPort, 20);
if (!quickStart) {
  children.push(spawnCommand(
    "node",
    ["../../node_modules/vite/bin/vite.js", "--host", "0.0.0.0", "--strictPort", "--port", String(clientPort)],
    childEnv,
    { cwd: "apps/mobile-web" }));
}
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
  try {
    stopExistingHost();
  } catch (error) {
    console.error(error);
    exitCode = 1;
  }

  for (const child of children) {
    stopChild(child, signal);
  }

  setTimeout(() => process.exit(exitCode), 500);
}
