import { networkInterfaces } from "node:os";
import { spawn, spawnSync } from "node:child_process";

const clientPort = process.env.VOLTURA_AIR_CLIENT_PORT ?? "5173";
const clientUrl = process.env.VOLTURA_AIR_CLIENT_URL ?? `http://${getLanAddress()}:${clientPort}`;
const args = [
  "run",
  "--project",
  "apps/windows-host/VolturaAir.Host.csproj",
  "--",
  "--client-url",
  clientUrl
];

stopExistingHost();
console.log(`Voltura Air dev client: ${clientUrl}`);

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
