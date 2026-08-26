import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import {
  copyFile,
  link,
  mkdtemp,
  mkdir,
  readFile,
  readdir,
  rename,
  rm,
  stat,
  writeFile,
} from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import test from "node:test";

import { getNextReleaseVersion } from "../../scripts/bump-release.mjs";
import {
  getReleaseAssetPaths,
  readReleaseCheckpoint,
  validateReleaseCheckpoint,
  writeReleaseCheckpoint,
} from "../../scripts/release-checkpoint.mjs";
import {
  auditDraft,
  buildReleaseBody,
  getRelease,
  getReleaseIfExists,
  publishReleaseIfRequested,
  restoreCleanTrackedTree,
  withReleaseLock,
} from "../../scripts/release-publish.mjs";
import {
  compareSemver,
  extractUserFacingReleaseNotes,
  extractMarkedReleaseNotes,
  freewareNotice,
  getReleaseNotesSection,
  getGeneralReleaseNotices,
  parseReleaseArguments,
  parseSyncReleaseArguments,
  releaseNotesEndMarker,
  releaseNotesStartMarker,
  replaceReleaseNotesSection,
  resolveLatestPublishedRelease,
  resolveReleaseVersion,
  unsignedReleaseNotice,
} from "../../scripts/release-tools.mjs";

const requiredNotices = `${freewareNotice}\n\n${unsignedReleaseNotice}`;
const auditedReleaseTitle = "Voltura Air v1.0.5";
const auditedReleaseBody = "Reviewed release body.\n";
const auditedReleaseMetadata = {
  releaseTitle: auditedReleaseTitle,
  releaseBodySha256: createHash("sha256").update(auditedReleaseBody, "utf8").digest("hex"),
};
const localReleaseSource = await readFile(
  new URL("../../scripts/release-publish.mjs", import.meta.url),
  "utf8",
);
const prepareReleaseSource = await readFile(
  new URL("../../scripts/prepare-release.ps1", import.meta.url),
  "utf8",
);
const prepareReleaseWrapperSource = await readFile(
  new URL("../../scripts/prepare-release.mjs", import.meta.url),
  "utf8",
);
const releaseLockSource = await readFile(
  new URL("../../scripts/release-lock.mjs", import.meta.url),
  "utf8",
);
const releaseVerifierSource = await readFile(
  new URL("../../scripts/verify-release-draft.mjs", import.meta.url),
  "utf8",
);
import { restoreGithubActions } from "../../scripts/restore-github-actions.mjs";
import { resolveSynchronizedRelease } from "../../scripts/sync-release-notes.mjs";
import {
  assertPreparedPublishedRelease,
  assertPreparedReleaseDraft,
  publishPreparedReleaseDraft,
} from "../../scripts/verify-release-draft.mjs";

test("release commands accept at most one explicit version", () => {
  assert.deepEqual(parseReleaseArguments([]), { version: null });
  assert.deepEqual(parseReleaseArguments(["0.8.0"]), { version: "0.8.0" });
  assert.throws(() => parseReleaseArguments(["0.8.0", "extra"]), /Usage/u);
  assert.throws(() => parseReleaseArguments(["latest"]), /semantic versioning/u);
});

test("production draft verification binds release assets to the packaged checkpoint", () => {
  const artifacts = [
    { name: "VolturaAir-1.0.5-win-x64.zip", size: 10, sha256: "a".repeat(64) },
    { name: "VolturaAir-Setup-1.0.5-win-x64.exe", size: 20, sha256: "b".repeat(64) },
    { name: "VolturaAir-Setup-1.0.5-win-x64-full.exe", size: 30, sha256: "c".repeat(64) },
  ];
  const release = {
    tagName: "v1.0.5",
    name: auditedReleaseTitle,
    body: auditedReleaseBody,
    isDraft: true,
    isPrerelease: false,
    targetCommitish: "d".repeat(40),
    assets: artifacts.map((artifact) => ({
      name: artifact.name,
      size: artifact.size,
      digest: `sha256:${artifact.sha256}`,
    })),
  };
  const input = {
    version: "1.0.5",
    commit: "d".repeat(40),
    checkpoint: { phase: "packaged", artifacts, ...auditedReleaseMetadata },
    release,
    assetNames: artifacts.map((artifact) => artifact.name),
  };
  assert.doesNotThrow(() => assertPreparedReleaseDraft(input));
  assert.throws(
    () =>
      assertPreparedReleaseDraft({
        ...input,
        release: { ...release, targetCommitish: "e".repeat(40) },
      }),
    /target commit/u,
  );
});

