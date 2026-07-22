import { createHash } from "node:crypto";
import { readFile, readdir, stat, writeFile } from "node:fs/promises";
import path from "node:path";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";

import { getNextReleaseVersion } from "./bump-release.mjs";
import {
  getGeneralReleaseNotices,
  getReleaseNotesSection,
  parseReleaseArguments,
  parseSemver,
  releaseNotesEndMarker,
  releaseNotesStartMarker,
  resolveLatestPublishedRelease,
  resolveReleaseVersion
} from "./release-tools.mjs";

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const runtime = "win-x64";

function runCommand(command, args = [], { captureOutput = false, allowFailure = false } = {}) {
  let executable = command;
  let executableArgs = args;
  if (command === "npm") {
    if (!process.env.npm_execpath) {
      throw new Error("Local releases must be run through npm: npm run release:local");
    }
    executable = process.execPath;
    executableArgs = [process.env.npm_execpath, ...args];
  }

  const result = spawnSync(executable, executableArgs, {
    cwd: repositoryRoot,
    encoding: "utf8",
    stdio: captureOutput ? ["ignore", "pipe", "pipe"] : "inherit",
    windowsHide: true
  });
  if (result.error) {
    throw result.error;
  }
  if (result.signal) {
    throw new Error(`${command} ${args.join(" ")} was terminated by signal ${result.signal}.`);
  }
  if (result.status !== 0 && !allowFailure) {
    const details = captureOutput ? (result.stderr || result.stdout || "").trim() : "";
    throw new Error(`${command} ${args.join(" ")} failed with exit code ${result.status}.${details ? ` ${details}` : ""}`);
  }
  return { status: result.status ?? 1, stdout: (result.stdout ?? "").trim(), stderr: (result.stderr ?? "").trim() };
}

function checked(command, args = [], options = {}) {
  return runCommand(command, args, options).stdout;
}

async function assertNoActiveWorkflowFiles() {
  const workflowsDirectory = path.join(repositoryRoot, ".github", "workflows");
  let names;
  try {
    names = await readdir(workflowsDirectory);
  } catch (error) {
    if (error.code === "ENOENT") {
      return;
    }
    throw error;
  }
  const workflows = names.filter((name) => /\.ya?ml$/u.test(name));
  if (workflows.length > 0) {
    throw new Error(`Local release requires GitHub Actions workflows to remain archived; found: ${workflows.join(", ")}`);
  }
}

async function assertGitStatePathAbsent(name) {
  const statePath = checked("git", ["rev-parse", "--git-path", name], { captureOutput: true });
  try {
    await stat(path.resolve(repositoryRoot, statePath));
    throw new Error(`A merge or rebase is in progress: ${statePath}`);
  } catch (error) {
    if (error.code !== "ENOENT") {
      throw error;
    }
  }
}

export function getRelease(tag, repository, lookup = checked) {
  return JSON.parse(lookup("gh", [
    "release", "view", tag, "--repo", repository,
    "--json", "tagName,isDraft,isPrerelease,targetCommitish,url,assets"
  ], { captureOutput: true }));
}

function remoteTagExists(tag) {
  return checked("git", ["ls-remote", "--tags", "origin", `refs/tags/${tag}`], { captureOutput: true }).length > 0;
}

async function sha256(filePath) {
  const bytes = await readFile(filePath);
  return createHash("sha256").update(bytes).digest("hex");
}

async function assertReleaseAssets(paths) {
  for (const filePath of paths) {
    const file = await stat(filePath).catch(() => null);
    if (!file?.isFile() || file.size <= 0) {
      throw new Error(`Release asset is missing or empty: ${filePath}`);
    }
  }
}

export function buildReleaseBody({ notes, notices, version, latestTag, repository }) {
  return `## What's new

${releaseNotesStartMarker}
${notes}

${notices}
${releaseNotesEndMarker}

## Downloads

- **VolturaAir-Setup-${version}-${runtime}.exe**: compact installer; downloads required .NET 10 components if they are missing.
- **VolturaAir-Setup-${version}-${runtime}-full.exe**: offline installer with all required runtimes bundled.
- **VolturaAir-${version}-${runtime}.zip**: portable package.

**Full changelog:** https://github.com/${repository}/compare/${latestTag}...v${version}
`;
}

