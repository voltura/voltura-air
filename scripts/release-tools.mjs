const semverPattern = /^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$/u;

export const freewareNotice = "Voltura Air is free software from Voltura AB. If it helps you, optional support is available through [Ko-fi](https://ko-fi.com/voltura) or [PayPal](https://www.paypal.me/voltura).";
export const unsignedReleaseNotice = "Release binaries are not code-signed. Windows may show an unknown-publisher or Microsoft Defender SmartScreen warning. Download release files only from the official Voltura Air website or GitHub release page.";
export const releaseNotesStartMarker = "<!-- voltura-air:release-notes:start -->";
export const releaseNotesEndMarker = "<!-- voltura-air:release-notes:end -->";

const versionHeadingSource = "##[ \\t]+v(?:0|[1-9]\\d*)\\.(?:0|[1-9]\\d*)\\.(?:0|[1-9]\\d*)(?:-[0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*)?(?:\\+[0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*)?[ \\t]*";
const sectionHeadingSource = "##[ \\t]+\\S.*?[ \\t]*";
const generalNoticesHeading = "General notices";
const releaseSectionEndHeadingSource = `(?:${versionHeadingSource}|##[ \\t]+${generalNoticesHeading}[ \\t]*)`;

export function parseReleaseArguments(args) {
  if (!Array.isArray(args) || !args.every((arg) => typeof arg === "string")) {
    throw new TypeError("Release arguments must be strings.");
  }

  const publishLatest = args.at(-1) === "latest";
  const versionArgs = publishLatest ? args.slice(0, -1) : args;
  if (versionArgs.length > 1 || (versionArgs.length === 1 && versionArgs[0] === "latest")) {
    throw new Error("Usage: npm run release:local -- [version] [latest]");
  }

  const version = versionArgs[0] ?? null;
  if (version !== null) {
    parseSemver(version);
  }

  return { version, publishLatest };
}

export function parseSyncReleaseArguments(args) {
  if (!Array.isArray(args) || args.length > 1 || !args.every((arg) => typeof arg === "string")) {
    throw new Error("Usage: npm run release:sync-release-notes -- [version]");
  }
  const version = args[0] ?? null;
  if (version !== null) {
    parseSemver(version);
  }
  return { version };
}

export function parseSemver(version) {
  const match = semverPattern.exec(version);
  if (!match) {
    throw new Error(`Version '${version}' is not supported semantic versioning.`);
  }

  return {
    version,
    core: match.slice(1, 4).map(Number),
    prerelease: match[4]?.split(".") ?? []
  };
}

function comparePrereleaseIdentifier(left, right) {
  const leftNumeric = /^\d+$/u.test(left);
  const rightNumeric = /^\d+$/u.test(right);
  if (leftNumeric && rightNumeric) {
    return Number(BigInt(left) - BigInt(right));
  }
  if (leftNumeric !== rightNumeric) {
    return leftNumeric ? -1 : 1;
  }
  return left === right ? 0 : left < right ? -1 : 1;
}

export function compareSemver(leftVersion, rightVersion) {
  const left = parseSemver(leftVersion);
  const right = parseSemver(rightVersion);
  for (let index = 0; index < left.core.length; index += 1) {
    if (left.core[index] !== right.core[index]) {
      return left.core[index] < right.core[index] ? -1 : 1;
    }
  }

  if (left.prerelease.length === 0 || right.prerelease.length === 0) {
    return left.prerelease.length === right.prerelease.length ? 0 : left.prerelease.length === 0 ? 1 : -1;
  }

  const count = Math.max(left.prerelease.length, right.prerelease.length);
  for (let index = 0; index < count; index += 1) {
    if (left.prerelease[index] === undefined || right.prerelease[index] === undefined) {
      return left.prerelease[index] === undefined ? -1 : 1;
    }
    const comparison = comparePrereleaseIdentifier(left.prerelease[index], right.prerelease[index]);
    if (comparison !== 0) {
      return comparison < 0 ? -1 : 1;
    }
  }
  return 0;
}

export function resolveLatestPublishedRelease(releases) {
  if (!Array.isArray(releases)) {
    throw new TypeError("GitHub releases must be an array.");
  }

  let latest = null;
  for (const release of releases) {
    if (release?.draft) {
      continue;
    }
    if (typeof release?.tag_name !== "string" || !release.tag_name.startsWith("v")) {
      throw new Error("A published GitHub release does not have a supported v-prefixed tag.");
    }
    const version = release.tag_name.slice(1);
    parseSemver(version);
    if (latest === null || compareSemver(version, latest.version) > 0) {
      latest = { release, tag: release.tag_name, version };
    }
  }

  if (latest === null) {
    throw new Error("Could not resolve a published release version.");
  }
  return latest;
}

export function resolveReleaseVersion({
  currentVersion,
  latestReleasedVersion,
  explicitVersion,
  currentTagExists,
  currentReleaseIsDraft,
  getNextVersion
}) {
  parseSemver(currentVersion);
  parseSemver(latestReleasedVersion);

  let targetVersion;
  if (explicitVersion) {
    parseSemver(explicitVersion);
    targetVersion = explicitVersion;
  } else if (currentReleaseIsDraft) {
    targetVersion = currentVersion;
  } else if (currentTagExists) {
    targetVersion = getNextVersion(currentVersion);
  } else {
    targetVersion = currentVersion;
  }

  if (compareSemver(targetVersion, latestReleasedVersion) <= 0) {
    const label = explicitVersion ? "Explicit version" : "Resolved version";
    throw new Error(`${label} '${targetVersion}' must be newer than '${latestReleasedVersion}'.`);
  }
  return targetVersion;
}

