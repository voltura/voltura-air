import assert from "node:assert/strict";
import { execFile } from "node:child_process";
import { mkdir, mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import test from "node:test";
import { promisify } from "node:util";
import { fileURLToPath } from "node:url";

const executeFile = promisify(execFile);
const repositoryRoot = fileURLToPath(new URL("../../", import.meta.url));
const fixtureFiles = [
  "assets/ui-tokens.json",
  "scripts/generate-ui-tokens.mjs",
  "apps/mobile-web/src/styles/generated/tokens.css",
  "apps/mobile-web/src/ui/tokens.g.ts",
  "apps/windows-host/Styles/Generated/UiTokens.xaml",
  "apps/windows-host/UiTokens.g.cs"
];

async function createFixture() {
  const root = await mkdtemp(path.join(tmpdir(), "voltura-air-ui-tokens-"));
  for (const relativePath of fixtureFiles) {
    const contents = await readFile(path.join(repositoryRoot, relativePath), "utf8");
    const targetPath = path.join(root, relativePath);
    await mkdir(path.dirname(targetPath), { recursive: true });
    await writeFile(targetPath, contents, "utf8");
  }
  return root;
}

async function withFixture(action) {
  const root = await createFixture();
  try {
    await action(root);
  } finally {
    await rm(root, { recursive: true, force: true });
  }
}

async function runTokenCheck(root) {
  return executeFile(process.execPath, [path.join(root, "scripts/generate-ui-tokens.mjs"), "--check"], { cwd: root });
}

async function useCrlfGeneratedFiles(root) {
  for (const relativePath of fixtureFiles.slice(2)) {
    const targetPath = path.join(root, relativePath);
    const contents = await readFile(targetPath, "utf8");
    await writeFile(targetPath, contents.replaceAll(/\r?\n/gu, "\r\n"), "utf8");
  }
}

test("accepts generated token files checked out with CRLF line endings", async () => {
  await withFixture(async (root) => {
    await useCrlfGeneratedFiles(root);
    await runTokenCheck(root);
  });
});

test("still rejects generated token content that is genuinely stale", async () => {
  await withFixture(async (root) => {
    await useCrlfGeneratedFiles(root);
    const cssPath = path.join(root, "apps/mobile-web/src/styles/generated/tokens.css");
    const css = await readFile(cssPath, "utf8");
    await writeFile(cssPath, css.replace("--space-sm: 8px;", "--space-sm: 9px;"), "utf8");

    await assert.rejects(
      runTokenCheck(root),
      (error) => {
        assert.match(String(error.stderr), /tokens\.css is stale/u);
        return true;
      }
    );
  });
});
