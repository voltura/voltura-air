import assert from "node:assert/strict";
import { mkdtemp, mkdir, readFile, rm, writeFile } from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import test from "node:test";

import { getNextReleaseVersion } from "../../scripts/bump-release.mjs";
import { auditDraft, buildReleaseBody, getRelease, publishReleaseIfRequested } from "../../scripts/release-publish.mjs";
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
  unsignedReleaseNotice
} from "../../scripts/release-tools.mjs";

const requiredNotices = `${freewareNotice}\n\n${unsignedReleaseNotice}`;
const localReleaseSource = await readFile(new URL("../../scripts/release-publish.mjs", import.meta.url), "utf8");
import { restoreGithubActions } from "../../scripts/restore-github-actions.mjs";
import { resolveSynchronizedRelease } from "../../scripts/sync-release-notes.mjs";

test("release commands accept at most one explicit version", () => {
  assert.deepEqual(parseReleaseArguments([]), { version: null });
  assert.deepEqual(parseReleaseArguments(["0.8.0"]), { version: "0.8.0" });
  assert.throws(() => parseReleaseArguments(["0.8.0", "extra"]), /Usage/u);
  assert.throws(() => parseReleaseArguments(["latest"]), /semantic versioning/u);
});

test("local draft completion does not run publication or tag commands", () => {
  const commands = [];
  publishReleaseIfRequested({
    publishLatest: false,
    targetTag: "v0.8.0",
    repository: "voltura/voltura-air",
    expectedCommit: "abc123"
  }, (command, args) => commands.push([command, args]));

  assert.deepEqual(commands, []);
});

test("local release does not fetch tags into the checkout", () => {
  assert.doesNotMatch(localReleaseSource, /git[^\n]+fetch[^\n]+--tags/u);
  assert.doesNotMatch(localReleaseSource, /refs\/tags\/\$\{targetTag\}:refs\/tags/u);
});

test("local latest completion publishes and verifies through GitHub without fetching a tag", () => {
  const commands = [];
  publishReleaseIfRequested({
    publishLatest: true,
    targetTag: "v0.8.0",
    repository: "voltura/voltura-air",
    expectedCommit: "abc123"
  }, (command, args) => {
    commands.push([command, args]);
    return command === "gh" && args[0] === "api"
      ? JSON.stringify({ tag_name: "v0.8.0", draft: false, target_commitish: "abc123" })
      : "";
  });

  assert.deepEqual(commands, [
    ["gh", ["release", "edit", "v0.8.0", "--repo", "voltura/voltura-air", "--draft=false", "--latest"]],
    ["gh", ["api", "repos/voltura/voltura-air/releases/latest"]]
  ]);
  assert.equal(commands.some(([command]) => command === "git"), false);
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
    version: "0.8.0-beta.2"
  });
  assert.throws(
    () => resolveReleaseVersion({
      currentVersion: "0.7.3",
      latestReleasedVersion: "0.8.0-beta.2",
      explicitVersion: "0.8.0-beta.1",
      currentTagExists: false,
      currentReleaseIsDraft: false,
      getNextVersion: getNextReleaseVersion
    }),
    /must be newer/u
  );
  assert.throws(
    () => resolveReleaseVersion({
      currentVersion: "0.7.3",
      latestReleasedVersion: "0.8.0-beta.2",
      explicitVersion: null,
      currentTagExists: true,
      currentReleaseIsDraft: false,
      getNextVersion: getNextReleaseVersion
    }),
    /Resolved version '0\.7\.4' must be newer/u
  );
  assert.throws(
    () => resolveReleaseVersion({
      currentVersion: "0.8.0-beta.1",
      latestReleasedVersion: "0.8.0-beta.2",
      explicitVersion: null,
      currentTagExists: true,
      currentReleaseIsDraft: true,
      getNextVersion: getNextReleaseVersion
    }),
    /Resolved version '0\.8\.0-beta\.1' must be newer/u
  );
  assert.throws(() => resolveLatestPublishedRelease([newerDraft]), /published release version/u);
});

test("release lookup propagates GitHub failures instead of reporting absence", () => {
  const failure = new Error("GitHub request failed");
  assert.throws(() => getRelease("v0.8.0", "voltura/voltura-air", () => {
    throw failure;
  }), (error) => error === failure);
  assert.deepEqual(getRelease("v0.8.0", "voltura/voltura-air", () => JSON.stringify({
    tagName: "v0.8.0",
    isDraft: true
  })), { tagName: "v0.8.0", isDraft: true });
});

test("release-note synchronization selects Latest by default or one explicit version", () => {
  assert.deepEqual(parseSyncReleaseArguments([]), { version: null });
  assert.deepEqual(parseSyncReleaseArguments(["0.8.0"]), { version: "0.8.0" });
  assert.deepEqual(parseSyncReleaseArguments(["0.8.0-beta.1"]), { version: "0.8.0-beta.1" });
  assert.throws(() => parseSyncReleaseArguments(["v0.8.0"]), /semantic versioning/u);
  assert.throws(() => parseSyncReleaseArguments(["0.8.0", "extra"]), /Usage/u);
});

