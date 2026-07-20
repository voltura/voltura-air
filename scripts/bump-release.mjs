import { readFile } from "node:fs/promises";
import path from "node:path";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const odometerVersionPattern = /^(0|[1-9]\d*)\.(\d)\.(\d)$/u;

export function getNextReleaseVersion(version) {
  const match = odometerVersionPattern.exec(version);
  if (!match) {
    throw new Error(
      `Release bump requires a stable version with single-digit minor and patch components; received '${version}'. Use npm run release -- <version> to choose the next version explicitly.`
    );
  }

  let major = Number(match[1]);
  let minor = Number(match[2]);
  let patch = Number(match[3]);

  if (patch < 9) {
    patch += 1;
  } else {
    patch = 0;
    if (minor < 9) {
      minor += 1;
    } else {
      minor = 0;
      major += 1;
    }
  }

  return `${major}.${minor}.${patch}`;
}

export async function bumpRelease() {
  const packageJsonPath = path.join(repositoryRoot, "package.json");
  const packageJson = JSON.parse(await readFile(packageJsonPath, "utf8"));
  const currentVersion = String(packageJson.version ?? "");
  const nextVersion = getNextReleaseVersion(currentVersion);
  console.log(`Bumping Voltura Air from ${currentVersion} to ${nextVersion}.`);
  const result = spawnSync("powershell", [
    "-NoProfile",
    "-ExecutionPolicy",
    "Bypass",
    "-File",
    path.join(repositoryRoot, "scripts", "prepare-release.ps1"),
    nextVersion
  ], {
    cwd: repositoryRoot,
    stdio: "inherit"
  });

  if (result.error) {
    throw result.error;
  }
  if (result.status !== 0) {
    throw new Error(`Release preparation failed with exit code ${result.status ?? "unknown"}.`);
  }
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  bumpRelease().catch((error) => {
    console.error(error.message);
    process.exitCode = 1;
  });
}