test("audited publication refuses a changed or deleted draft before any GitHub mutation", () => {
  const artifacts = [
    { name: "VolturaAir-1.0.5-win-x64.zip", size: 10, sha256: "a".repeat(64) },
    { name: "VolturaAir-Setup-1.0.5-win-x64.exe", size: 20, sha256: "b".repeat(64) },
    { name: "VolturaAir-Setup-1.0.5-win-x64-full.exe", size: 30, sha256: "c".repeat(64) },
  ];
  const commit = "d".repeat(40);
  const release = {
    tagName: "v1.0.5",
    name: auditedReleaseTitle,
    body: auditedReleaseBody,
    isDraft: true,
    isPrerelease: false,
    targetCommitish: commit,
    assets: artifacts.map((artifact) => ({
      name: artifact.name,
      size: artifact.size,
      digest: `sha256:${artifact.sha256}`,
    })),
  };
  const input = {
    version: "1.0.5",
    commit,
    checkpoint: { phase: "packaged", artifacts, ...auditedReleaseMetadata },
    release,
    assetNames: artifacts.map((artifact) => artifact.name),
  };
  const calls = [];
  const execute = (command, args) => {
    calls.push([command, args]);
    if (args[0] === "api") {
      return JSON.stringify({ tag_name: "v1.0.5", draft: false, target_commitish: commit });
    }
    return "";
  };

  assert.throws(
    () =>
      publishPreparedReleaseDraft(
        {
          ...input,
          release: {
            ...release,
            assets: release.assets.map((asset, index) =>
              index === 0 ? { ...asset, digest: `sha256:${"f".repeat(64)}` } : asset,
            ),
          },
        },
        execute,
      ),
    /packaged checkpoint/u,
  );
  assert.throws(
    () => publishPreparedReleaseDraft({ ...input, release: null }, execute),
    /unexpected release metadata/u,
  );
  assert.throws(
    () =>
      publishPreparedReleaseDraft(
        {
          ...input,
          release: { ...release, name: "Changed release title" },
        },
        execute,
      ),
    /title or body/u,
  );
  assert.throws(
    () =>
      publishPreparedReleaseDraft(
        {
          ...input,
          release: { ...release, body: "Changed release body." },
        },
        execute,
      ),
    /title or body/u,
  );
  assert.deepEqual(calls, []);

  publishPreparedReleaseDraft(input, execute);
  assert.deepEqual(calls, [
    [
      "gh",
      ["release", "edit", "v1.0.5", "--repo", "voltura/voltura-air", "--draft=false", "--latest"],
    ],
    ["gh", ["api", "repos/voltura/voltura-air/releases/latest"]],
  ]);
  assert.doesNotMatch(releaseVerifierSource, /"release", "(?:create|upload)"|--clobber/u);
});

test("published production resume requires exact packaged assets and GitHub Latest", () => {
  const artifacts = [
    { name: "VolturaAir-1.0.5-win-x64.zip", size: 10, sha256: "a".repeat(64) },
    { name: "VolturaAir-Setup-1.0.5-win-x64.exe", size: 20, sha256: "b".repeat(64) },
    { name: "VolturaAir-Setup-1.0.5-win-x64-full.exe", size: 30, sha256: "c".repeat(64) },
  ];
  const commit = "d".repeat(40);
  const release = {
    tagName: "v1.0.5",
    name: auditedReleaseTitle,
    body: auditedReleaseBody,
    isDraft: false,
    isPrerelease: false,
    targetCommitish: commit,
    assets: artifacts.map((artifact) => ({
      name: artifact.name,
      size: artifact.size,
      digest: `sha256:${artifact.sha256}`,
    })),
  };
  const input = {
    version: "1.0.5",
    commit,
    taggedCommit: commit,
    checkpoint: { phase: "packaged", artifacts, ...auditedReleaseMetadata },
    release,
    latest: { tag_name: "v1.0.5", draft: false, target_commitish: commit },
    assetNames: artifacts.map((artifact) => artifact.name),
  };
  assert.doesNotThrow(() => assertPreparedPublishedRelease(input));
  assert.throws(
    () => assertPreparedPublishedRelease({ ...input, taggedCommit: "e".repeat(40) }),
    /tag .*reviewed commit/u,
  );
  assert.throws(
    () =>
      assertPreparedPublishedRelease({
        ...input,
        release: { ...release, assets: release.assets.slice(1) },
      }),
    /assets do not match/u,
  );
  assert.throws(
    () =>
      assertPreparedPublishedRelease({
        ...input,
        release: {
          ...release,
          assets: release.assets.map((asset, index) =>
            index === 0 ? { ...asset, digest: `sha256:${"f".repeat(64)}` } : asset,
          ),
        },
      }),
    /packaged checkpoint/u,
  );
  assert.throws(
    () =>
      assertPreparedPublishedRelease({ ...input, latest: { ...input.latest, tag_name: "v1.0.4" } }),
    /not GitHub Latest/u,
  );
  assert.throws(
    () =>
      assertPreparedPublishedRelease({
        ...input,
        release: { ...release, body: "Changed after publication." },
      }),
    /title or body/u,
  );
});

test("release publication prepares all public site outputs before staging", () => {
  const previewBuild = localReleaseSource.indexOf('"site:preview:build"');
  const hostedBuild = localReleaseSource.indexOf('"site:hosted:build"');
  const generation = localReleaseSource.indexOf('"code:statistics"');
  const staging = localReleaseSource.indexOf("await stageReleaseChanges()");
  assert.ok(previewBuild > 0);
  assert.ok(hostedBuild > previewBuild);
  assert.ok(generation > hostedBuild);
  assert.ok(generation > 0);
  assert.ok(staging > generation);
  assert.match(localReleaseSource, /"code:statistics", "--", "--report", "--no-open", "--quiet"/u);
  assert.equal(localReleaseSource.match(/"site:hosted:build"/gu)?.length, 1);
  assert.doesNotMatch(localReleaseSource, /"publish:site:prepared"/u);
});

test("release packaging runs once from the final local commit before push", () => {
  const staging = localReleaseSource.indexOf("await stageReleaseChanges()");
  const commit = localReleaseSource.indexOf('checked("git", ["commit"');
  const packaging = localReleaseSource.indexOf('checked("npm", ["run", "package:win"]');
  const push = localReleaseSource.indexOf('checked("git", ["push", "origin", "main"])');
  const packageCommands =
    localReleaseSource.match(/checked\("npm", \["run", "package:win"\]\)/gu) ?? [];

  assert.ok(staging > 0);
  assert.ok(commit > staging);
  assert.ok(packaging > commit);
  assert.ok(push > packaging);
  assert.equal(packageCommands.length, 1);
  assert.doesNotMatch(localReleaseSource, /"package:win", "--"/u);
});

