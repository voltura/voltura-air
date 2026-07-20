import { spawnSync } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const scheduleScript = path.join(scriptDirectory, "install-chatgpt-codex-schedule.ps1");
const argumentsFromUser = process.argv.slice(2);
let time = "04:00:00";

for (let index = 0; index < argumentsFromUser.length; index += 1) {
  if (argumentsFromUser[index] !== "--time" || index + 1 >= argumentsFromUser.length) {
    throw new Error("Usage: npm run ai:schedule -- [--time HH:mm:ss]");
  }

  time = argumentsFromUser[index + 1];
  index += 1;
}

if (!/^([01]\d|2[0-3]):[0-5]\d:[0-5]\d$/u.test(time)) {
  throw new Error("The schedule time must use 24-hour HH:mm:ss format.");
}

const result = spawnSync(
  "powershell.exe",
  ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", scheduleScript, "-Time", time],
  { stdio: "inherit" }
);

if (result.error) {
  throw result.error;
}

process.exitCode = result.status ?? 1;
