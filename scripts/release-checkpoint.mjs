import { createHash } from "node:crypto";
import { mkdir, readFile, rename, stat, writeFile } from "node:fs/promises";
import path from "node:path";

async function sha256(filePath) {
  return createHash("sha256").update(await readFile(filePath)).digest("hex");
}

export function getReleaseAssetPaths(repositoryRoot, version, runtime) {
  const publishRoot = path.join(repositoryRoot, "artifacts", "publish");
  return [
    path.join(publishRoot, `VolturaAir-${version}-${runtime}.zip`),
    path.join(publishRoot, `VolturaAir-Setup-${version}-${runtime}.exe`),
    path.join(publishRoot, `VolturaAir-Setup-${version}-${runtime}-full.exe`)
  ];
}

export function getReleaseCheckpointPath(repositoryRoot, version) {
  return path.join(repositoryRoot, "artifacts", "publish", `release-checkpoint-v${version}.json`);
}

export function validateReleaseCheckpoint(checkpoint, { version, commit, expectedNames }) {
  if (!checkpoint || checkpoint.schema !== 1 || checkpoint.version !== version || checkpoint.commit !== commit) {
    return null;
  }
  if (checkpoint.phase !== "packaged" || !Array.isArray(checkpoint.artifacts)) return null;

  const actualNames = checkpoint.artifacts.map((artifact) => artifact?.name).sort();
  if (actualNames.join("|") !== [...expectedNames].sort().join("|") || checkpoint.artifacts.some((artifact) =>
    !Number.isSafeInteger(artifact?.size) || artifact.size <= 0 || !/^[a-f0-9]{64}$/u.test(artifact?.sha256 ?? ""))) {
    return null;
  }
  return { phase: "packaged", artifacts: checkpoint.artifacts };
}

export async function readReleaseCheckpoint({ repositoryRoot, version, commit, assetPaths, verifyArtifacts = true }) {
  let checkpoint;
  try {
    checkpoint = JSON.parse(await readFile(getReleaseCheckpointPath(repositoryRoot, version), "utf8"));
  } catch (error) {
    if (error.code === "ENOENT" || error instanceof SyntaxError) return null;
    throw error;
  }
  const validated = validateReleaseCheckpoint(checkpoint, {
    version,
    commit,
    expectedNames: assetPaths.map((assetPath) => path.basename(assetPath))
  });
  if (validated?.phase !== "packaged" || !verifyArtifacts) return validated;

  for (const record of validated.artifacts) {
    const assetPath = assetPaths.find((candidate) => path.basename(candidate) === record.name);
    const file = await stat(assetPath).catch(() => null);
    if (!file?.isFile() || file.size !== record.size || await sha256(assetPath) !== record.sha256) return null;
  }
  return validated;
}

export async function writeReleaseCheckpoint({ repositoryRoot, version, commit, phase, assetPaths = [] }) {
  const checkpointPath = getReleaseCheckpointPath(repositoryRoot, version);
  await mkdir(path.dirname(checkpointPath), { recursive: true });
  const artifacts = [];
  if (phase === "packaged") {
    for (const assetPath of assetPaths) {
      const file = await stat(assetPath);
      artifacts.push({ name: path.basename(assetPath), size: file.size, sha256: await sha256(assetPath) });
    }
  }
  const pendingPath = `${checkpointPath}.pending`;
  await writeFile(pendingPath, `${JSON.stringify({ schema: 1, version, commit, phase, artifacts }, null, 2)}\n`, "utf8");
  await rename(pendingPath, checkpointPath);
}