test("release validates source and Git without rerunning the development test suite", () => {
  const tools = localReleaseSource.indexOf('checked("npm", ["run", "tools:check"]');
  const locks = localReleaseSource.indexOf("preflightPublishRestores()");
  const push = localReleaseSource.indexOf('checked("git", ["push", "--dry-run"');
  for (const preflight of [tools, locks, push]) {
    assert.ok(preflight > 0);
  }
  assert.doesNotMatch(localReleaseSource, /checked\("npm", \["test"\]\)/u);
  assert.doesNotMatch(localReleaseSource, /"branding:generate"/u);
  assert.match(localReleaseSource, /packages\.self-contained\.lock\.json/u);
  assert.match(localReleaseSource, /packages\.framework-dependent\.lock\.json/u);
});

test("release checkpoints are exact to version, commit, phase, and artifact set", () => {
  const context = { version: "0.9.5", commit: "abc123", expectedNames: ["a.zip", "b.exe"] };
  const artifacts = [
    { name: "a.zip", size: 10, sha256: "a".repeat(64) },
    { name: "b.exe", size: 20, sha256: "b".repeat(64) },
  ];
  const releaseMetadata = {
    releaseTitle: "Voltura Air v0.9.5",
    releaseBodySha256: "c".repeat(64),
  };
  assert.deepEqual(
    validateReleaseCheckpoint(
      {
        schema: 2,
        version: "0.9.5",
        commit: "abc123",
        phase: "packaged",
        artifacts,
        ...releaseMetadata,
      },
      context,
    ),
    { phase: "packaged", artifacts, ...releaseMetadata },
  );
  assert.equal(
    validateReleaseCheckpoint(
      {
        schema: 2,
        version: "0.9.5",
        commit: "other",
        phase: "packaged",
        artifacts,
        ...releaseMetadata,
      },
      context,
    ),
    null,
  );
  assert.equal(
    validateReleaseCheckpoint(
      {
        schema: 2,
        version: "0.9.5",
        commit: "abc123",
        phase: "packaged",
        artifacts: artifacts.slice(1),
        ...releaseMetadata,
      },
      context,
    ),
    null,
  );
  assert.equal(
    validateReleaseCheckpoint(
      {
        schema: 1,
        version: "0.9.5",
        commit: "abc123",
        phase: "packaged",
        artifacts,
        ...releaseMetadata,
      },
      context,
    ),
    null,
  );
  assert.equal(
    validateReleaseCheckpoint(
      {
        schema: 2,
        version: "0.9.5",
        commit: "abc123",
        phase: "packaged",
        artifacts,
        ...releaseMetadata,
        releaseBodySha256: "invalid",
      },
      context,
    ),
    null,
  );
});

test("packaged checkpoints verify local bytes but can audit a published release without local artifacts", async () => {
  const root = await mkdtemp(path.join(os.tmpdir(), "voltura-release-checkpoint-"));
  try {
    const version = "0.9.5";
    const commit = "abc123";
    const assetPaths = getReleaseAssetPaths(root, version, "win-x64");
    await mkdir(path.dirname(assetPaths[0]), { recursive: true });
    await Promise.all(
      assetPaths.map((assetPath, index) => writeFile(assetPath, `artifact-${index}`)),
    );
    await writeReleaseCheckpoint({
      repositoryRoot: root,
      version,
      commit,
      phase: "packaged",
      assetPaths,
      releaseTitle: "Voltura Air v0.9.5",
      releaseBody: "Reviewed body.\n",
    });
    assert.equal(
      (await readReleaseCheckpoint({ repositoryRoot: root, version, commit, assetPaths }))?.phase,
      "packaged",
    );

    await rm(assetPaths[0]);
    assert.equal(
      await readReleaseCheckpoint({ repositoryRoot: root, version, commit, assetPaths }),
      null,
    );
    assert.equal(
      (
        await readReleaseCheckpoint({
          repositoryRoot: root,
          version,
          commit,
          assetPaths,
          verifyArtifacts: false,
        })
      )?.phase,
      "packaged",
    );
  } finally {
    await rm(root, { recursive: true, force: true });
  }
});

test("release failures restore tracked release changes and checkpoints skip completed work", () => {
  assert.match(
    localReleaseSource,
    /restoreCleanTrackedTree\(releaseCommit \?\? releaseContext\.startingCommit\)/u,
  );
  assert.match(localReleaseSource, /phase: "packaged"/u);
  assert.match(localReleaseSource, /audited .* release already contains the final artifacts/u);
});

