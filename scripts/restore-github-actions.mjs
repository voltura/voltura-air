import { copyFile, mkdir, readdir } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");

export async function restoreGithubActions({
  sourceDirectory = path.join(repositoryRoot, "scripts", "legacy"),
  targetDirectory = path.join(repositoryRoot, ".github", "workflows"),
  copy = copyFile,
  makeDirectory = mkdir,
  readDirectory = readdir
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

  for (const name of workflowNames) {
    await copy(path.join(sourceDirectory, name), path.join(targetDirectory, name));
  }
  return workflowNames;
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
