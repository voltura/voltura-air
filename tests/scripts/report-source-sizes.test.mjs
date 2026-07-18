import assert from "node:assert/strict";
import { execFile } from "node:child_process";
import { mkdir, mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import test from "node:test";
import { promisify } from "node:util";

const executeFile = promisify(execFile);
const repositoryRoot = new URL("../../", import.meta.url);

async function withFixture(reviews, action) {
  const root = await mkdtemp(path.join(tmpdir(), "voltura-air-source-size-"));
  try {
    await mkdir(path.join(root, "scripts"), { recursive: true });
    await mkdir(path.join(root, "src"), { recursive: true });
    await writeFile(
      path.join(root, "scripts/report-source-sizes.mjs"),
      await readFile(new URL("scripts/report-source-sizes.mjs", repositoryRoot), "utf8"),
      "utf8");
    await writeFile(path.join(root, "scripts/source-size-reviews.json"), JSON.stringify(reviews), "utf8");
    await writeFile(path.join(root, "src/large.cs"), "public class Line {}\n".repeat(501), "utf8");
    await action(root);
  } finally {
    await rm(root, { recursive: true, force: true });
  }
}

async function check(root) {
  return executeFile(process.execPath, ["scripts/report-source-sizes.mjs", "--check"], { cwd: root });
}

test("accepts a specific current review for every strong source-size warning", async () => {
  await withFixture(
    { "src/large.cs": "This fixture is intentionally cohesive so the strong-warning review has a sufficiently specific rationale." },
    async (root) => {
      const result = await check(root);
      assert.match(result.stdout, /Every strong source-size warning has a current cohesive-ownership review/u);
    });
});

test("rejects an unreviewed strong source-size warning", async () => {
  await withFixture({}, async (root) => {
    await assert.rejects(check(root), (error) => {
      assert.match(String(error.stderr), /Strong size warning needs a specific review: src\/large\.cs/u);
      return true;
    });
  });
});

test("rejects a stale source-size review", async () => {
  await withFixture(
    {
      "src/large.cs": "This fixture is intentionally cohesive so the strong-warning review has a sufficiently specific rationale.",
      "src/removed.cs": "This review must be removed when the warning disappears so the checked inventory remains current."
    },
    async (root) => {
      await assert.rejects(check(root), (error) => {
        assert.match(String(error.stderr), /Source-size review is stale.*src\/removed\.cs/u);
        return true;
      });
    });
});
