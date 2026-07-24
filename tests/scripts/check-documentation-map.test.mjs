import assert from "node:assert/strict";
import { mkdir, mkdtemp, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import test from "node:test";
import { checkDocumentationMap } from "../../scripts/check-documentation-map.mjs";

async function createFixture(files) {
  const root = await mkdtemp(path.join(tmpdir(), "voltura-air-docs-"));
  for (const [relativePath, contents] of Object.entries(files)) {
    const absolutePath = path.join(root, relativePath);
    await mkdir(path.dirname(absolutePath), { recursive: true });
    await writeFile(absolutePath, contents, "utf8");
  }
  return root;
}

async function withFixture(files, action) {
  const root = await createFixture(files);
  try {
    await action(root);
  } finally {
    await rm(root, { recursive: true, force: true });
  }
}

test("accepts a complete documentation catalog with valid local links", async () => {
  await withFixture(
    {
      "README.md": "See the [guide](docs/guide.md).\n",
      "docs/README.md": "[Root](../README.md)\n[Guide](guide.md)\n",
      "docs/guide.md": "Return to the [root](../README.md).\n"
    },
    async (root) => {
      const result = await checkDocumentationMap({ root, publicSurfaces: [] });
      assert.deepEqual(result.errors, []);
      assert.equal(result.requiredFiles.length, 2);
      assert.equal(result.checkedLinks, 4);
    }
  );
});

test("reports a maintained Markdown document missing from the catalog", async () => {
  await withFixture(
    {
      "README.md": "Root\n",
      "docs/README.md": "[Root](../README.md)\n",
      "docs/orphan.md": "Orphan\n"
    },
    async (root) => {
      const result = await checkDocumentationMap({ root, publicSurfaces: [] });
      assert.ok(result.errors.includes("Documentation map does not catalog: docs/orphan.md"));
    }
  );
});

test("reports broken local documentation links", async () => {
  await withFixture(
    {
      "README.md": "See [missing](docs/missing.md).\n",
      "docs/README.md": "[Root](../README.md)\n"
    },
    async (root) => {
      const result = await checkDocumentationMap({ root, publicSurfaces: [] });
      assert.ok(result.errors.includes("README.md links to missing local target: docs/missing.md"));
    }
  );
});

test("discovers public documentation surfaces and requires them in the catalog", async () => {
  await withFixture(
    {
      "README.md": "Root\n",
      "docs/README.md": "[Root](../README.md)\n",
      "docs/site/index.php": "<!doctype html>\n"
    },
    async (root) => {
      const result = await checkDocumentationMap({ root });
      assert.ok(result.errors.includes("Documentation map does not catalog: docs/site/index.php"));
    }
  );
});

test("discovers structured public issue forms and requires them in the catalog", async () => {
  await withFixture(
    {
      "README.md": "Root\n",
      "docs/README.md": "[Root](../README.md)\n",
      ".github/ISSUE_TEMPLATE/bug_report.yml": "name: Bug report\n"
    },
    async (root) => {
      const result = await checkDocumentationMap({ root, publicSurfaces: [] });
      assert.ok(result.errors.includes(
        "Documentation map does not catalog: .github/ISSUE_TEMPLATE/bug_report.yml"
      ));
    }
  );
});

test("accepts documented root and workspace npm scripts with arguments", async () => {
  await withFixture(
    {
      "package.json": JSON.stringify({
        scripts: { "docs:check": "node check.mjs" },
        workspaces: ["apps/mobile-web"]
      }),
      "apps/mobile-web/package.json": JSON.stringify({
        scripts: { lint: "eslint ." }
      }),
      "README.md": "Run `npm run docs:check` and `npm run lint --workspace apps/mobile-web`.\n",
      "docs/README.md": "[Root](../README.md)\n"
    },
    async (root) => {
      const result = await checkDocumentationMap({ root, publicSurfaces: [] });
      assert.deepEqual(result.errors, []);
    }
  );
});

test("reports a documented npm script that no package exposes", async () => {
  await withFixture(
    {
      "package.json": JSON.stringify({ scripts: { test: "node --test" } }),
      "README.md": "Run `npm run missing:check`.\n",
      "docs/README.md": "[Root](../README.md)\n"
    },
    async (root) => {
      const result = await checkDocumentationMap({ root, publicSurfaces: [] });
      assert.ok(result.errors.includes("README.md references missing npm script: missing:check"));
    }
  );
});

test("ignores scripts from packages outside the declared workspaces", async () => {
  await withFixture(
    {
      "package.json": JSON.stringify({ scripts: {} }),
      "tools/unrelated/package.json": JSON.stringify({ scripts: { hidden: "node hidden.mjs" } }),
      "README.md": "Run `npm run hidden`.\n",
      "docs/README.md": "[Root](../README.md)\n"
    },
    async (root) => {
      const result = await checkDocumentationMap({ root, publicSurfaces: [] });
      assert.ok(result.errors.includes("README.md references missing npm script: hidden"));
    }
  );
});
