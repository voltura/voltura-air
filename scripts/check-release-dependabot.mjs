import { execFileSync } from "node:child_process";
import { resolve } from "node:path";
import { fileURLToPath } from "node:url";

const root = fileURLToPath(new URL("../", import.meta.url));

function github(args) {
  return execFileSync("gh", args, {
    cwd: root,
    encoding: "utf8",
    stdio: ["ignore", "pipe", "pipe"],
    timeout: 30_000,
    windowsHide: true,
  }).trim();
}

export function checkReleaseDependabot(repository, query = github) {
  if (!/^[a-z0-9_.-]+\/[a-z0-9_.-]+$/iu.test(repository)) {
    throw new Error("Expected a GitHub repository in owner/name form.");
  }

  let pages;
  try {
    pages = JSON.parse(
      query([
        "api",
        "--paginate",
        "--slurp",
        `repos/${repository}/pulls?state=open&base=main&per_page=100`,
      ]),
    );
  } catch {
    throw new Error(
      `Could not check open Dependabot pull requests in ${repository}. Verify GitHub CLI access and retry.`,
    );
  }
  if (!Array.isArray(pages) || pages.length === 0 || !pages.every(Array.isArray)) {
    throw new Error(`GitHub returned an unexpected pull request response for ${repository}.`);
  }

  const pending = pages.flat().filter((pull) => pull.user?.login === "dependabot[bot]");
  if (pending.length > 0) {
    const links = pending.map((pull) => `https://github.com/${repository}/pull/${pull.number}`);
    throw new Error(
      `Resolve the open Dependabot pull requests targeting ${repository} main before releasing:\n${links.join("\n")}`,
    );
  }
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  try {
    const repository =
      process.argv[2] ??
      github(["repo", "view", "--json", "nameWithOwner", "--jq", ".nameWithOwner"]);
    checkReleaseDependabot(repository);
    console.log(`No open Dependabot pull requests targeting ${repository} main.`);
  } catch (error) {
    console.error(error.message);
    process.exitCode = 1;
  }
}
