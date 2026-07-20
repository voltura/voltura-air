import { readFile } from "node:fs/promises";
import path from "node:path";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");

export function useAutomaticPublication(args) {
  if (args.length === 0) {
    return false;
  }
  if (args.length === 1 && args[0] === "auto") {
    return true;
  }

  throw new Error("Usage: npm run release:full [-- auto]");
}

function runCommand(command, args, { captureOutput = false } = {}) {
  const useWindowsNpmShell = process.platform === "win32" && command === "npm";
  const executable = useWindowsNpmShell ? "npm.cmd" : command;
  const result = spawnSync(executable, args, {
    cwd: repositoryRoot,
    encoding: "utf8",
    shell: useWindowsNpmShell,
    stdio: captureOutput ? ["ignore", "pipe", "inherit"] : "inherit"
  });

  if (result.error) {
    throw result.error;
  }
  if (result.status !== 0) {
    throw new Error(`${command} ${args.join(" ")} failed with exit code ${result.status ?? "unknown"}.`);
  }

  return captureOutput ? result.stdout.trim() : undefined;
}

function verifyCleanWorkingTree(run) {
  const status = run("git", ["status", "--porcelain=v1"], { captureOutput: true });
  if (status) {
    throw new Error("Full release requires a clean Git working tree so it cannot combine a version bump with unrelated changes.");
  }
}

function verifyAutomaticPublicationPreconditions(run) {
  run("git", ["rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{upstream}"], { captureOutput: true });
  run("git", ["var", "GIT_AUTHOR_IDENT"], { captureOutput: true });
}

async function getReleaseVersion() {
  const packageJsonPath = path.join(repositoryRoot, "package.json");
  const packageJson = JSON.parse(await readFile(packageJsonPath, "utf8"));
  return String(packageJson.version ?? "");
}

export async function runFullRelease(
  args = process.argv.slice(2),
  { run = runCommand, readVersion = getReleaseVersion } = {}
) {
  const automaticPublication = useAutomaticPublication(args);
  verifyCleanWorkingTree(run);
  if (automaticPublication) {
    verifyAutomaticPublicationPreconditions(run);
  }

  run("npm", ["run", "release:bump"]);
  run("npm", ["run", "branding:generate"]);
  run("npm", ["run", "publish:site"]);

  if (!automaticPublication) {
    return;
  }

  const version = await readVersion();
  run("git", ["add", "--all"]);
  run("git", ["commit", "-m", `Release version ${version}`]);
  run("git", ["push"]);
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  runFullRelease().catch((error) => {
    console.error(error.message);
    process.exitCode = 1;
  });
}
