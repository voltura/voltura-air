import { execFileSync } from "node:child_process";
import { readFileSync } from "node:fs";
import { rm } from "node:fs/promises";
import path from "node:path";
import process from "node:process";
import { fileURLToPath } from "node:url";

import {
  getReleaseAssetPaths,
  getReleaseCheckpointPath,
  readReleaseCheckpoint,
} from "./release-checkpoint.mjs";
import {
  auditDraft,
  auditReleaseArtifacts,
  publishReleaseIfRequested,
} from "./release-publish.mjs";
import { parseReleaseArguments, parseSemver } from "./release-tools.mjs";

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const repository = "voltura/voltura-air";
const runtime = "win-x64";

function checked(command, args) {
  return execFileSync(command, args, {
    cwd: repositoryRoot,
    encoding: "utf8",
    windowsHide: true,
  }).trim();
}

export function assertPreparedReleaseDraft({ version, commit, checkpoint, release, assetNames }) {
  if (checkpoint?.phase !== "packaged") {
    throw new Error(`Release v${version} has no verified packaged checkpoint.`);
  }
  if (
    release?.tagName !== `v${version}` ||
    release.isPrerelease !== parseSemver(version).prerelease.length > 0
  ) {
    throw new Error(`Draft v${version} has unexpected release metadata.`);
  }
  auditDraft(release, commit, assetNames, checkpoint.artifacts, checkpoint);
}

export function assertPreparedPublishedRelease({
  version,
  commit,
  taggedCommit,
  checkpoint,
  release,
  latest,
  assetNames,
}) {
  if (checkpoint?.phase !== "packaged") {
    throw new Error(`Release v${version} has no verified packaged checkpoint.`);
  }
  if (release?.tagName !== `v${version}` || release.isPrerelease !== false) {
    throw new Error(`Published release v${version} has unexpected release metadata.`);
  }
  if (taggedCommit !== commit) {
    throw new Error(`Published tag v${version} does not resolve to the reviewed commit.`);
  }
  auditReleaseArtifacts(release, commit, assetNames, false, checkpoint.artifacts, checkpoint);
  if (
    latest?.tag_name !== `v${version}` ||
    latest.draft !== false ||
    latest.target_commitish !== commit
  ) {
    throw new Error(`Published release v${version} is not GitHub Latest at the reviewed commit.`);
  }
}

export function publishPreparedReleaseDraft(
  { version, commit, checkpoint, release, assetNames },
  execute = checked,
) {
  assertPreparedReleaseDraft({ version, commit, checkpoint, release, assetNames });
  publishReleaseIfRequested(
    {
      publishLatest: true,
      targetTag: `v${version}`,
      repository,
      expectedCommit: commit,
    },
    execute,
  );
}

export async function verifyPreparedReleaseDraft(
  args = process.argv.slice(2),
  { publishedLatest = false, removeCheckpoint = false, publishAuditedDraft = false } = {},
) {
  if (publishedLatest && publishAuditedDraft) {
    throw new Error("Choose either audited-draft publication or published-release verification.");
  }
  const { version: explicitVersion } = parseReleaseArguments(args);
  const version = String(
    JSON.parse(readFileSync(path.join(repositoryRoot, "package.json"), "utf8")).version ?? "",
  );
  if (explicitVersion !== null && explicitVersion !== version) {
    throw new Error(`The reviewed checkout is v${version}, not v${explicitVersion}.`);
  }
  if (checked("git", ["branch", "--show-current"]) !== "main") {
    throw new Error("Release draft verification must run from public main.");
  }
  if (checked("git", ["status", "--porcelain=v1", "--untracked-files=all"])) {
    throw new Error("Release draft verification requires a clean public checkout.");
  }
  checked("git", ["fetch", "--quiet", "origin", "main"]);
  const commit = checked("git", ["rev-parse", "HEAD"]);
  if (checked("git", ["rev-parse", "refs/remotes/origin/main"]) !== commit) {
    throw new Error("Public main must exactly match origin/main before production deployment.");
  }

  const assetPaths = getReleaseAssetPaths(repositoryRoot, version, runtime);
  const checkpoint = await readReleaseCheckpoint({ repositoryRoot, version, commit, assetPaths });
  const release = JSON.parse(
    checked("gh", [
      "release",
      "view",
      `v${version}`,
      "--repo",
      repository,
      "--json",
      "tagName,name,body,isDraft,isPrerelease,targetCommitish,url,assets",
    ]),
  );
  const common = {
    version,
    commit,
    checkpoint,
    release,
    assetNames: assetPaths.map((assetPath) => path.basename(assetPath)),
  };
  if (publishedLatest) {
    checked("git", ["fetch", "--quiet", "--force", "origin", `refs/tags/v${version}`]);
    const taggedCommit = checked("git", ["rev-parse", "FETCH_HEAD^{commit}"]);
    const latest = JSON.parse(checked("gh", ["api", `repos/${repository}/releases/latest`]));
    assertPreparedPublishedRelease({ ...common, latest, taggedCommit });
    if (removeCheckpoint) {
      await rm(getReleaseCheckpointPath(repositoryRoot, version), { force: true });
    }
    console.log(`Verified GitHub Latest v${version} and its packaged artifacts at ${commit}.`);
  } else if (publishAuditedDraft) {
    publishPreparedReleaseDraft(common);
    const publishedRelease = JSON.parse(
      checked("gh", [
        "release",
        "view",
        `v${version}`,
        "--repo",
        repository,
        "--json",
        "tagName,name,body,isDraft,isPrerelease,targetCommitish,url,assets",
      ]),
    );
    checked("git", ["fetch", "--quiet", "--force", "origin", `refs/tags/v${version}`]);
    const taggedCommit = checked("git", ["rev-parse", "FETCH_HEAD^{commit}"]);
    const latest = JSON.parse(checked("gh", ["api", `repos/${repository}/releases/latest`]));
    assertPreparedPublishedRelease({
      ...common,
      release: publishedRelease,
      latest,
      taggedCommit,
    });
    console.log(`Published the existing audited GitHub draft v${version} as Latest at ${commit}.`);
  } else {
    assertPreparedReleaseDraft(common);
    console.log(`Verified audited GitHub draft v${version} at ${commit}.`);
  }
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const cliArgs = process.argv.slice(2);
  const publishedLatest = cliArgs[0] === "--published-latest";
  const publishAuditedDraft = cliArgs[0] === "--publish-audited-draft";
  const modeArgs = publishedLatest || publishAuditedDraft ? cliArgs.slice(1) : cliArgs;
  const publishedArgs = publishedLatest ? modeArgs : [];
  const removeCheckpoint = publishedArgs[0] === "--remove-checkpoint";
  const releaseArgs = publishedLatest
    ? removeCheckpoint
      ? publishedArgs.slice(1)
      : publishedArgs
    : modeArgs;
  verifyPreparedReleaseDraft(releaseArgs, {
    publishedLatest,
    removeCheckpoint,
    publishAuditedDraft,
  }).catch((error) => {
    console.error(error instanceof Error ? error.message : String(error));
    process.exitCode = 1;
  });
}
