import { spawnSync } from "node:child_process";

function runGit(arguments_, captureOutput = false) {
  const result = spawnSync("git", arguments_, {
    encoding: "utf8",
    stdio: captureOutput ? ["ignore", "pipe", "pipe"] : "inherit"
  });

  if (result.error) {
    throw new Error(`Could not run git: ${result.error.message}`);
  }

  if (result.status !== 0) {
    if (captureOutput && result.stderr) {
      process.stderr.write(result.stderr);
    }
    throw new Error(`git ${arguments_.join(" ")} failed.`);
  }

  return captureOutput ? result.stdout.trim() : undefined;
}

try {
  const branch = runGit(["branch", "--show-current"], true);
  if (branch.length === 0) {
    throw new Error("Check out a branch before syncing it with main.");
  }

  if (branch === "main") {
    throw new Error("Switch to a feature branch before syncing it with main.");
  }

  if (runGit(["status", "--porcelain=v1", "--untracked-files=all"], true).length > 0) {
    throw new Error("Commit, stash, or discard working-tree changes before syncing with main.");
  }

  runGit(["remote", "get-url", "origin"], true);
  console.log(`Syncing ${branch} with the latest origin/main...`);
  runGit(["fetch", "origin", "main"]);
  runGit(["merge", "--no-edit", "FETCH_HEAD"]);
} catch (error) {
  console.error(`branch:sync: ${error.message}`);
  process.exitCode = 1;
}
