import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import { mkdir, mkdtemp, readFile, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import test from "node:test";

const repositoryRoot = process.cwd();
const scriptPath = path.join(repositoryRoot, "scripts", "code-statistics.mjs");

async function writeFixtureFile(root, file, contents) {
  const absolutePath = path.join(root, file);
  await mkdir(path.dirname(absolutePath), { recursive: true });
  await writeFile(absolutePath, contents, "utf8");
}

async function createStatisticsFixture() {
  const root = await mkdtemp(path.join(tmpdir(), "voltura-air-statistics-"));
  await Promise.all([
    writeFixtureFile(root, "Directory.Build.props", "<Project />\n"),
    writeFixtureFile(root, "VolturaAir.slnx", "<Solution />\n"),
    writeFixtureFile(root, ".gitignore", "/.codex-temp/\n/.codex-tmp/\n"),
    writeFixtureFile(root, "README.md", "# Fixture\n"),
    writeFixtureFile(
      root,
      "package.json",
      JSON.stringify({ scripts: { "code:statistics": "node scripts/code-statistics.mjs" } }),
    ),
    writeFixtureFile(
      root,
      "apps/mobile-web/vitest.config.ts",
      "export default { test: { globals: true, include: ['src/**/*.test.{ts,tsx}'] } };\n",
    ),
    writeFixtureFile(
      root,
      "apps/mobile-web/src/App.tsx",
      "export function App() {\n  return null;\n}\n",
    ),
    writeFixtureFile(
      root,
      "apps/mobile-web/src/App.test.tsx",
      "describe('App', () => {\n  it('works', () => {});\n  it.each([1])('works for %s', () => {});\n});\n",
    ),
    writeFixtureFile(
      root,
      "apps/mobile-web/eslint-rules/architecture.test.mjs",
      "test('allows imports', () => {});\n",
    ),
    writeFixtureFile(root, "apps/windows-host/Program.cs", "public static class Program {}\n"),
    writeFixtureFile(root, "apps/cursor-watchdog/watchdog.c", "int main(void) { return 0; }\n"),
    writeFixtureFile(
      root,
      "tests/VolturaAir.Host.Tests/VolturaAir.Host.Tests.csproj",
      "<Project />\n",
    ),
    writeFixtureFile(
      root,
      "tests/VolturaAir.Host.Tests/HostTests.cs",
      "public sealed class HostTests\n{\n    [Fact]\n    public void Works() {}\n    [Theory]\n    public void WorksWithData() {}\n}\n",
    ),
    writeFixtureFile(
      root,
      "tests/VolturaAir.Host.Tests/TestFixture.cs",
      "public sealed class TestFixture {}\n",
    ),
    writeFixtureFile(
      root,
      "services/relay/vitest.config.ts",
      "export default { test: { include: ['tests/**/*.test.ts'] } };\n",
    ),
    writeFixtureFile(root, "services/relay/src/worker.ts", "export const worker = true;\n"),
    writeFixtureFile(
      root,
      "services/relay/tests/worker.test.ts",
      "import { describe, it } from 'vitest';\ndescribe('worker', () => {\n  it('works', () => {});\n});\n",
    ),
    writeFixtureFile(root, "services/relay/wrangler.jsonc", '{ "name": "relay" }\n'),
    writeFixtureFile(root, "services/relay/Dockerfile", "FROM node:22\n"),
    writeFixtureFile(root, "apps/public-site/index.php", "<?php echo 'Voltura Air';\n"),
    writeFixtureFile(root, "apps/public-site/styles.css", "body { color: black; }\n"),
    writeFixtureFile(root, "apps/public-site/schema.sql", "CREATE TABLE screens ();\n"),
    writeFixtureFile(root, "scripts/publish-site.mjs", "export function publishSite() {}\n"),
    writeFixtureFile(root, "scripts/run-chatgpt-codex-update-hidden.vbs", "WScript.Quit 0\n"),
    writeFixtureFile(root, "scripts/legacy/quality.yml", "name: quality\n"),
    writeFixtureFile(root, ".github/workflows/quality.yml", "name: quality\n"),
    writeFixtureFile(
      root,
      "tests/scripts/publish-site.test.mjs",
      "import test from 'node:test';\ntest('publishes', () => {});\ntest('lists', () => {});\n",
    ),
    writeFixtureFile(root, "installer/VolturaAir.nsi", "Name VolturaAir\n"),
    writeFixtureFile(root, "apps/public-site/index.php", "<?php echo 'Voltura Air';\n"),
    writeFixtureFile(root, ".codex-temp/relay-build/generated.js", "// generated\n"),
    writeFixtureFile(root, ".codex-temp/relay-build/generated.png", "generated\n"),
    writeFixtureFile(root, ".codex-tmp/probe/generated.mjs", "// generated\n"),
    writeFixtureFile(
      root,
      ".codex-tmp/probe/package.json",
      JSON.stringify({ scripts: { generated: "node generated.mjs" } }),
    ),
  ]);
  execFileSync("git", ["init", "--quiet"], { cwd: root });
  execFileSync("git", ["-c", "core.autocrlf=false", "add", "--all"], { cwd: root });
  await Promise.all([
    writeFixtureFile(root, "untracked-root.png", "untracked\n"),
    writeFixtureFile(
      root,
      "apps/mobile-web/src/Untracked.test.tsx",
      "it('is not maintained', () => {});\n",
    ),
  ]);
  return root;
}

test("code statistics covers production, test, automation, and script test cases", async () => {
  const root = await createStatisticsFixture();

  const output = execFileSync(process.execPath, [scriptPath], { cwd: root, encoding: "utf8" });

  assert.match(output, /Mobile client\s+\(apps\/mobile-web\)\r?\n  Total: 2 files, 4 lines/u);
  assert.match(output, /Mobile client tests\s+\(apps\/mobile-web\)\r?\n  Total: 2 files, 5 lines/u);
  assert.match(
    output,
    /Windows host tests\s+\(tests\/VolturaAir\.Host\.Tests\)\r?\n  Total: 3 files, 9 lines/u,
  );
  assert.match(output, /Relay service\s+\(services\/relay\)\r?\n  Total: 4 files, 4 lines/u);
  assert.match(
    output,
    /Relay service tests\s+\(services\/relay\/tests\)\r?\n  Total: 1 files, 4 lines/u,
  );
  assert.match(output, /Public website\s+\(apps\/public-site\)\r?\n  Total: 3 files, 3 lines/u);
  assert.match(output, /Repository automation\s+\(scripts\)\r?\n  Total: 3 files, 3 lines/u);
  assert.match(output, /GitHub automation\s+\(\.github\)\r?\n  Total: 1 files, 1 lines/u);
  assert.match(
    output,
    /Repository automation tests\s+\(tests\/scripts\)\r?\n  Total: 1 files, 3 lines/u,
  );
  assert.match(output, /Installers\s+\(installer\)\r?\n  Total: 1 files, 1 lines/u);
  assert.match(output, /Mobile client\s+1 files  2 cases/u);
  assert.match(output, /Windows host\s+1 files  2 cases/u);
  assert.match(output, /Repository automation\s+1 files  2 cases/u);
  assert.match(output, /Relay service\s+1 files  1 cases/u);
  assert.doesNotMatch(output, /\.codex-(?:temp|tmp)/u);
  assert.doesNotMatch(output, /untracked-root/u);
});

test("HTML statistics report uses the comprehensive public source inventory", async () => {
  const root = await createStatisticsFixture();

  execFileSync(process.execPath, [scriptPath, "--report", "--no-open", "--quiet"], {
    cwd: root,
    encoding: "utf8",
  });
  const html = await readFile(path.join(root, "apps", "public-site", "stats.html"), "utf8");

  assert.match(html, /<h2>Windows host tests<\/h2>/u);
  assert.match(html, /<h2>Repository automation<\/h2>/u);
  assert.match(html, /<h2>GitHub automation<\/h2>/u);
  assert.match(html, /<h2>Installers<\/h2>/u);
  assert.match(html, /<h2>Relay service<\/h2>/u);
  assert.match(html, /<h2>Relay service tests<\/h2>/u);
  assert.match(html, /<h2>Public website<\/h2>/u);
  assert.match(html, /<td>Repository automation<\/td><td>1<\/td><td>2<\/td>/u);
  assert.match(html, /discovered test cases expand parameterized data/u);
  assert.match(
    html,
    /every maintained production, test, website, installer, and repository automation area/u,
  );
  assert.doesNotMatch(html, /undefined \d+\.\d+%/u);
  assert.doesNotMatch(html, /NaN%/u);
  assert.doesNotMatch(html, /\.codex-(?:temp|tmp)/u);
  assert.doesNotMatch(html, /untracked-root/u);
  assert.match(html, /<dt>Assets<\/dt><dd>1/u);
  assert.match(html, /<dt>NPM commands<\/dt><dd>1<span>7 script files<\/span>/u);
});
