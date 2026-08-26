import { constants } from "node:fs";
import { copyFile, link, mkdir, readFile, readdir, rename, rm, stat } from "node:fs/promises";
import { randomUUID } from "node:crypto";
import path from "node:path";
import { fileURLToPath } from "node:url";

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");

export async function restoreGithubActions({
  sourceDirectory = path.join(repositoryRoot, "scripts", "legacy"),
  targetDirectory = path.join(repositoryRoot, ".github", "workflows"),
  copy = (source, target) => copyFile(source, target, constants.COPYFILE_EXCL),
  makeDirectory = mkdir,
  publish = link,
  move = rename,
  read = readFile,
  readDirectory = readdir,
  remove = rm,
  inspect = stat,
  createId = randomUUID,
} = {}) {
  const workflowNames = (await readDirectory(sourceDirectory))
    .filter((name) => /\.ya?ml$/u.test(name))
    .sort();
  if (workflowNames.length === 0) {
    throw new Error(`No archived workflow files were found in ${sourceDirectory}.`);
  }

  await makeDirectory(targetDirectory, { recursive: true });
  const existing = new Set(await readDirectory(targetDirectory));
  const conflicts = workflowNames.filter((name) => existing.has(name));
  if (conflicts.length > 0) {
    throw new Error(`Refusing to overwrite existing workflows: ${conflicts.join(", ")}`);
  }

  const copied = [];
  const temporaryFiles = [];
  try {
    for (const name of workflowNames) {
      const sourcePath = path.join(sourceDirectory, name);
      const expectedContents = await read(sourcePath);
      const temporaryPath = path.join(
        targetDirectory,
        `.voltura-restore-${createId()}-${name}.tmp`,
      );
      const temporaryFile = { path: temporaryPath, identity: null };
      temporaryFiles.push(temporaryFile);
      await copy(sourcePath, temporaryPath);
      const temporaryIdentity = await inspect(temporaryPath);
      temporaryFile.identity = temporaryIdentity;
      const copiedContents = await read(temporaryPath);
      if (!copiedContents.equals(expectedContents)) {
        throw new Error(`Archived workflow ${name} changed while it was being staged.`);
      }
      const targetPath = path.join(targetDirectory, name);
      try {
        await publish(temporaryPath, targetPath);
      } catch (publishError) {
        try {
          const publishedIdentity = await inspect(targetPath);
          if (sameFileIdentity(publishedIdentity, temporaryIdentity)) {
            copied.push({ name, expectedContents, identity: temporaryIdentity, temporaryPath });
          }
        } catch (inspectionError) {
          if (inspectionError?.code !== "ENOENT") {
            throw new AggregateError(
              [publishError, inspectionError],
              `Workflow ${name} publication failed and its target ownership could not be reconciled.`,
            );
          }
        }
        throw publishError;
      }
      copied.push({ name, expectedContents, identity: temporaryIdentity, temporaryPath });
      const targetIdentity = await inspect(targetPath);
      if (!sameFileIdentity(targetIdentity, temporaryIdentity)) {
        throw new Error(`Published workflow ${name} does not match the staged file identity.`);
      }
    }
  } catch (error) {
    const cleanupErrors = [];
    for (const { name, expectedContents, identity } of copied.reverse()) {
      const targetPath = path.join(targetDirectory, name);
      try {
        await removeOwnedPath(targetPath, {
          identity,
          expectedContents,
          label: `workflow ${name} during rollback`,
          inspect,
          move,
          publish,
          read,
          remove,
          createId,
        });
      } catch (cleanupError) {
        cleanupErrors.push(cleanupError);
      }
    }
    await cleanOwnedTemporaryFiles(
      temporaryFiles,
      { inspect, move, publish, read, remove, createId },
      cleanupErrors,
    );
    if (cleanupErrors.length > 0) {
      throw new AggregateError(
        [error, ...cleanupErrors],
        "Workflow restoration and rollback both failed.",
      );
    }
    throw error;
  }
  const cleanupErrors = [];
  await cleanOwnedTemporaryFiles(
    temporaryFiles,
    { inspect, move, publish, read, remove, createId },
    cleanupErrors,
  );
  if (cleanupErrors.length > 0) {
    throw new AggregateError(
      cleanupErrors,
      "Workflows were restored, but staging-file cleanup failed.",
    );
  }
  return workflowNames;
}

function sameFileIdentity(left, right) {
  return left.dev === right.dev && left.ino === right.ino;
}

async function cleanOwnedTemporaryFiles(temporaryFiles, dependencies, cleanupErrors) {
  for (const temporary of temporaryFiles.reverse()) {
    try {
      if (temporary.identity === null) {
        try {
          await dependencies.inspect(temporary.path);
        } catch (error) {
          if (error?.code === "ENOENT") {
            continue;
          }
          throw error;
        }
        throw new Error(
          `Refusing to remove an unverified workflow staging file ${path.basename(temporary.path)}.`,
        );
      }
      await removeOwnedPath(temporary.path, {
        ...dependencies,
        identity: temporary.identity,
        label: `workflow staging file ${path.basename(temporary.path)}`,
      });
    } catch (cleanupError) {
      cleanupErrors.push(cleanupError);
    }
  }
}

async function removeOwnedPath(
  filePath,
  { identity, expectedContents, label, inspect, move, publish, read, remove, createId },
) {
  const quarantinePath = `${filePath}.voltura-owned-${createId()}.quarantine`;
  try {
    await move(filePath, quarantinePath);
  } catch (moveError) {
    if (!(await pathExists(quarantinePath, inspect))) {
      throw moveError;
    }
    // The rename reached its intended state before reporting failure. Continue
    // with identity verification so rollback does not leave a partial result.
  }
  let removalAttempted = false;
  try {
    const quarantinedIdentity = await inspect(quarantinePath);
    const contentsMatch =
      expectedContents === undefined || (await read(quarantinePath)).equals(expectedContents);
    if (!sameFileIdentity(quarantinedIdentity, identity) || !contentsMatch) {
      throw new Error(`Refusing to remove concurrently changed ${label}.`);
    }
    removalAttempted = true;
    await remove(quarantinePath);
  } catch (error) {
    if (removalAttempted && !(await pathExists(quarantinePath, inspect))) {
      return;
    }
    try {
      await restoreQuarantinedPath(quarantinePath, filePath, { publish, remove });
    } catch (restoreError) {
      throw new AggregateError(
        [error, restoreError],
        `Could not restore ${label} after quarantining it; preserved copy: ${quarantinePath}`,
      );
    }
    throw error;
  }
}

async function restoreQuarantinedPath(quarantinePath, filePath, { publish, remove }) {
  await publish(quarantinePath, filePath);
  await remove(quarantinePath);
}

async function pathExists(filePath, inspect) {
  try {
    await inspect(filePath);
    return true;
  } catch (error) {
    if (error?.code === "ENOENT") {
      return false;
    }
    throw error;
  }
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  restoreGithubActions()
    .then((names) => {
      console.log(`Restored GitHub Actions workflows: ${names.join(", ")}`);
      console.log("Review the files and GitHub workflow state before committing them.");
    })
    .catch((error) => {
      console.error(error.message);
      process.exitCode = 1;
    });
}
