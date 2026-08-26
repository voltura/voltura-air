import { execFileSync } from "node:child_process";
import { existsSync, readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const supportedSource = /\.(?:cjs|css|js|jsx|mjs|ts|tsx)$/u;
const inlineDirective = /(?:eslint|oxlint)-(?:disable|enable)(?:-line|-next-line)?\b/u;
const trackedFiles = execFileSync("git", ["ls-files", "-z"], {
  cwd: repositoryRoot,
  encoding: "utf8",
})
  .split("\0")
  .filter((file) => supportedSource.test(file));

const violations = trackedFiles.filter((file) => {
  const absolutePath = path.join(repositoryRoot, file);
  return existsSync(absolutePath) && inlineDirective.test(readFileSync(absolutePath, "utf8"));
});
if (violations.length > 0) {
  console.error(`Inline lint suppression is prohibited:\n- ${violations.join("\n- ")}`);
  process.exit(1);
}

console.log(`Lint policy check passed for ${trackedFiles.length} tracked source files.`);
