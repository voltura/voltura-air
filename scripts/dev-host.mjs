import { spawn } from "node:child_process";
import { getLanAddress, stopExistingHost } from "./dev-shared.mjs";

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
