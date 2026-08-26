import { createHash } from "node:crypto";
import { readFile, readdir, rm, stat, writeFile } from "node:fs/promises";
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
  resolveReleaseVersion,
} from "./release-tools.mjs";
import { createReleaseProgress } from "./release-progress.mjs";
import { withReleaseLock as withSharedReleaseLock } from "./release-lock.mjs";
import { prepareReleaseUnlocked } from "./prepare-release.mjs";
import {
  getReleaseAssetPaths,
  getReleaseCheckpointPath,
  readReleaseCheckpoint,
  writeReleaseCheckpoint,
} from "./release-checkpoint.mjs";

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const runtime = "win-x64";

function runCommand(command, args = [], { captureOutput = false, allowFailure = false } = {}) {
  let executable = command;
  let executableArgs = args;
  if (command === "npm") {
    if (!process.env.npm_execpath) {
      throw new Error(
        "Releases must be run through npm: npm run release:draft or npm run release:full",
      );
    }
    executable = process.execPath;
    executableArgs = [process.env.npm_execpath, ...args];
  }

  const result = spawnSync(executable, executableArgs, {
    cwd: repositoryRoot,
    encoding: "utf8",
    stdio: captureOutput ? ["ignore", "pipe", "pipe"] : "inherit",
    windowsHide: true,
  });
  if (result.error) {
    throw result.error;
  }
  if (result.signal) {
    throw new Error(`${command} ${args.join(" ")} was terminated by signal ${result.signal}.`);
  }
  if (result.status !== 0 && !allowFailure) {
    const details = captureOutput ? (result.stderr || result.stdout || "").trim() : "";
    throw new Error(
      `${command} ${args.join(" ")} failed with exit code ${result.status}.${details ? ` ${details}` : ""}`,
    );
  }
  return {
    status: result.status ?? 1,
    stdout: (result.stdout ?? "").trim(),
    stderr: (result.stderr ?? "").trim(),
  };
}

function checked(command, args = [], options = {}) {
  return runCommand(command, args, options).stdout;
}

