import { networkInterfaces } from "node:os";
import { spawn, spawnSync } from "node:child_process";

const clientPort = process.env.VOLTURA_AIR_CLIENT_PORT ?? "5173";
const args = [
  "run",
  "--project",
  "apps/windows-host/VolturaAir.Host.csproj",
  "--"
];
const useViteClient =
  process.env.VOLTURA_AIR_USE_VITE_CLIENT === "1" ||
  process.env.VOLTURA_AIR_USE_VITE_CLIENT?.toLowerCase() === "true" ||
  Boolean(process.env.VOLTURA_AIR_CLIENT_URL);
const clientUrl = process.env.VOLTURA_AIR_CLIENT_URL ?? `http://${getLanAddress()}:${clientPort}`;

if (useViteClient) {
  args.push("--client-url", clientUrl);
}

stopExistingHost();
console.log(useViteClient
  ? `Voltura Air phone client: ${clientUrl}`
  : "Voltura Air phone client: Windows host URL");

const child = spawn("dotnet", args, {
  stdio: "inherit"
});

child.on("exit", (code, signal) => {
  if (signal) {
    process.kill(process.pid, signal);
  }

  process.exit(code ?? 0);
});

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

function stopExistingHost() {
  if (process.platform !== "win32") {
    return;
  }

  spawnSync("taskkill", ["/IM", "VolturaAir.Host.exe", "/F", "/T"], { stdio: "ignore" });
}
