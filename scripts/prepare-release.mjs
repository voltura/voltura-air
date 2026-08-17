import path from "node:path";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";
import { withReleaseLock } from "./release-lock.mjs";

export const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");

export function prepareReleaseUnlocked(version) {
  if (!version) throw new Error("A release version is required.");
  const result = spawnSync("pwsh", ["-NoProfile", "-File", path.join(repositoryRoot, "scripts", "prepare-release.ps1"), version], {
    cwd: repositoryRoot,
    stdio: "inherit",
    windowsHide: true
  });
  if (result.error) throw result.error;
  if (result.status !== 0) throw new Error(`Release preparation failed with exit code ${result.status ?? "unknown"}.`);
}

export function prepareRelease(version) {
  return withReleaseLock(() => prepareReleaseUnlocked(version), { repositoryRoot });
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  prepareRelease(process.argv[2]).catch((error) => {
    console.error(error.message);
    process.exitCode = 1;
  });
}
