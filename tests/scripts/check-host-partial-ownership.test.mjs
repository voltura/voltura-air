import assert from "node:assert/strict";
import { mkdir, mkdtemp, rm, writeFile } from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import test from "node:test";
import { findMultiFilePartialTypes } from "../../scripts/check-host-partial-ownership.mjs";

test("reports a partial type split across maintained files", async () => {
  await withFixture(async (root) => {
    await writeSource(root, "One.cs", "namespace Example;\npublic partial class SplitOwner {}\n");
    await writeSource(root, "Two.cs", "namespace Example;\npublic partial class SplitOwner {}\n");

    const findings = await findMultiFilePartialTypes(root);

    assert.equal(findings.length, 1);
    assert.equal(findings[0].type, "Example.SplitOwner");
    assert.equal(findings[0].files.length, 2);
  });
});

test("allows framework partials confined to one maintained file", async () => {
  await withFixture(async (root) => {
    await writeSource(root, "Window.xaml.cs", "namespace Example;\npublic partial class Window {}\n");
    await writeSource(root, "Other.cs", "namespace Example;\ninternal sealed class Other {}\n");

    assert.deepEqual(await findMultiFilePartialTypes(root), []);
  });
});

test("distinguishes equal type names in different namespaces and ignores generated files", async () => {
  await withFixture(async (root) => {
    await writeSource(root, "One.cs", "namespace First;\npublic partial class SharedName {}\n");
    await writeSource(root, "Two.cs", "namespace Second;\npublic partial class SharedName {}\n");
    await writeSource(root, "One.g.cs", "namespace First;\npublic partial class SharedName {}\n");

    assert.deepEqual(await findMultiFilePartialTypes(root), []);
  });
});

async function withFixture(action) {
  const root = await mkdtemp(path.join(os.tmpdir(), "voltura-partial-ownership-"));
  try {
    await action(root);
  } finally {
    await rm(root, { recursive: true, force: true });
  }
}

async function writeSource(root, relativePath, contents) {
  const filePath = path.join(root, relativePath);
  await mkdir(path.dirname(filePath), { recursive: true });
  await writeFile(filePath, contents, "utf8");
}