export function auditDraft(release, expectedCommit, expectedNames) {
  if (!release?.isDraft || release.targetCommitish !== expectedCommit) {
    throw new Error("Draft release audit failed: draft state or target commit does not match.");
  }
  const actualNames = release.assets.map((asset) => asset.name).sort();
  if (actualNames.join("|") !== [...expectedNames].sort().join("|")) {
    throw new Error(`Draft release assets do not match the expected set: ${actualNames.join(", ")}`);
  }
  for (const asset of release.assets) {
    if (asset.size <= 0 || !asset.digest) {
      throw new Error(`Release asset '${asset.name}' has invalid size or digest metadata.`);
    }
  }
}

export async function runLocalRelease(args = process.argv.slice(2)) {
  const { version: explicitVersion, publishLatest } = parseReleaseArguments(args);
  if (process.platform !== "win32") {
    throw new Error("Local Voltura Air releases are supported only on Windows.");
  }

  await assertNoActiveWorkflowFiles();
  for (const command of ["git", "node", "dotnet", "gh"]) {
    checked(command, ["--version"], { captureOutput: true });
  }
  const dotnetSdks = checked("dotnet", ["--list-sdks"], { captureOutput: true });
  if (!/^10\.0\./mu.test(dotnetSdks)) {
    throw new Error(".NET 10 SDK was not found.");
  }
  checked("gh", ["auth", "status", "--hostname", "github.com"], { captureOutput: true });

  const initialStatus = checked("git", ["status", "--porcelain=v1", "--untracked-files=all"], { captureOutput: true });
  if (initialStatus) {
    throw new Error("Local release requires a clean Git working tree.");
  }
  const branch = checked("git", ["branch", "--show-current"], { captureOutput: true });
  if (branch !== "main") {
    throw new Error("Local releases must run from the main branch.");
  }
  checked("git", ["var", "GIT_AUTHOR_IDENT"], { captureOutput: true });
  await assertGitStatePathAbsent("MERGE_HEAD");
  await assertGitStatePathAbsent("rebase-merge");
  await assertGitStatePathAbsent("rebase-apply");
  checked("git", ["remote", "get-url", "origin"], { captureOutput: true });

  const repository = checked("gh", ["repo", "view", "--json", "nameWithOwner", "--jq", ".nameWithOwner"], { captureOutput: true });
  checked("git", ["fetch", "origin", "main", "--tags"]);
  checked("git", ["merge-base", "--is-ancestor", "origin/main", "HEAD"]);

  const releasePages = JSON.parse(checked("gh", [
    "api", "--paginate", "--slurp", `repos/${repository}/releases?per_page=100`
  ], { captureOutput: true }));
  const latest = resolveLatestPublishedRelease(releasePages.flat());

  const packagePath = path.join(repositoryRoot, "package.json");
  const currentVersion = String(JSON.parse(await readFile(packagePath, "utf8")).version ?? "");
  const currentTag = `v${currentVersion}`;
  const currentTagExists = remoteTagExists(currentTag);
  const currentRelease = currentTagExists ? getRelease(currentTag, repository) : null;
  const targetVersion = resolveReleaseVersion({
    currentVersion,
    latestReleasedVersion: latest.version,
    explicitVersion,
    currentTagExists,
    currentReleaseIsDraft: currentRelease?.isDraft === true,
    getNextVersion: getNextReleaseVersion
  });
  const targetTag = `v${targetVersion}`;
  const targetSemver = parseSemver(targetVersion);
  if (publishLatest && targetSemver.prerelease.length > 0) {
    throw new Error("A prerelease cannot be published as Latest; omit 'latest' to create its draft.");
  }

  const notesPath = path.join(repositoryRoot, "docs", "release-notes.md");
  const releaseNotes = await readFile(notesPath, "utf8");
  const notes = getReleaseNotesSection(releaseNotes, targetVersion);
  const notices = getGeneralReleaseNotices(releaseNotes);
  const targetTagExists = targetTag === currentTag ? currentTagExists : remoteTagExists(targetTag);
  const targetReleaseBeforeBuild = targetTagExists ? getRelease(targetTag, repository) : null;
  if (targetReleaseBeforeBuild && !targetReleaseBeforeBuild.isDraft) {
    throw new Error(`Release '${targetTag}' is already public. Prepare a new version instead.`);
  }
  const startingCommit = checked("git", ["rev-parse", "HEAD"], { captureOutput: true });
  if (targetReleaseBeforeBuild?.isDraft && targetReleaseBeforeBuild.targetCommitish !== startingCommit) {
    throw new Error(`Draft '${targetTag}' targets another commit and cannot be resumed from this checkout.`);
  }

  checked("npm", ["run", "size:check"]);
  checked("npm", ["run", "release", "--", targetVersion]);
  checked("npm", ["run", "branding:generate"]);
  checked("npm", ["test"]);
  checked("npm", ["run", "package:win", "--", "-Version", targetVersion, "-Runtime", runtime]);

  checked("git", ["add", "--all"]);
  const staged = runCommand("git", ["diff", "--cached", "--quiet"], { allowFailure: true });
  if (staged.status === 1) {
    checked("git", ["commit", "-m", `Release Voltura Air ${targetVersion}`]);
  } else if (staged.status !== 0) {
    throw new Error("Could not inspect staged release changes.");
  }
  checked("git", ["push", "origin", "main"]);

  const releaseCommit = checked("git", ["rev-parse", "HEAD"], { captureOutput: true });
  checked("npm", ["run", "package:win", "--", "-Version", targetVersion, "-Runtime", runtime]);
  const finalStatus = checked("git", ["status", "--porcelain=v1", "--untracked-files=all"], { captureOutput: true });
  if (finalStatus) {
    throw new Error(`Repository is not clean after the final release build: ${finalStatus}`);
  }

  const publishRoot = path.join(repositoryRoot, "artifacts", "publish");
  const assetPaths = [
    path.join(publishRoot, `VolturaAir-${targetVersion}-${runtime}.zip`),
    path.join(publishRoot, `VolturaAir-Setup-${targetVersion}-${runtime}.exe`),
    path.join(publishRoot, `VolturaAir-Setup-${targetVersion}-${runtime}-full.exe`)
  ];
  await assertReleaseAssets(assetPaths);
  const assetNames = assetPaths.map((filePath) => path.basename(filePath));
  const bodyPath = path.join(publishRoot, `release-notes-${targetTag}.md`);
  await writeFile(bodyPath, buildReleaseBody({
    notes,
    notices,
    version: targetVersion,
    latestTag: latest.tag,
    repository
  }), "utf8");

  const existingDraft = targetReleaseBeforeBuild;
  if (existingDraft === null) {
    const createArgs = [
      "release", "create", targetTag, "--repo", repository,
      "--target", releaseCommit, "--title", `Voltura Air ${targetTag}`,
      "--draft", "--fail-on-no-commits", "--notes-file", bodyPath
    ];
    if (targetSemver.prerelease.length > 0) {
      createArgs.push("--prerelease");
    }
    createArgs.push(...assetPaths);
    checked("gh", createArgs);
  } else {
    if (!existingDraft.isDraft || existingDraft.targetCommitish !== releaseCommit) {
      throw new Error(`Existing release '${targetTag}' is not a matching resumable draft.`);
    }
    checked("gh", ["release", "edit", targetTag, "--repo", repository, "--title", `Voltura Air ${targetTag}`, "--notes-file", bodyPath]);
    checked("gh", ["release", "upload", targetTag, "--repo", repository, "--clobber", ...assetPaths]);
  }

  const auditedDraft = getRelease(targetTag, repository);
  auditDraft(auditedDraft, releaseCommit, assetNames);
  checked("npm", ["run", "publish:site"]);

  if (publishLatest) {
    checked("gh", ["release", "edit", targetTag, "--repo", repository, "--draft=false", "--latest"]);
    const latestPublished = JSON.parse(checked("gh", ["api", `repos/${repository}/releases/latest`], { captureOutput: true }));
    if (latestPublished.tag_name !== targetTag) {
      throw new Error(`GitHub did not mark '${targetTag}' as the latest release.`);
    }
  }
  checked("git", ["fetch", "origin", `refs/tags/${targetTag}:refs/tags/${targetTag}`]);

  console.log(`${publishLatest ? "Published" : "Created draft"} https://github.com/${repository}/releases/tag/${targetTag}`);
  for (const assetPath of assetPaths) {
    console.log(`${path.basename(assetPath)} SHA-256 ${await sha256(assetPath)}`);
  }
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  runLocalRelease().catch((error) => {
    console.error(error.message);
    process.exitCode = 1;
  });
}