test("release-note synchronization accepts only the intended published release", () => {
  const stable = { tagName: "v0.8.0", isDraft: false, isPrerelease: false, body: "notes", url: "stable" };
  assert.equal(resolveSynchronizedRelease(stable).version, "0.8.0");
  assert.equal(resolveSynchronizedRelease({ ...stable, tagName: "v0.9.0-beta.1", isPrerelease: true }, "0.9.0-beta.1").version, "0.9.0-beta.1");
  assert.throws(() => resolveSynchronizedRelease({ ...stable, isDraft: true }), /published release/u);
  assert.throws(() => resolveSynchronizedRelease({ ...stable, tagName: "v0.9.0-beta.1", isPrerelease: true }), /GitHub Latest/u);
  assert.throws(() => resolveSynchronizedRelease(stable, "0.8.1"), /instead of the requested/u);
});

test("release resolution bumps published versions and resumes pending drafts", () => {
  const common = {
    latestReleasedVersion: "0.7.3",
    explicitVersion: null,
    getNextVersion: getNextReleaseVersion
  };
  assert.equal(resolveReleaseVersion({
    ...common,
    currentVersion: "0.7.3",
    currentTagExists: true,
    currentReleaseIsDraft: false
  }), "0.7.4");
  assert.equal(resolveReleaseVersion({
    ...common,
    currentVersion: "0.7.4",
    currentTagExists: true,
    currentReleaseIsDraft: true
  }), "0.7.4");
  assert.equal(resolveReleaseVersion({
    ...common,
    currentVersion: "0.7.3",
    explicitVersion: "0.8.0",
    currentTagExists: true,
    currentReleaseIsDraft: false
  }), "0.8.0");
  assert.throws(() => resolveReleaseVersion({
    ...common,
    currentVersion: "0.7.3",
    explicitVersion: "0.7.3",
    currentTagExists: true,
    currentReleaseIsDraft: false
  }), /must be newer/u);
});

test("release notes require one non-placeholder section and one shared notices section", () => {
  const notes = `## v0.7.4\n\n- New visible behavior.\n\n## v0.7.3\n\n- Previous release.\n\n## General notices\n\n${requiredNotices}\n`;
  assert.equal(getReleaseNotesSection(notes, "0.7.4"), "- New visible behavior.");
  assert.equal(getGeneralReleaseNotices(notes), requiredNotices);
  assert.throws(() => getReleaseNotesSection("## v0.7.4\n\n<!-- Add notes. -->\n", "0.7.4"), /user-facing changes/u);
  assert.throws(
    () => getReleaseNotesSection(`## v0.7.4\n\n- Changed.\n\n${requiredNotices}\n\n## General notices\n\n${requiredNotices}\n`, "0.7.4"),
    /must not repeat/u
  );
  assert.throws(() => getGeneralReleaseNotices("## v0.7.4\n\n- Changed.\n"), /General notices/u);
  assert.throws(() => getReleaseNotesSection("## v0.7.4\n- One\n## v0.7.4\n- Two\n", "0.7.4"), /exactly one/u);
});

test("marked release-note extraction requires safe boundaries and canonical notices", () => {
  const content = `## Highlights\n\n- Edited on GitHub.\n\n<!-- Keep this editorial note. -->\n\n${requiredNotices}`;
  const body = `## What's new\n\n${releaseNotesStartMarker}\n${content}\n${releaseNotesEndMarker}\n\n## Downloads\n\n- Installer`;
  assert.equal(extractMarkedReleaseNotes(body), content);
  assert.throws(() => extractMarkedReleaseNotes(content), /marker pair/u);
  assert.throws(
    () => extractMarkedReleaseNotes(`${releaseNotesEndMarker}\n${content}\n${releaseNotesStartMarker}`),
    /reversed/u
  );
  assert.throws(
    () => extractMarkedReleaseNotes(`${releaseNotesStartMarker}\n- Changed.\n${releaseNotesEndMarker}`),
    /freeware notice/u
  );
  assert.throws(
    () => extractMarkedReleaseNotes(`${releaseNotesStartMarker}\n## v0.9.0\n\n${content}\n${releaseNotesEndMarker}`),
    /version section heading/u
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
    assert.deepEqual(await restoreGithubActions({ sourceDirectory, targetDirectory }), ["quality.yaml", "release.yml"]);
    assert.equal(await readFile(path.join(targetDirectory, "release.yml"), "utf8"), "name: Release\n");
    await assert.rejects(() => restoreGithubActions({ sourceDirectory, targetDirectory }), /Refusing to overwrite/u);
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
    repository: "voltura/voltura-air"
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
    isDraft: true,
    targetCommitish: "abc123",
    assets: names.map((name) => ({ name, size: 10, digest: "sha256:valid" }))
  };
  assert.doesNotThrow(() => auditDraft(release, "abc123", names));
  assert.throws(() => auditDraft({ ...release, targetCommitish: "other" }, "abc123", names), /target commit/u);
  assert.throws(() => auditDraft({ ...release, assets: release.assets.slice(1) }, "abc123", names), /expected set/u);
});
