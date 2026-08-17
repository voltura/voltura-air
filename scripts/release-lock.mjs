import { mkdir, rmdir } from "node:fs/promises";
import path from "node:path";
import { spawnSync } from "node:child_process";

export function getReleaseLockPath(repositoryRoot) {
  const result = spawnSync("git", ["rev-parse", "--git-path", "voltura-air-release.lock"], {
    cwd: repositoryRoot,
    encoding: "utf8",
    windowsHide: true
  });
  if (result.status !== 0) throw new Error("Git could not resolve the Voltura Air release lock path.");
  return path.resolve(repositoryRoot, result.stdout.trim());
}

export async function withReleaseLock(action, {
  repositoryRoot,
  lockPath,
  makeDirectory = mkdir,
  remove = rmdir
} = {}) {
  const resolvedLockPath = lockPath ?? getReleaseLockPath(repositoryRoot);
  try {
    await makeDirectory(resolvedLockPath);
  } catch (error) {
    if (error.code === "EEXIST") throw new Error(`Another Voltura Air release is already running (${resolvedLockPath}).`);
    throw error;
  }
  let result;
  let actionError;
  try { result = await action(); }
  catch (error) { actionError = error; }
  try { await remove(resolvedLockPath); }
  catch (cleanupError) {
    if (actionError) throw new AggregateError([actionError, cleanupError], `${actionError.message} Release lock cleanup also failed: ${cleanupError.message}`);
    throw cleanupError;
  }
  if (actionError) throw actionError;
  return result;
}
