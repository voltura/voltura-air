import assert from "node:assert/strict";
import { execFileSync, spawnSync } from "node:child_process";
import { mkdtempSync, mkdirSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

const repositoryRoot = fileURLToPath(new URL("../..", import.meta.url));
const mobileRoot = path.join(repositoryRoot, "apps", "mobile-web");
const oxlint = path.join(repositoryRoot, "node_modules", "oxlint", "bin", "oxlint");
const config = path.join(mobileRoot, ".oxlint-parity.json");

function runOxlint(file) {
  return spawnSync(process.execPath, [oxlint, "--config", config, file], {
    cwd: mobileRoot,
    encoding: "utf8",
  });
}

test("Oxlint JavaScript bridge detects every retained lifecycle leak and the architecture boundary", (t) => {
  const fixtureRoot = mkdtempSync(path.join(tmpdir(), "voltura-air-oxlint-"));
  t.after(() => rmSync(fixtureRoot, { recursive: true, force: true }));
  const sourceRoot = path.join(fixtureRoot, "src");
  mkdirSync(path.join(sourceRoot, "ui"), { recursive: true });
  const invalidFile = path.join(sourceRoot, "ui", "Leaks.tsx");
  writeFileSync(
    invalidFile,
    `
    import { useEffect } from "react";
    import { privateFeature } from "../features/privateFeature";
    export function Leaks() {
      useEffect(() => { window.addEventListener("resize", () => undefined); }, []);
      useEffect(() => { void fetch("/"); }, []);
      useEffect(() => { const observer = new IntersectionObserver(() => undefined); observer.observe(document.body); }, []);
      useEffect(() => { window.setInterval(() => undefined, 100); }, []);
      useEffect(() => { const observer = new ResizeObserver(() => undefined); observer.observe(document.body); }, []);
      useEffect(() => { window.setTimeout(() => undefined, 100); }, []);
      return <div>{privateFeature}</div>;
    }
  `,
  );

  const result = runOxlint(invalidFile);
  const output = `${result.stdout}\n${result.stderr}`;
  assert.notEqual(result.status, 0, output);
  for (const rule of [
    "no-leaked-event-listener",
    "no-leaked-fetch",
    "no-leaked-intersection-observer",
    "no-leaked-interval",
    "no-leaked-resize-observer",
    "no-leaked-timeout",
    "dependency-direction",
  ]) {
    assert.match(output, new RegExp(rule, "u"), `Expected ${rule} in:\n${output}`);
  }
});

test("Oxlint JavaScript bridge keeps paired lifecycle cleanup clean", (t) => {
  const fixtureRoot = mkdtempSync(path.join(tmpdir(), "voltura-air-oxlint-"));
  t.after(() => rmSync(fixtureRoot, { recursive: true, force: true }));
  const sourceRoot = path.join(fixtureRoot, "src", "ui");
  mkdirSync(sourceRoot, { recursive: true });
  const validFile = path.join(sourceRoot, "Clean.tsx");
  writeFileSync(
    validFile,
    `
    import { useEffect } from "react";
    export function Clean() {
      useEffect(() => {
        const controller = new AbortController();
        const onResize = () => undefined;
        const intersection = new IntersectionObserver(() => undefined);
        const resize = new ResizeObserver(() => undefined);
        const interval = window.setInterval(() => undefined, 100);
        const timeout = window.setTimeout(() => undefined, 100);
        window.addEventListener("resize", onResize);
        void fetch("/", { signal: controller.signal });
        intersection.observe(document.body);
        resize.observe(document.body);
        return () => {
          controller.abort();
          window.removeEventListener("resize", onResize);
          intersection.disconnect();
          resize.disconnect();
          window.clearInterval(interval);
          window.clearTimeout(timeout);
        };
      }, []);
      return null;
    }
  `,
  );

  const result = runOxlint(validFile);
  assert.equal(result.status, 0, `${result.stdout}\n${result.stderr}`);
});

test("the migrated config keeps exactly the reviewed nursery rules and validated overrides", () => {
  const printed = execFileSync(process.execPath, [oxlint, "--print-config", "src/App.tsx"], {
    cwd: mobileRoot,
    encoding: "utf8",
  });
  for (const rule of [
    "no-undef",
    "no-useless-assignment",
    "typescript/prefer-optional-chain",
    "import/named",
    "import/export",
  ]) {
    assert.match(printed, new RegExp(`"${rule}": "deny"`, "u"));
  }
  assert.doesNotMatch(printed, /includeRoles/u);

  const testConfig = execFileSync(
    process.execPath,
    [oxlint, "--print-config", "src/example.test.tsx"],
    {
      cwd: mobileRoot,
      encoding: "utf8",
    },
  );
  assert.match(testConfig, /"react\/globals": "allow"/u);
  assert.match(testConfig, /"typescript\/no-deprecated": "allow"/u);
});