test("production orchestration can retain and then remove an audited published checkpoint", () => {
  assert.match(
    localReleaseSource,
    /const retainCheckpoint = publishArgs\[0\] === "--retain-checkpoint"/u,
  );
  assert.match(localReleaseSource, /publishLatest && !retainCheckpoint/u);
  assert.match(localReleaseSource, /rm\(getReleaseCheckpointPath/u);
  assert.match(
    releaseVerifierSource,
    /const removeCheckpoint = publishedArgs\[0\] === "--remove-checkpoint"/u,
  );
  const publishedAudit = releaseVerifierSource.indexOf("assertPreparedPublishedRelease");
  const checkpointRemoval = releaseVerifierSource.lastIndexOf("await rm(getReleaseCheckpointPath");
  assert.ok(
    checkpointRemoval > publishedAudit,
    "checkpoint removal must follow the published release audit",
  );
});

test("release failure cleanup restores its exact commit and removes generated untracked files", () => {
  const commands = [];
  const statuses = [" M package.json\n?? generated.txt", ""];
  restoreCleanTrackedTree("abc123", (command, args) => {
    commands.push([command, args]);
    return args[0] === "status" ? statuses.shift() : "";
  });
  assert.deepEqual(commands, [
    ["git", ["status", "--porcelain=v1", "--untracked-files=all"]],
    ["git", ["restore", "--source=abc123", "--staged", "--worktree", "--", "."]],
    ["git", ["clean", "-fd", "--", "."]],
    ["git", ["status", "--porcelain=v1", "--untracked-files=all"]],
  ]);

  assert.throws(
    () =>
      restoreCleanTrackedTree("abc123", (_command, args) =>
        args[0] === "status" ? " M package.json" : "",
      ),
    /could not restore a clean repository/u,
  );
});

test("release staging never removes a Git index lock owned by another operation", () => {
  assert.doesNotMatch(localReleaseSource, /await rm\(lockPath\)/u);
  assert.doesNotMatch(localReleaseSource, /minimumStaleAgeMs/u);
  assert.match(localReleaseSource, /Resolve any active Git operation or lock before retrying/u);
});

test("release execution is exclusive and always removes its own lock", async () => {
  const root = await mkdtemp(path.join(os.tmpdir(), "voltura-release-lock-"));
  const lockPath = path.join(root, "release.lock");
  try {
    await mkdir(lockPath);
    await assert.rejects(
      () =>
        withReleaseLock(async () => assert.fail("a competing release must not start"), {
          lockPath,
        }),
      /already running/u,
    );
    await rm(lockPath, { recursive: true });

    await assert.rejects(
      () =>
        withReleaseLock(
          async () => {
            throw new Error("injected release failure");
          },
          { lockPath },
        ),
      /injected release failure/u,
    );
    await assert.rejects(() => readFile(lockPath), /ENOENT/u);
    assert.equal(await withReleaseLock(async () => "complete", { lockPath }), "complete");
    await assert.rejects(() => readFile(lockPath), /ENOENT/u);
  } finally {
    await rm(root, { recursive: true, force: true });
  }
});

test("standalone and full release preparation share one focused transactional owner", () => {
  assert.match(prepareReleaseWrapperSource, /withReleaseLock/u);
  assert.match(localReleaseSource, /prepareReleaseUnlocked/u);
  assert.match(localReleaseSource, /withSharedReleaseLock/u);
  assert.match(releaseLockSource, /voltura-air-release\.lock/u);
  assert.match(prepareReleaseSource, /voltura-air-release-preparation\.json/u);
  assert.match(prepareReleaseSource, /FileOptions\]::WriteThrough/u);
  assert.match(prepareReleaseSource, /\[IO\.File\]::Replace/u);
  assert.match(prepareReleaseSource, /contains unexpected content/u);
  assert.match(prepareReleaseSource, /Rollback-ReleaseTransaction/u);
  assert.match(prepareReleaseSource, /AggregateException/u);
});

test("local draft completion does not run publication or tag commands", () => {
  const commands = [];
  publishReleaseIfRequested(
    {
      publishLatest: false,
      targetTag: "v0.8.0",
      repository: "voltura/voltura-air",
      expectedCommit: "abc123",
    },
    (command, args) => commands.push([command, args]),
  );

  assert.deepEqual(commands, []);
});

test("public release never deploys hosted service infrastructure", () => {
  const latestPublication = localReleaseSource.lastIndexOf("publishReleaseIfRequested({");
  assert.ok(latestPublication > 0);
  assert.doesNotMatch(localReleaseSource, /relay:deploy|relay:health|publish:site/u);
  assert.doesNotMatch(localReleaseSource, /wrangler/u);
});

test("local release does not fetch tags into the checkout", () => {
  assert.doesNotMatch(localReleaseSource, /git[^\n]+fetch[^\n]+--tags/u);
  assert.doesNotMatch(localReleaseSource, /refs\/tags\/\$\{targetTag\}:refs\/tags/u);
});

test("local latest completion publishes and verifies through GitHub without fetching a tag", () => {
  const commands = [];
  publishReleaseIfRequested(
    {
      publishLatest: true,
      targetTag: "v0.8.0",
      repository: "voltura/voltura-air",
      expectedCommit: "abc123",
    },
    (command, args) => {
      commands.push([command, args]);
      return command === "gh" && args[0] === "api"
        ? JSON.stringify({ tag_name: "v0.8.0", draft: false, target_commitish: "abc123" })
        : "";
    },
  );

  assert.deepEqual(commands, [
    [
      "gh",
      ["release", "edit", "v0.8.0", "--repo", "voltura/voltura-air", "--draft=false", "--latest"],
    ],
    ["gh", ["api", "repos/voltura/voltura-air/releases/latest"]],
  ]);
  assert.equal(
    commands.some(([command]) => command === "git"),
    false,
  );
});

test("semantic version ordering handles stable and prerelease versions", () => {
  assert.equal(compareSemver("0.8.0", "0.7.9"), 1);
  assert.equal(compareSemver("0.8.0-beta.2", "0.8.0-beta.1"), 1);
  assert.equal(compareSemver("0.8.0", "0.8.0-beta.2"), 1);
  assert.equal(compareSemver("0.8.0-BETA", "0.8.0-alpha"), -1);
  assert.equal(compareSemver("0.8.0+build.2", "0.8.0+build.1"), 0);
});

