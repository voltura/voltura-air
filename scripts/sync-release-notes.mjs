import { readFile, writeFile } from "node:fs/promises";
import path from "node:path";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";

import {
  extractMarkedReleaseNotes,
  parseSemver,
  parseSyncReleaseArguments,
  replaceReleaseNotesSection
} from "./release-tools.mjs";

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const notesPath = path.join(repositoryRoot, "docs", "release-notes.md");

function run(command, args) {
  const result = spawnSync(command, args, {
    cwd: repositoryRoot,
    encoding: "utf8",
    stdio: ["ignore", "pipe", "pipe"],
    windowsHide: true
  });
  if (result.error) {
    throw result.error;
  }
  if (result.status !== 0) {
    const details = (result.stderr || result.stdout || "").trim();
    throw new Error(`${command} ${args.join(" ")} failed with exit code ${result.status}.${details ? ` ${details}` : ""}`);
  }
  return (result.stdout ?? "").trim();
}

function normalizeRelease(raw) {
  const release = typeof raw === "string" ? JSON.parse(raw) : raw;
  return {
    tag: release.tagName ?? release.tag_name,
    draft: release.isDraft ?? release.draft,
    prerelease: release.isPrerelease ?? release.prerelease,
    body: release.body ?? "",
    url: release.url ?? release.html_url
  };
}

export function resolveSynchronizedRelease(raw, explicitVersion = null) {
  const release = normalizeRelease(raw);
  if (release.draft || (!explicitVersion && release.prerelease)) {
    throw new Error("Release-note synchronization requires a published release; the default selection must be GitHub Latest.");
  }
  if (!release.tag?.startsWith("v")) {
    throw new Error("The selected GitHub release does not have a supported v-prefixed tag.");
  }
  const version = release.tag.slice(1);
  parseSemver(version);
  if (explicitVersion && version !== explicitVersion) {
    throw new Error(`GitHub returned '${release.tag}' instead of the requested release 'v${explicitVersion}'.`);
  }
  return { ...release, version };
}

export async function syncReleaseNotes(args = process.argv.slice(2)) {
  const { version: explicitVersion } = parseSyncReleaseArguments(args);
  if (run("git", ["status", "--porcelain=v1", "--untracked-files=all"])) {
    throw new Error("Release-note synchronization requires a clean Git working tree.");
  }
  run("gh", ["auth", "status", "--hostname", "github.com"]);
  const repository = run("gh", ["repo", "view", "--json", "nameWithOwner", "--jq", ".nameWithOwner"]);

  const rawRelease = explicitVersion
    ? run("gh", ["release", "view", `v${explicitVersion}`, "--repo", repository, "--json", "tagName,isDraft,isPrerelease,body,url"])
    : run("gh", ["api", `repos/${repository}/releases/latest`]);
  const release = resolveSynchronizedRelease(rawRelease, explicitVersion);

  const synchronizedContent = extractMarkedReleaseNotes(release.body);
  const currentNotes = await readFile(notesPath, "utf8");
  const updatedNotes = replaceReleaseNotesSection(currentNotes, release.version, synchronizedContent);
  if (updatedNotes === currentNotes) {
    console.log(`Release notes for ${release.tag} already match ${release.url}.`);
    return false;
  }
  await writeFile(notesPath, updatedNotes, "utf8");
  console.log(`Synchronized ${release.tag} from ${release.url} into docs/release-notes.md.`);
  return true;
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  syncReleaseNotes().catch((error) => {
    console.error(error.message);
    process.exitCode = 1;
  });
}
