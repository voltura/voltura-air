function versionParts(value) {
  const match = /^v?(\d+)\.(\d+)(?:\.(\d+))?(?:\.\d+)?$/u.exec(value.trim());
  return match ? [Number(match[1]), Number(match[2]), Number(match[3] ?? 0)] : null;
}

export function supportsToolVersion(actual, expected, comparison = "exact") {
  const parts = versionParts(actual);
  if (!parts) return false;
  const [major, minor, patch] = parts;
  const [expectedMajor, expectedMinor, expectedPatch = 0] = expected;
  const atLeast =
    major > expectedMajor ||
    (major === expectedMajor &&
      (minor > expectedMinor || (minor === expectedMinor && patch >= expectedPatch)));
  switch (comparison) {
    case "major-line":
      return major === expectedMajor && atLeast;
    case "major-minor":
      return major === expectedMajor && minor === expectedMinor;
    case "patch-line":
      return major === expectedMajor && minor === expectedMinor && atLeast;
    case "feature-band":
      return (
        major === expectedMajor &&
        minor === expectedMinor &&
        Math.floor(patch / 100) === Math.floor(expectedPatch / 100) &&
        atLeast
      );
    case "minimum":
      return atLeast;
    case "exact":
      return major === expectedMajor && minor === expectedMinor && patch === expectedPatch;
    default:
      return false;
  }
}

export function toolVersionExpectation(expected, comparison = "exact") {
  const [major, minor, patch = 0] = expected;
  switch (comparison) {
    case "major-line":
      return `${expected.join(".")} or a newer ${major}.x release`;
    case "major-minor":
      return `${major}.${minor}.x`;
    case "patch-line":
      return `${expected.join(".")} or a newer ${major}.${minor} patch`;
    case "feature-band":
      return `${major}.${minor}.${Math.floor(patch / 100)}xx feature band (at least ${patch})`;
    case "minimum":
      return `${expected.join(".")} or newer`;
    default:
      return expected.join(".");
  }
}
