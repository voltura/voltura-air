import assert from "node:assert/strict";
import { mkdtempSync, mkdirSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";

import {
  createFileFingerprint,
  createOutputFingerprint,
  isCurrentMobileQuickBuild,
  MOBILE_QUICK_BUILD_STATE_SCHEMA_VERSION
} from "../../scripts/build-mobile-quick.mjs";

const temporaryDirectories = [];

test.afterEach(() => {
  for (const directory of temporaryDirectories.splice(0)) {
    rmSync(directory, { recursive: true, force: true });
  }
});

test("mobile quick build state is current only when inputs and outputs are unchanged", () => {
  const repositoryRoot = mkdtempSync(join(tmpdir(), "voltura-air-mobile-quick-"));
  temporaryDirectories.push(repositoryRoot);
  const inputPath = join(repositoryRoot, "src", "main.tsx");
  const outputDirectory = join(repositoryRoot, "dist");
  const outputPath = join(outputDirectory, "index.html");
  mkdirSync(join(repositoryRoot, "src"), { recursive: true });
  mkdirSync(outputDirectory, { recursive: true });
  writeFileSync(inputPath, "export const value = 1;\n");
  writeFileSync(outputPath, "<!doctype html>\n");

  const inputFingerprint = createFileFingerprint([inputPath], repositoryRoot);
  const outputFingerprint = createOutputFingerprint(outputDirectory);
  const state = {
    schemaVersion: MOBILE_QUICK_BUILD_STATE_SCHEMA_VERSION,
    inputFingerprint,
    outputFingerprint,
    webBuildId: "build-1"
  };

  assert.equal(isCurrentMobileQuickBuild(state, inputFingerprint, outputFingerprint, ""), true);
  assert.equal(isCurrentMobileQuickBuild(state, inputFingerprint, outputFingerprint, "build-2"), false);

  writeFileSync(inputPath, "export const value = 2;\n");
  const changedInputFingerprint = createFileFingerprint([inputPath], repositoryRoot);
  assert.equal(isCurrentMobileQuickBuild(state, changedInputFingerprint, outputFingerprint, ""), false);
});

test("mobile quick build state is stale when the output directory is missing", () => {
  const repositoryRoot = mkdtempSync(join(tmpdir(), "voltura-air-mobile-quick-"));
  temporaryDirectories.push(repositoryRoot);
  const inputPath = join(repositoryRoot, "src", "main.tsx");
  mkdirSync(join(repositoryRoot, "src"), { recursive: true });
  writeFileSync(inputPath, "export const value = 1;\n");

  const inputFingerprint = createFileFingerprint([inputPath], repositoryRoot);
  const state = {
    schemaVersion: MOBILE_QUICK_BUILD_STATE_SCHEMA_VERSION,
    inputFingerprint,
    outputFingerprint: "previous-output",
    webBuildId: "build-1"
  };

  assert.equal(isCurrentMobileQuickBuild(state, inputFingerprint, null, ""), false);
});