async function stageReleaseChanges() {
  const result = runCommand("git", ["add", "--all"], { captureOutput: true, allowFailure: true });
  if (result.status !== 0) {
    const details = `${result.stderr}\n${result.stdout}`.trim();
    throw new Error(
      `git add --all failed with exit code ${result.status}.${details ? ` ${details}` : ""} Resolve any active Git operation or lock before retrying.`,
    );
  }
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
    throw new Error(
      `Release publication requires GitHub Actions workflows to remain archived; found: ${workflows.join(", ")}`,
    );
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

function getReleaseArguments(tag, repository) {
  return [
    "release",
    "view",
    tag,
    "--repo",
    repository,
    "--json",
    "tagName,name,body,isDraft,isPrerelease,targetCommitish,url,assets",
  ];
}

export function getRelease(tag, repository, lookup = checked) {
  return JSON.parse(lookup("gh", getReleaseArguments(tag, repository), { captureOutput: true }));
}

export function getReleaseIfExists(tag, repository, execute = runCommand) {
  const result = execute("gh", getReleaseArguments(tag, repository), {
    captureOutput: true,
    allowFailure: true,
  });
  if (result.status === 0) {
    return JSON.parse(result.stdout);
  }
  const details = `${result.stderr}\n${result.stdout}`.trim();
  if (/release not found|HTTP 404/iu.test(details)) {
    return null;
  }
  throw new Error(`Could not inspect release '${tag}'.${details ? ` ${details}` : ""}`);
}

export function publishReleaseIfRequested(
  { publishLatest, targetTag, repository, expectedCommit },
  execute = checked,
) {
  if (!publishLatest) {
    return;
  }

  execute("gh", ["release", "edit", targetTag, "--repo", repository, "--draft=false", "--latest"]);
  const latestPublished = JSON.parse(
    execute("gh", ["api", `repos/${repository}/releases/latest`], { captureOutput: true }),
  );
  if (
    latestPublished.tag_name !== targetTag ||
    latestPublished.draft !== false ||
    latestPublished.target_commitish !== expectedCommit
  ) {
    throw new Error(
      `GitHub did not publish '${targetTag}' from the expected commit as the latest release.`,
    );
  }
}

function remoteTagExists(tag) {
  return (
    checked("git", ["ls-remote", "--tags", "origin", `refs/tags/${tag}`], { captureOutput: true })
      .length > 0
  );
}

async function assertReleaseAssets(paths) {
  for (const filePath of paths) {
    const file = await stat(filePath).catch(() => null);
    if (!file?.isFile() || file.size <= 0) {
      throw new Error(`Release asset is missing or empty: ${filePath}`);
    }
  }
}

function preflightPublishRestores() {
  const project = "apps/windows-host/VolturaAir.Host.csproj";
  checked("dotnet", [
    "restore",
    project,
    "-r",
    runtime,
    "-p:SelfContained=true",
    "-p:PublishSingleFile=true",
    "-p:RestoreLockedMode=true",
    "-p:NuGetLockFilePath=packages.self-contained.lock.json",
  ]);
  checked("dotnet", [
    "restore",
    project,
    "-r",
    runtime,
    "-p:SelfContained=false",
    "-p:PublishSingleFile=false",
    "-p:RestoreLockedMode=true",
    "-p:NuGetLockFilePath=packages.framework-dependent.lock.json",
  ]);
}

export function restoreCleanTrackedTree(commit, execute = checked) {
  const status = execute("git", ["status", "--porcelain=v1", "--untracked-files=all"], {
    captureOutput: true,
  });
  if (!status) return;
  execute("git", ["restore", `--source=${commit}`, "--staged", "--worktree", "--", "."]);
  execute("git", ["clean", "-fd", "--", "."]);
  const remaining = execute("git", ["status", "--porcelain=v1", "--untracked-files=all"], {
    captureOutput: true,
  });
  if (remaining)
    throw new Error(`Release cleanup could not restore a clean repository: ${remaining}`);
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

export function auditReleaseArtifacts(
  release,
  expectedCommit,
  expectedNames,
  expectedDraft,
  expectedArtifacts = null,
  expectedReleaseMetadata = null,
) {
  if (!release || release.isDraft !== expectedDraft || release.targetCommitish !== expectedCommit) {
    throw new Error(
      `${expectedDraft ? "Draft" : "Published"} release audit failed: release state or target commit does not match.`,
    );
  }
  if (
    expectedReleaseMetadata &&
    (release.name !== expectedReleaseMetadata.releaseTitle ||
      createHash("sha256")
        .update(release.body ?? "", "utf8")
        .digest("hex") !== expectedReleaseMetadata.releaseBodySha256)
  ) {
    throw new Error(
      `${expectedDraft ? "Draft" : "Published"} release title or body does not match the packaged checkpoint.`,
    );
  }
  const actualNames = release.assets.map((asset) => asset.name).sort();
  if (actualNames.join("|") !== [...expectedNames].sort().join("|")) {
    throw new Error(
      `Draft release assets do not match the expected set: ${actualNames.join(", ")}`,
    );
  }
  for (const asset of release.assets) {
    if (asset.size <= 0 || !asset.digest) {
      throw new Error(`Release asset '${asset.name}' has invalid size or digest metadata.`);
    }
    const expected = expectedArtifacts?.find((artifact) => artifact.name === asset.name);
    if (
      expected &&
      (asset.size !== expected.size || asset.digest !== `sha256:${expected.sha256}`)
    ) {
      throw new Error(`Release asset '${asset.name}' does not match the packaged checkpoint.`);
    }
  }
}

function getReleaseHashes(release, expectedNames) {
  const assets = new Map(release.assets.map((asset) => [asset.name, asset]));
  return expectedNames.map((name) => {
    const digest = assets.get(name)?.digest;
    if (!digest?.startsWith("sha256:"))
      throw new Error(`Release asset '${name}' has no SHA-256 digest.`);
    return `${name} SHA-256 ${digest.slice("sha256:".length)}`;
  });
}

export function auditDraft(
  release,
  expectedCommit,
  expectedNames,
  expectedArtifacts = null,
  expectedReleaseMetadata = null,
) {
  auditReleaseArtifacts(
    release,
    expectedCommit,
    expectedNames,
    true,
    expectedArtifacts,
    expectedReleaseMetadata,
  );
}

async function performStep(progress, title, detail, action) {
  progress.start(title, detail);
  const result = await action();
  progress.complete();
  return result;
}

export async function withReleaseLock(action, { lockPath, makeDirectory, remove } = {}) {
  return withSharedReleaseLock(action, { repositoryRoot, lockPath, makeDirectory, remove });
}

export async function runLocalRelease(args = process.argv.slice(2), options = {}) {
  return withReleaseLock(() => runLocalReleaseUnlocked(args, options));
}

async function runLocalReleaseUnlocked(
  args = process.argv.slice(2),
  { progress, publishLatest = false, retainCheckpoint = false } = {},
) {
  const releaseProgress = progress ?? createReleaseProgress({ totalSteps: 5 });
  const { version: explicitVersion } = parseReleaseArguments(args);
  let releaseContext;

  await performStep(
    releaseProgress,
    "Checking release prerequisites",
    "Validating tools, Git state, GitHub access, and release notes.",
    async () => {
      if (process.platform !== "win32") {
        throw new Error("Voltura Air releases are supported only on Windows.");
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
      checked("npm", ["run", "tools:check"]);
      preflightPublishRestores();
      const initialStatus = checked("git", ["status", "--porcelain=v1", "--untracked-files=all"], {
        captureOutput: true,
      });
      if (initialStatus) {
        throw new Error("Release publication requires a clean Git working tree.");
      }
      const branch = checked("git", ["branch", "--show-current"], { captureOutput: true });
      if (branch !== "main") {
        throw new Error("Releases must run from the main branch.");
      }
      checked("git", ["var", "GIT_AUTHOR_IDENT"], { captureOutput: true });
      await assertGitStatePathAbsent("MERGE_HEAD");
      await assertGitStatePathAbsent("rebase-merge");
      await assertGitStatePathAbsent("rebase-apply");
      checked("git", ["remote", "get-url", "origin"], { captureOutput: true });

      const repository = checked(
        "gh",
        ["repo", "view", "--json", "nameWithOwner", "--jq", ".nameWithOwner"],
        { captureOutput: true },
      );
      const canPush = checked("gh", ["api", `repos/${repository}`, "--jq", ".permissions.push"], {
        captureOutput: true,
      });
      if (canPush !== "true") {
        throw new Error(
          `GitHub reports that the authenticated account cannot push to '${repository}'.`,
        );
      }
      checked("git", ["fetch", "origin", "main"]);
      checked("git", ["merge-base", "--is-ancestor", "origin/main", "HEAD"]);
      checked("git", ["push", "--dry-run", "origin", "HEAD:main"], { captureOutput: true });
      const releasePages = JSON.parse(
        checked(
          "gh",
          ["api", "--paginate", "--slurp", `repos/${repository}/releases?per_page=100`],
          { captureOutput: true },
        ),
      );
      const latest = resolveLatestPublishedRelease(releasePages.flat());

      const packagePath = path.join(repositoryRoot, "package.json");
      const currentVersion = String(JSON.parse(await readFile(packagePath, "utf8")).version ?? "");
      const currentTag = `v${currentVersion}`;
      const currentTagExists = remoteTagExists(currentTag);
      const currentRelease = getReleaseIfExists(currentTag, repository);
      const startingCommit = checked("git", ["rev-parse", "HEAD"], { captureOutput: true });
      const currentAssetPaths = getReleaseAssetPaths(repositoryRoot, currentVersion, runtime);
      const currentCheckpointMetadata = await readReleaseCheckpoint({
        repositoryRoot,
        version: currentVersion,
        commit: startingCommit,
        assetPaths: currentAssetPaths,
        verifyArtifacts: false,
      });
      const resumePublished =
        currentRelease?.isDraft === false &&
        currentCheckpointMetadata?.phase === "packaged" &&
        (explicitVersion === null || explicitVersion === currentVersion);
      if (resumePublished) {
        auditReleaseArtifacts(
          currentRelease,
          startingCommit,
          currentAssetPaths.map((assetPath) => path.basename(assetPath)),
          false,
          currentCheckpointMetadata.artifacts,
        );
      }
      const targetVersion = resumePublished
        ? currentVersion
        : resolveReleaseVersion({
            currentVersion,
            latestReleasedVersion: latest.version,
            explicitVersion,
            currentTagExists,
            currentReleaseIsDraft: currentRelease?.isDraft === true,
            getNextVersion: getNextReleaseVersion,
          });
      const targetTag = `v${targetVersion}`;
      const targetSemver = parseSemver(targetVersion);
      if (publishLatest && targetSemver.prerelease.length > 0) {
        throw new Error("A prerelease cannot be published as Latest; use release:draft instead.");
      }

      const notesPath = path.join(repositoryRoot, "docs", "release-notes.md");
      const releaseNotes = await readFile(notesPath, "utf8");
      const notes = getReleaseNotesSection(releaseNotes, targetVersion);
      const notices = getGeneralReleaseNotices(releaseNotes);
      const releaseTitle = `Voltura Air ${targetTag}`;
      const releaseBody = buildReleaseBody({
        notes,
        notices,
        version: targetVersion,
        latestTag: latest.tag,
        repository,
      });
      const targetTagExists =
        targetTag === currentTag ? currentTagExists : remoteTagExists(targetTag);
      const targetReleaseBeforeBuild =
        targetTag === currentTag ? currentRelease : getReleaseIfExists(targetTag, repository);
      if (targetReleaseBeforeBuild && !targetReleaseBeforeBuild.isDraft && !resumePublished) {
        throw new Error(`Release '${targetTag}' is already public. Prepare a new version instead.`);
      }
      if (
        targetReleaseBeforeBuild?.isDraft &&
        targetReleaseBeforeBuild.targetCommitish !== startingCommit
      ) {
        throw new Error(
          `Draft '${targetTag}' targets another commit and cannot be resumed from this checkout.`,
        );
      }

      const assetPaths = getReleaseAssetPaths(repositoryRoot, targetVersion, runtime);
      const assetNames = assetPaths.map((assetPath) => path.basename(assetPath));
      let resumePhase = null;
      let checkpoint = null;
      checkpoint =
        targetVersion === currentVersion
          ? resumePublished
            ? currentCheckpointMetadata
            : await readReleaseCheckpoint({
                repositoryRoot,
                version: currentVersion,
                commit: startingCommit,
                assetPaths: currentAssetPaths,
              })
          : await readReleaseCheckpoint({
              repositoryRoot,
              version: targetVersion,
              commit: startingCommit,
              assetPaths,
            });
      if (resumePublished) {
        resumePhase = "published";
      } else if (targetReleaseBeforeBuild?.isDraft) {
        if (checkpoint?.phase !== "packaged") {
          throw new Error(
            `Draft '${targetTag}' cannot be resumed without its verified packaged checkpoint.`,
          );
        }
        try {
          auditDraft(
            targetReleaseBeforeBuild,
            startingCommit,
            assetNames,
            checkpoint.artifacts,
            checkpoint,
          );
          resumePhase = "drafted";
        } catch (error) {
          resumePhase = "packaged";
        }
      } else {
        resumePhase = checkpoint?.phase ?? null;
      }

      releaseContext = {
        assetNames,
        assetPaths,
        checkpoint,
        latest,
        notes,
        notices,
        repository,
        releaseBody,
        releaseTitle,
        resumePhase,
        startingCommit,
        targetReleaseBeforeBuild,
        targetSemver,
        targetTag,
        targetVersion,
      };
    },
  );

  let releaseCommit = releaseContext.resumePhase ? releaseContext.startingCommit : null;
  const { assetPaths, assetNames } = releaseContext;
  let bodyPath;
  try {
    await performStep(
      releaseProgress,
      "Preparing release sources",
      "Checking source ownership, setting the version, and generating hosted outputs.",
      () => {
        if (releaseContext.resumePhase) {
          console.log(
            `Resuming ${releaseContext.targetTag} from the ${releaseContext.resumePhase} checkpoint; source preparation is already complete.`,
          );
          return;
        }
        checked("npm", ["run", "size:check"]);
        prepareReleaseUnlocked(releaseContext.targetVersion);
        checked("npm", ["run", "site:preview:build"]);
        checked("npm", ["run", "site:hosted:build"]);
        checked("npm", ["run", "code:statistics", "--", "--report", "--no-open", "--quiet"]);
      },
    );

    await performStep(
      releaseProgress,
      "Committing and creating final artifacts",
      "Committing prepared sources, packaging once from that exact commit, then pushing after validation.",
      async () => {
        if (
          releaseContext.resumePhase === "drafted" ||
          releaseContext.resumePhase === "published"
        ) {
          console.log(
            `The audited ${releaseContext.targetTag} release already contains the final artifacts; packaging and push are already complete.`,
          );
          return;
        }
        if (!releaseContext.resumePhase) {
          await stageReleaseChanges();
          const staged = runCommand("git", ["diff", "--cached", "--quiet"], { allowFailure: true });
          if (staged.status === 1) {
            checked("git", ["commit", "-m", `Release Voltura Air ${releaseContext.targetVersion}`]);
          } else if (staged.status !== 0) {
            throw new Error("Could not inspect staged release changes.");
          }
          releaseCommit = checked("git", ["rev-parse", "HEAD"], { captureOutput: true });
        }

        if (releaseContext.resumePhase !== "packaged") {
          checked("npm", ["run", "package:win"]);
          checked("node", [
            "scripts/sign-update-manifest.mjs",
            releaseContext.targetVersion,
            path.join(repositoryRoot, "artifacts", "publish"),
          ]);
          const finalStatus = checked(
            "git",
            ["status", "--porcelain=v1", "--untracked-files=all"],
            { captureOutput: true },
          );
          if (finalStatus) {
            throw new Error(`Repository changed during final release packaging: ${finalStatus}`);
          }
          await assertReleaseAssets(assetPaths);
          releaseContext.checkpoint = await writeReleaseCheckpoint({
            repositoryRoot,
            version: releaseContext.targetVersion,
            commit: releaseCommit,
            phase: "packaged",
            assetPaths,
            releaseTitle: releaseContext.releaseTitle,
            releaseBody: releaseContext.releaseBody,
          });
        } else {
          console.log(
            `Reusing the verified artifacts already packaged from commit ${releaseCommit}.`,
          );
        }

        checked("git", ["push", "origin", "main"]);
        bodyPath = path.join(
          repositoryRoot,
          "artifacts",
          "publish",
          `release-notes-${releaseContext.targetTag}.md`,
        );
        await writeFile(bodyPath, releaseContext.releaseBody, "utf8");
      },
    );

    await performStep(
      releaseProgress,
      "Creating and auditing the GitHub release",
      "Uploading the exact ZIP and installer set, then verifying GitHub metadata and digests.",
      () => {
        if (releaseContext.resumePhase === "published") {
          auditReleaseArtifacts(
            getRelease(releaseContext.targetTag, releaseContext.repository),
            releaseCommit,
            assetNames,
            false,
            releaseContext.checkpoint?.artifacts,
            releaseContext.checkpoint,
          );
          return;
        }
        const existingDraft = releaseContext.targetReleaseBeforeBuild;
        if (existingDraft === null) {
          const createArgs = [
            "release",
            "create",
            releaseContext.targetTag,
            "--repo",
            releaseContext.repository,
            "--target",
            releaseCommit,
            "--title",
            releaseContext.releaseTitle,
            "--draft",
            "--fail-on-no-commits",
            "--notes-file",
            bodyPath,
          ];
          if (releaseContext.targetSemver.prerelease.length > 0) createArgs.push("--prerelease");
          createArgs.push(...assetPaths);
          checked("gh", createArgs);
        } else if (releaseContext.resumePhase !== "drafted") {
          checked("gh", [
            "release",
            "edit",
            releaseContext.targetTag,
            "--repo",
            releaseContext.repository,
            "--title",
            releaseContext.releaseTitle,
            "--notes-file",
            bodyPath,
          ]);
          checked("gh", [
            "release",
            "upload",
            releaseContext.targetTag,
            "--repo",
            releaseContext.repository,
            "--clobber",
            ...assetPaths,
          ]);
        }
        const auditedDraft = getRelease(releaseContext.targetTag, releaseContext.repository);
        auditDraft(
          auditedDraft,
          releaseCommit,
          assetNames,
          releaseContext.checkpoint?.artifacts,
          releaseContext.checkpoint,
        );
      },
    );

    await performStep(
      releaseProgress,
      publishLatest ? "Publishing GitHub Latest" : "Finalizing the audited draft",
      publishLatest
        ? "Publishing the already audited public release as GitHub Latest."
        : "Leaving the audited public release as a draft.",
      () => {
        publishReleaseIfRequested({
          publishLatest,
          targetTag: releaseContext.targetTag,
          repository: releaseContext.repository,
          expectedCommit: releaseCommit,
        });
      },
    );

    const finalRelease = getRelease(releaseContext.targetTag, releaseContext.repository);
    auditReleaseArtifacts(
      finalRelease,
      releaseCommit,
      assetNames,
      !publishLatest,
      releaseContext.checkpoint?.phase === "packaged" ? releaseContext.checkpoint.artifacts : null,
      releaseContext.checkpoint?.phase === "packaged" ? releaseContext.checkpoint : null,
    );
    const hashes = getReleaseHashes(finalRelease, assetNames);
    if (publishLatest && !retainCheckpoint) {
      await rm(getReleaseCheckpointPath(repositoryRoot, releaseContext.targetVersion), {
        force: true,
      });
    }
    const url = `https://github.com/${releaseContext.repository}/releases/tag/${releaseContext.targetTag}`;
    return {
      hashes,
      publishLatest,
      summary: `${publishLatest ? "Published as GitHub Latest" : "Created audited GitHub draft"}: ${url}`,
      url,
    };
  } catch (error) {
    try {
      restoreCleanTrackedTree(releaseCommit ?? releaseContext.startingCommit);
    } catch (cleanupError) {
      throw new AggregateError(
        [error, cleanupError],
        `${error.message} Release cleanup also failed: ${cleanupError.message}`,
      );
    }
    throw error;
  }
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const cliArgs = process.argv.slice(2);
  const publishLatest = cliArgs[0] === "--publish-latest";
  const publishArgs = publishLatest ? cliArgs.slice(1) : cliArgs;
  const retainCheckpoint = publishArgs[0] === "--retain-checkpoint";
  const releaseArgs = retainCheckpoint ? publishArgs.slice(1) : publishArgs;
  const progress = createReleaseProgress({ totalSteps: 5 });
  runLocalRelease(releaseArgs, { progress, publishLatest, retainCheckpoint })
    .then((result) => {
      for (const hash of result.hashes) {
        console.log(hash);
      }
      progress.success(result.summary);
    })
    .catch((error) => {
      progress.issue(error);
      process.exitCode = 1;
    });
}