test("latest published release ordering includes prereleases and excludes drafts", () => {
  const stable = { tag_name: "v0.7.3", draft: false, prerelease: false };
  const prerelease = { tag_name: "v0.8.0-beta.2", draft: false, prerelease: true };
  const newerDraft = { tag_name: "v0.9.0", draft: true, prerelease: false };
  assert.deepEqual(resolveLatestPublishedRelease([stable, prerelease, newerDraft]), {
    release: prerelease,
    tag: "v0.8.0-beta.2",
    version: "0.8.0-beta.2",
  });
  assert.throws(
    () =>
      resolveReleaseVersion({
        currentVersion: "0.7.3",
        latestReleasedVersion: "0.8.0-beta.2",
        explicitVersion: "0.8.0-beta.1",
        currentTagExists: false,
        currentReleaseIsDraft: false,
        getNextVersion: getNextReleaseVersion,
      }),
    /must be newer/u,
  );
  assert.throws(
    () =>
      resolveReleaseVersion({
        currentVersion: "0.7.3",
        latestReleasedVersion: "0.8.0-beta.2",
        explicitVersion: null,
        currentTagExists: true,
        currentReleaseIsDraft: false,
        getNextVersion: getNextReleaseVersion,
      }),
    /Resolved version '0\.7\.4' must be newer/u,
  );
  assert.throws(
    () =>
      resolveReleaseVersion({
        currentVersion: "0.8.0-beta.1",
        latestReleasedVersion: "0.8.0-beta.2",
        explicitVersion: null,
        currentTagExists: true,
        currentReleaseIsDraft: true,
        getNextVersion: getNextReleaseVersion,
      }),
    /Resolved version '0\.8\.0-beta\.1' must be newer/u,
  );
  assert.throws(() => resolveLatestPublishedRelease([newerDraft]), /published release version/u);
});

test("release lookup propagates GitHub failures instead of reporting absence", () => {
  const failure = new Error("GitHub request failed");
  assert.throws(
    () =>
      getRelease("v0.8.0", "voltura/voltura-air", () => {
        throw failure;
      }),
    (error) => error === failure,
  );
  assert.deepEqual(
    getRelease("v0.8.0", "voltura/voltura-air", () =>
      JSON.stringify({
        tagName: "v0.8.0",
        isDraft: true,
      }),
    ),
    { tagName: "v0.8.0", isDraft: true },
  );
});

test("release discovery finds drafts without remote tags and only treats not-found as absence", () => {
  const draft = { tagName: "v0.8.0", isDraft: true, targetCommitish: "abc123" };
  assert.deepEqual(
    getReleaseIfExists("v0.8.0", "voltura/voltura-air", () => ({
      status: 0,
      stdout: JSON.stringify(draft),
      stderr: "",
    })),
    draft,
  );
  assert.equal(
    getReleaseIfExists("v0.8.0", "voltura/voltura-air", () => ({
      status: 1,
      stdout: "",
      stderr: "release not found",
    })),
    null,
  );
  assert.throws(
    () =>
      getReleaseIfExists("v0.8.0", "voltura/voltura-air", () => ({
        status: 1,
        stdout: "",
        stderr: "authentication failed",
      })),
    /authentication failed/u,
  );
});

test("release-note synchronization selects Latest by default or one explicit version", () => {
  assert.deepEqual(parseSyncReleaseArguments([]), { version: null });
  assert.deepEqual(parseSyncReleaseArguments(["0.8.0"]), { version: "0.8.0" });
  assert.deepEqual(parseSyncReleaseArguments(["0.8.0-beta.1"]), { version: "0.8.0-beta.1" });
  assert.throws(() => parseSyncReleaseArguments(["v0.8.0"]), /semantic versioning/u);
  assert.throws(() => parseSyncReleaseArguments(["0.8.0", "extra"]), /Usage/u);
});

test("release-note synchronization accepts only the intended published release", () => {
  const stable = {
    tagName: "v0.8.0",
    isDraft: false,
    isPrerelease: false,
    body: "notes",
    url: "stable",
  };
  assert.equal(resolveSynchronizedRelease(stable).version, "0.8.0");
  assert.equal(
    resolveSynchronizedRelease(
      { ...stable, tagName: "v0.9.0-beta.1", isPrerelease: true },
      "0.9.0-beta.1",
    ).version,
    "0.9.0-beta.1",
  );
  assert.throws(
    () => resolveSynchronizedRelease({ ...stable, isDraft: true }),
    /published release/u,
  );
  assert.throws(
    () => resolveSynchronizedRelease({ ...stable, tagName: "v0.9.0-beta.1", isPrerelease: true }),
    /GitHub Latest/u,
  );
  assert.throws(() => resolveSynchronizedRelease(stable, "0.8.1"), /instead of the requested/u);
});

test("release resolution bumps published versions and resumes pending drafts", () => {
  const common = {
    latestReleasedVersion: "0.7.3",
    explicitVersion: null,
    getNextVersion: getNextReleaseVersion,
  };
  assert.equal(
    resolveReleaseVersion({
      ...common,
      currentVersion: "0.7.3",
      currentTagExists: true,
      currentReleaseIsDraft: false,
    }),
    "0.7.4",
  );
  assert.equal(
    resolveReleaseVersion({
      ...common,
      currentVersion: "0.7.4",
      currentTagExists: true,
      currentReleaseIsDraft: true,
    }),
    "0.7.4",
  );
  assert.equal(
    resolveReleaseVersion({
      ...common,
      currentVersion: "0.7.3",
      explicitVersion: "0.8.0",
      currentTagExists: true,
      currentReleaseIsDraft: false,
    }),
    "0.8.0",
  );
  assert.throws(
    () =>
      resolveReleaseVersion({
        ...common,
        currentVersion: "0.7.3",
        explicitVersion: "0.7.3",
        currentTagExists: true,
        currentReleaseIsDraft: false,
      }),
    /must be newer/u,
  );
});

test("release notes require one non-placeholder section and one shared notices section", () => {
  const notes = `## v0.7.4\n\n- New visible behavior.\n\n## v0.7.3\n\n- Previous release.\n\n## General notices\n\n${requiredNotices}\n`;
  assert.equal(getReleaseNotesSection(notes, "0.7.4"), "- New visible behavior.");
  assert.equal(getGeneralReleaseNotices(notes), requiredNotices);
  assert.throws(
    () => getReleaseNotesSection("## v0.7.4\n\n<!-- Add notes. -->\n", "0.7.4"),
    /user-facing changes/u,
  );
  assert.throws(
    () =>
      getReleaseNotesSection(
        `## v0.7.4\n\n- Changed.\n\n${requiredNotices}\n\n## General notices\n\n${requiredNotices}\n`,
        "0.7.4",
      ),
    /must not repeat/u,
  );
  assert.throws(() => getGeneralReleaseNotices("## v0.7.4\n\n- Changed.\n"), /General notices/u);
  assert.throws(
    () => getReleaseNotesSection("## v0.7.4\n- One\n## v0.7.4\n- Two\n", "0.7.4"),
    /exactly one/u,
  );
});