function escapeRegex(value) {
  return value.replace(/[.*+?^${}()|[\]\\]/gu, "\\$&");
}

function countOccurrences(text, value) {
  return text.split(value).length - 1;
}

export function validateReleaseNotesContent(content, { preserveComments = false } = {}) {
  const trimmedContent = content.trim();
  const releaseContent = trimmedContent.replace(/<!--[\s\S]*?-->/gu, "").trim();
  if (countOccurrences(releaseContent, freewareNotice) !== 1) {
    throw new Error("Release notes must contain exactly one canonical freeware notice.");
  }
  if (countOccurrences(releaseContent, unsignedReleaseNotice) !== 1) {
    throw new Error("Release notes must contain exactly one canonical unsigned-release notice.");
  }
  const publishableContent = releaseContent
    .replace(freewareNotice, "")
    .replace(unsignedReleaseNotice, "")
    .trim();
  if (!publishableContent) {
    throw new Error("Release notes contain no user-facing changes.");
  }
  return preserveComments ? trimmedContent : releaseContent;
}

export function extractUserFacingReleaseNotes(content) {
  const releaseContent = validateReleaseNotesContent(content, { preserveComments: true });
  return releaseContent
    .replace(freewareNotice, "")
    .replace(unsignedReleaseNotice, "")
    .trim();
}

function validateUserFacingReleaseNotes(content, { preserveComments = false } = {}) {
  const trimmedContent = content.trim();
  const releaseContent = trimmedContent.replace(/<!--[\s\S]*?-->/gu, "").trim();
  if (releaseContent.includes(freewareNotice) || releaseContent.includes(unsignedReleaseNotice)) {
    throw new Error("Version sections must not repeat the notices from '## General notices'.");
  }
  if (!releaseContent) {
    throw new Error("Release notes contain no user-facing changes.");
  }
  return preserveComments ? trimmedContent : releaseContent;
}

export function extractMarkedReleaseNotes(body) {
  if (countOccurrences(body, releaseNotesStartMarker) !== 1 || countOccurrences(body, releaseNotesEndMarker) !== 1) {
    throw new Error("Published release notes must contain exactly one synchronization marker pair.");
  }
  const start = body.indexOf(releaseNotesStartMarker) + releaseNotesStartMarker.length;
  const end = body.indexOf(releaseNotesEndMarker);
  if (end < start) {
    throw new Error("Published release-note synchronization markers are reversed.");
  }
  const content = body.slice(start, end).trim();
  if (new RegExp(`^${versionHeadingSource}\\r?$`, "mu").test(content)) {
    throw new Error("Synchronized release notes cannot contain a version section heading.");
  }
  return validateReleaseNotesContent(content, { preserveComments: true });
}

function findVersionSection(text, version) {
  parseSemver(version);
  const heading = new RegExp(`^##[ \\t]+v${escapeRegex(version)}[ \\t]*(?=\\r?$)`, "gmu");
  const matches = [...text.matchAll(heading)];
  if (matches.length !== 1) {
    throw new Error(`Expected exactly one '## v${version}' heading in docs/release-notes.md; found ${matches.length}.`);
  }
  const contentStart = matches[0].index + matches[0][0].length;
  const nextHeading = new RegExp(`^${releaseSectionEndHeadingSource}\\r?$`, "gmu");
  nextHeading.lastIndex = contentStart;
  const next = nextHeading.exec(text);
  return { heading: matches[0], contentStart, contentEnd: next?.index ?? text.length };
}

export function getReleaseNotesSection(text, version) {
  const section = findVersionSection(text, version);
  return validateUserFacingReleaseNotes(text.slice(section.contentStart, section.contentEnd).trim());
}

export function getGeneralReleaseNotices(text) {
  const heading = new RegExp(`^##[ \\t]+${escapeRegex(generalNoticesHeading)}[ \\t]*(?=\\r?$)`, "gmu");
  const matches = [...text.matchAll(heading)];
  if (matches.length !== 1) {
    throw new Error(`Expected exactly one '## ${generalNoticesHeading}' heading in docs/release-notes.md; found ${matches.length}.`);
  }
  const contentStart = matches[0].index + matches[0][0].length;
  const nextHeading = new RegExp(`^${sectionHeadingSource}\\r?$`, "gmu");
  nextHeading.lastIndex = contentStart;
  const next = nextHeading.exec(text);
  const content = text.slice(contentStart, next?.index ?? text.length).trim();
  const releaseContent = content.replace(/<!--[\s\S]*?-->/gu, "").trim();
  if (countOccurrences(releaseContent, freewareNotice) !== 1
    || countOccurrences(releaseContent, unsignedReleaseNotice) !== 1) {
    throw new Error("General notices must contain exactly one copy of each canonical notice.");
  }
  const remainingContent = releaseContent
    .replace(freewareNotice, "")
    .replace(unsignedReleaseNotice, "")
    .trim();
  if (remainingContent) {
    throw new Error("The General notices section must contain only the canonical notices.");
  }
  return releaseContent;
}

export function replaceReleaseNotesSection(text, version, content) {
  const releaseContent = validateUserFacingReleaseNotes(content, { preserveComments: true });
  const section = findVersionSection(text, version);
  const lineEnding = text.includes("\r\n") ? "\r\n" : "\n";
  const normalizedContent = releaseContent.replace(/\r?\n/gu, lineEnding);
  const headingEnd = section.heading.index + section.heading[0].length;
  const suffix = text.slice(section.contentEnd).replace(/^(?:\r?\n)*/u, "");
  const replacement = `${text.slice(0, headingEnd)}${lineEnding}${lineEnding}${normalizedContent}${lineEnding}`;
  return suffix ? `${replacement}${lineEnding}${suffix}` : replacement;
}
