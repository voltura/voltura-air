import { spawn } from "node:child_process";
import { access } from "node:fs/promises";
import path from "node:path";
import { getLanAddress, stopChild, stopExistingHost } from "./dev-shared.mjs";

const clientEntryPath = path.resolve("apps", "mobile-web", "dist", "index.html");
const clientFileRetryCount = 3;
const clientFileRetryDelayMs = 2000;

const clientPort = process.env.VOLTURA_AIR_CLIENT_PORT ?? "5173";
const restartExitCode = 23;
let shuttingDown = false;
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
} else {
  args.push("--print-host-client-url");
}

stopExistingHost();
if (useViteClient) {
  console.log(`Voltura Air phone client: ${clientUrl}`);
} else {
  await waitForClientFiles();
}

let child = startHost();

for (const signal of ["SIGINT", "SIGTERM"]) {
  process.once(signal, () => shutdown(signal));
}

function startHost() {
  const next = spawn("dotnet", args, {
    stdio: ["inherit", "pipe", "inherit"],
    env: { ...process.env, VOLTURA_AIR_DEV_HOST: "1" }
  });
  next.stdout.pipe(process.stdout);
  next.on("exit", (code, signal) => {
    if (shuttingDown) {
      return;
    }

    if (code === restartExitCode) {
      child = startHost();
      return;
    }

    if (signal) {
      process.kill(process.pid, signal);
    }

    process.exit(code ?? 0);
  });
  return next;
}

function shutdown(signal) {
  if (shuttingDown) {
    return;
  }

  shuttingDown = true;
  let exitCode = 0;
  try {
    stopExistingHost();
  } catch (error) {
    console.error(error);
    exitCode = 1;
  }

  stopChild(child, signal);
  setTimeout(() => process.exit(exitCode), 250);
}

async function waitForClientFiles() {
  for (let attempt = 0; attempt <= clientFileRetryCount; attempt += 1) {
    try {
      await access(clientEntryPath);
      return;
    } catch (error) {
      if (attempt === clientFileRetryCount) {
        throw new Error(
          `Mobile client files were not found at ${clientEntryPath} after ${clientFileRetryCount} retries. Run npm run build --workspace apps/mobile-web and try again.`,
          { cause: error }
        );
      }

      console.warn(`Mobile client files are not ready; retrying in ${clientFileRetryDelayMs / 1000} seconds (${attempt + 1}/${clientFileRetryCount})...`);
      await new Promise((resolve) => setTimeout(resolve, clientFileRetryDelayMs));
    }
  }
}