test("marked release-note extraction requires safe boundaries and canonical notices", () => {
  const content = `## Highlights\n\n- Edited on GitHub.\n\n<!-- Keep this editorial note. -->\n\n${requiredNotices}`;
  const body = `## What's new\n\n${releaseNotesStartMarker}\n${content}\n${releaseNotesEndMarker}\n\n## Downloads\n\n- Installer`;
  assert.equal(extractMarkedReleaseNotes(body), content);
  assert.throws(() => extractMarkedReleaseNotes(content), /marker pair/u);
  assert.throws(
    () =>
      extractMarkedReleaseNotes(`${releaseNotesEndMarker}\n${content}\n${releaseNotesStartMarker}`),
    /reversed/u,
  );
  assert.throws(
    () =>
      extractMarkedReleaseNotes(`${releaseNotesStartMarker}\n- Changed.\n${releaseNotesEndMarker}`),
    /freeware notice/u,
  );
  assert.throws(
    () =>
      extractMarkedReleaseNotes(
        `${releaseNotesStartMarker}\n## v0.9.0\n\n${content}\n${releaseNotesEndMarker}`,
      ),
    /version section heading/u,
  );
});

test("release-note replacement changes only the matching section and is idempotent", () => {
  const original = `# Release notes\r\n\r\n## v0.8.0\r\n\r\n- Old.\r\n\r\n## v0.7.3\r\n\r\n- Keep.\r\n\r\n## General notices\r\n\r\n${requiredNotices.replaceAll("\n", "\r\n")}\r\n`;
  const replacement = "## Highlights\n\n- Edited on GitHub.";
  const updated = replaceReleaseNotesSection(original, "0.8.0", replacement);
  assert.match(updated, /## v0\.8\.0\r\n\r\n## Highlights\r\n\r\n- Edited on GitHub\./u);
  assert.match(updated, /## v0\.7\.3\r\n\r\n- Keep\./u);
  assert.equal(updated.includes("\n") && !updated.includes("\r\n"), false);
  assert.equal(replaceReleaseNotesSection(updated, "0.8.0", replacement), updated);
  assert.throws(() => replaceReleaseNotesSection(original, "0.9.0", replacement), /found 0/u);
});

test("release notes keep one shared canonical notices section", async () => {
  const notes = await readFile(new URL("../../docs/release-notes.md", import.meta.url), "utf8");
  const sectionCount = [...notes.matchAll(/^## v\S+$/gmu)].length;
  assert.ok(sectionCount > 0);
  assert.equal(notes.split(freewareNotice).length - 1, 1);
  assert.equal(notes.split(unsignedReleaseNotice).length - 1, 1);
  assert.equal(getGeneralReleaseNotices(notes), requiredNotices);
});

test("workflow restoration copies archived YAML without overwriting existing files", async () => {
  const root = await mkdtemp(path.join(os.tmpdir(), "voltura-air-actions-"));
  const sourceDirectory = path.join(root, "legacy");
  const targetDirectory = path.join(root, "workflows");
  try {
    await mkdir(sourceDirectory);
    await writeFile(path.join(sourceDirectory, "release.yml"), "name: Release\n", "utf8");
    await writeFile(path.join(sourceDirectory, "quality.yaml"), "name: Quality\n", "utf8");
    assert.deepEqual(await restoreGithubActions({ sourceDirectory, targetDirectory }), [
      "quality.yaml",
      "release.yml",
    ]);
    assert.equal(
      await readFile(path.join(targetDirectory, "release.yml"), "utf8"),
      "name: Release\n",
    );
    await assert.rejects(
      () => restoreGithubActions({ sourceDirectory, targetDirectory }),
      /Refusing to overwrite/u,
    );
  } finally {
    await rm(root, { recursive: true, force: true });
  }
});

test("workflow restoration rolls back files copied before a later failure", async () => {
  const root = await mkdtemp(path.join(os.tmpdir(), "voltura-air-actions-rollback-"));
  const sourceDirectory = path.join(root, "legacy");
  const targetDirectory = path.join(root, "workflows");
  try {
    await mkdir(sourceDirectory);
    await writeFile(path.join(sourceDirectory, "quality.yaml"), "name: Quality\n", "utf8");
    await writeFile(path.join(sourceDirectory, "release.yml"), "name: Release\n", "utf8");
    let copies = 0;
    await assert.rejects(
      () =>
        restoreGithubActions({
          sourceDirectory,
          targetDirectory,
          copy: async (source, target) => {
            copies += 1;
            if (copies === 2) throw new Error("injected copy failure");
            await copyFile(source, target);
          },
        }),
      /injected copy failure/u,
    );
    assert.deepEqual(await readdir(targetDirectory), []);
  } finally {
    await rm(root, { recursive: true, force: true });
  }
});

test("workflow restoration rolls back a published file when post-publication inspection fails", async () => {
  const root = await mkdtemp(path.join(os.tmpdir(), "voltura-air-actions-publish-inspection-"));
  const sourceDirectory = path.join(root, "legacy");
  const targetDirectory = path.join(root, "workflows");
  try {
    await mkdir(sourceDirectory);
    await writeFile(path.join(sourceDirectory, "quality.yaml"), "name: Quality\n", "utf8");
    let inspections = 0;
    await assert.rejects(
      () =>
        restoreGithubActions({
          sourceDirectory,
          targetDirectory,
          inspect: async (target) => {
            inspections += 1;
            if (inspections === 2) {
              throw new Error("injected published-file inspection failure");
            }
            return stat(target);
          },
        }),
      /injected published-file inspection failure/u,
    );
    assert.deepEqual(await readdir(targetDirectory), []);
  } finally {
    await rm(root, { recursive: true, force: true });
  }
});

test("workflow restoration rolls back a hard link published before its call reports failure", async () => {
  const root = await mkdtemp(path.join(os.tmpdir(), "voltura-air-actions-publish-ambiguous-"));
  const sourceDirectory = path.join(root, "legacy");
  const targetDirectory = path.join(root, "workflows");
  try {
    await mkdir(sourceDirectory);
    await writeFile(path.join(sourceDirectory, "quality.yaml"), "name: Quality\n", "utf8");
    await assert.rejects(
      () =>
        restoreGithubActions({
          sourceDirectory,
          targetDirectory,
          publish: async (source, target) => {
            await link(source, target);
            throw new Error("injected post-publish failure");
          },
        }),
      /injected post-publish failure/u,
    );
    assert.deepEqual(await readdir(targetDirectory), []);
  } finally {
    await rm(root, { recursive: true, force: true });
  }
});

test("workflow restoration does not overwrite a workflow created after its preflight", async () => {
  const root = await mkdtemp(path.join(os.tmpdir(), "voltura-air-actions-race-"));
  const sourceDirectory = path.join(root, "legacy");
  const targetDirectory = path.join(root, "workflows");
  try {
    await mkdir(sourceDirectory);
    await writeFile(path.join(sourceDirectory, "quality.yaml"), "name: Archived\n", "utf8");
    let reads = 0;
    await assert.rejects(
      () =>
        restoreGithubActions({
          sourceDirectory,
          targetDirectory,
          readDirectory: async (directory) => {
            reads += 1;
            if (reads === 2) {
              await writeFile(
                path.join(targetDirectory, "quality.yaml"),
                "name: Concurrent\n",
                "utf8",
              );
              return [];
            }
            return readdir(directory);
          },
        }),
      /EEXIST/u,
    );
    assert.equal(
      await readFile(path.join(targetDirectory, "quality.yaml"), "utf8"),
      "name: Concurrent\n",
    );
  } finally {
    await rm(root, { recursive: true, force: true });
  }
});

test("workflow restoration rollback preserves a workflow changed after it was copied", async () => {
  const root = await mkdtemp(path.join(os.tmpdir(), "voltura-air-actions-rollback-race-"));
  const sourceDirectory = path.join(root, "legacy");
  const targetDirectory = path.join(root, "workflows");
  try {
    await mkdir(sourceDirectory);
    await writeFile(path.join(sourceDirectory, "quality.yaml"), "name: Archived\n", "utf8");
    await writeFile(path.join(sourceDirectory, "release.yml"), "name: Release\n", "utf8");
    let copies = 0;
    await assert.rejects(
      () =>
        restoreGithubActions({
          sourceDirectory,
          targetDirectory,
          copy: async (source, target) => {
            copies += 1;
            if (copies === 2) {
              await writeFile(
                path.join(targetDirectory, "quality.yaml"),
                "name: Concurrent\n",
                "utf8",
              );
              throw new Error("injected copy failure");
            }
            await copyFile(source, target);
          },
        }),
      /rollback both failed/u,
    );
    assert.equal(
      await readFile(path.join(targetDirectory, "quality.yaml"), "utf8"),
      "name: Concurrent\n",
    );
  } finally {
    await rm(root, { recursive: true, force: true });
  }
});

test("workflow restoration rollback preserves an identical concurrent replacement", async () => {
  const root = await mkdtemp(
    path.join(os.tmpdir(), "voltura-air-actions-rollback-identical-race-"),
  );
  const sourceDirectory = path.join(root, "legacy");
  const targetDirectory = path.join(root, "workflows");
  try {
    await mkdir(sourceDirectory);
    await writeFile(path.join(sourceDirectory, "quality.yaml"), "name: Archived\n", "utf8");
    await writeFile(path.join(sourceDirectory, "release.yml"), "name: Release\n", "utf8");
    let copies = 0;
    await assert.rejects(
      () =>
        restoreGithubActions({
          sourceDirectory,
          targetDirectory,
          copy: async (source, target) => {
            copies += 1;
            if (copies === 2) {
              await rm(path.join(targetDirectory, "quality.yaml"));
              await writeFile(
                path.join(targetDirectory, "quality.yaml"),
                "name: Archived\n",
                "utf8",
              );
              throw new Error("injected copy failure");
            }
            await copyFile(source, target);
          },
        }),
      /rollback both failed/u,
    );
    assert.equal(
      await readFile(path.join(targetDirectory, "quality.yaml"), "utf8"),
      "name: Archived\n",
    );
  } finally {
    await rm(root, { recursive: true, force: true });
  }
});

test("workflow restoration rollback cannot delete a replacement created as rollback begins", async () => {
  const root = await mkdtemp(path.join(os.tmpdir(), "voltura-air-actions-rollback-atomic-race-"));
  const sourceDirectory = path.join(root, "legacy");
  const targetDirectory = path.join(root, "workflows");
  try {
    await mkdir(sourceDirectory);
    await writeFile(path.join(sourceDirectory, "quality.yaml"), "name: Archived\n", "utf8");
    await writeFile(path.join(sourceDirectory, "release.yml"), "name: Release\n", "utf8");
    let copies = 0;
    let replacedDuringRollback = false;
    await assert.rejects(
      () =>
        restoreGithubActions({
          sourceDirectory,
          targetDirectory,
          copy: async (source, target) => {
            copies += 1;
            if (copies === 2) {
              throw new Error("injected copy failure");
            }
            await copyFile(source, target);
          },
          move: async (source, target) => {
            await rename(source, target);
            if (!replacedDuringRollback && source === path.join(targetDirectory, "quality.yaml")) {
              replacedDuringRollback = true;
              await writeFile(source, "name: Concurrent\n", "utf8");
            }
          },
        }),
      /injected copy failure/u,
    );
    assert.equal(
      await readFile(path.join(targetDirectory, "quality.yaml"), "utf8"),
      "name: Concurrent\n",
    );
  } finally {
    await rm(root, { recursive: true, force: true });
  }
});

test("workflow restoration reconciles a quarantine move that succeeds before reporting failure", async () => {
  const root = await mkdtemp(path.join(os.tmpdir(), "voltura-air-actions-rollback-move-failure-"));
  const sourceDirectory = path.join(root, "legacy");
  const targetDirectory = path.join(root, "workflows");
  try {
    await mkdir(sourceDirectory);
    await writeFile(path.join(sourceDirectory, "quality.yaml"), "name: Archived\n", "utf8");
    await writeFile(path.join(sourceDirectory, "release.yml"), "name: Release\n", "utf8");
    let copies = 0;
    let injected = false;
    await assert.rejects(
      () =>
        restoreGithubActions({
          sourceDirectory,
          targetDirectory,
          copy: async (source, target) => {
            copies += 1;
            if (copies === 2) {
              throw new Error("injected copy failure");
            }
            await copyFile(source, target);
          },
          move: async (source, target) => {
            await rename(source, target);
            if (!injected && source === path.join(targetDirectory, "quality.yaml")) {
              injected = true;
              throw new Error("injected post-move failure");
            }
          },
        }),
      /injected copy failure/u,
    );
    await assert.rejects(() => readFile(path.join(targetDirectory, "quality.yaml")), /ENOENT/u);
  } finally {
    await rm(root, { recursive: true, force: true });
  }
});

test("workflow restoration accepts a quarantine removal that succeeds before reporting failure", async () => {
  const root = await mkdtemp(
    path.join(os.tmpdir(), "voltura-air-actions-rollback-remove-failure-"),
  );
  const sourceDirectory = path.join(root, "legacy");
  const targetDirectory = path.join(root, "workflows");
  try {
    await mkdir(sourceDirectory);
    await writeFile(path.join(sourceDirectory, "quality.yaml"), "name: Archived\n", "utf8");
    await writeFile(path.join(sourceDirectory, "release.yml"), "name: Release\n", "utf8");
    let copies = 0;
    let injected = false;
    await assert.rejects(
      () =>
        restoreGithubActions({
          sourceDirectory,
          targetDirectory,
          copy: async (source, target) => {
            copies += 1;
            if (copies === 2) {
              throw new Error("injected copy failure");
            }
            await copyFile(source, target);
          },
          remove: async (target, options) => {
            await rm(target, options);
            if (
              !injected &&
              target.includes("quality.yaml.voltura-owned-") &&
              !target.includes(".tmp.")
            ) {
              injected = true;
              throw new Error("injected post-remove failure");
            }
          },
        }),
      /injected copy failure/u,
    );
    await assert.rejects(() => readFile(path.join(targetDirectory, "quality.yaml")), /ENOENT/u);
  } finally {
    await rm(root, { recursive: true, force: true });
  }
});

test("release body and draft audit require the exact local artifact set", () => {
  const body = buildReleaseBody({
    notes: "- A visible fix.",
    notices: requiredNotices,
    version: "0.7.4",
    latestTag: "v0.7.3",
    repository: "voltura/voltura-air",
  });
  assert.match(body, /VolturaAir-Setup-0\.7\.4-win-x64-full\.exe/u);
  assert.match(body, /compare\/v0\.7\.3\.\.\.v0\.7\.4/u);
  assert.equal(body.split(releaseNotesStartMarker).length - 1, 1);
  assert.equal(body.split(releaseNotesEndMarker).length - 1, 1);
  assert.ok(body.indexOf(releaseNotesStartMarker) < body.indexOf("- A visible fix."));
  assert.equal(extractUserFacingReleaseNotes(extractMarkedReleaseNotes(body)), "- A visible fix.");
  assert.ok(body.indexOf(releaseNotesEndMarker) < body.indexOf("## Downloads"));

  const names = ["portable.zip", "small.exe", "full.exe"];
  const release = {
    name: "Voltura Air v0.7.4",
    body,
    isDraft: true,
    targetCommitish: "abc123",
    assets: names.map((name) => ({ name, size: 10, digest: "sha256:valid" })),
  };
  const expectedArtifacts = names.map((name) => ({ name, size: 10, sha256: "valid" }));
  const expectedMetadata = {
    releaseTitle: release.name,
    releaseBodySha256: createHash("sha256").update(body, "utf8").digest("hex"),
  };
  assert.doesNotThrow(() =>
    auditDraft(release, "abc123", names, expectedArtifacts, expectedMetadata),
  );
  assert.throws(
    () =>
      auditDraft(
        { ...release, body: "Changed" },
        "abc123",
        names,
        expectedArtifacts,
        expectedMetadata,
      ),
    /title or body/u,
  );
  assert.throws(
    () => auditDraft({ ...release, targetCommitish: "other" }, "abc123", names),
    /target commit/u,
  );
  assert.throws(
    () => auditDraft({ ...release, assets: release.assets.slice(1) }, "abc123", names),
    /expected set/u,
  );
  assert.throws(
    () =>
      auditDraft(
        {
          ...release,
          assets: release.assets.map((asset, index) =>
            index === 0 ? { ...asset, digest: "sha256:substituted" } : asset,
          ),
        },
        "abc123",
        names,
        expectedArtifacts,
      ),
    /packaged checkpoint/u,
  );
});
