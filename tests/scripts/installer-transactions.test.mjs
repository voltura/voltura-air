import assert from "node:assert/strict";
import { spawn, spawnSync } from "node:child_process";
import { createHash } from "node:crypto";
import {
  existsSync,
  copyFileSync,
  mkdtempSync,
  mkdirSync,
  readFileSync,
  readdirSync,
  renameSync,
  rmSync,
  writeFileSync,
} from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";

const transactionScriptPath = fileURLToPath(
  new URL("../../installer/InstallTransaction.ps1", import.meta.url),
);

function writeInstallerPayload(directory, contents) {
  mkdirSync(directory);
  writeFileSync(join(directory, "payload.txt"), contents, "utf8");
  const hash = createHash("sha256").update(contents).digest("hex");
  writeFileSync(join(directory, "installer-payload.sha256"), `${hash} *payload.txt\n`, "utf8");
}

function runTransaction(action, installDirectory, journalPath, stagingDirectory) {
  const args = [
    "-NoProfile",
    "-ExecutionPolicy",
    "Bypass",
    "-File",
    transactionScriptPath,
    "-Action",
    action,
    "-InstallDirectory",
    installDirectory,
    "-JournalPath",
    journalPath,
  ];
  if (stagingDirectory) args.push("-StagingDirectory", stagingDirectory);
  const result = spawnSync("powershell.exe", args, {
    encoding: "utf8",
    windowsHide: true,
  });
  assert.equal(result.status, 0, `${action} failed:\n${result.stdout}\n${result.stderr}`);
}

test("installer transaction promotes, verifies, and rolls back only its owned siblings", () => {
  const root = mkdtempSync(join(tmpdir(), "voltura-air-installer-transaction-"));
  const install = join(root, "Voltura Air");
  const staging = `${install}.staging-4160`;
  const journal = join(root, "installer-transaction.json");
  try {
    writeInstallerPayload(install, "old");
    writeInstallerPayload(staging, "new");
    runTransaction("Promote", install, journal, staging);
    assert.equal(readFileSync(join(install, "payload.txt"), "utf8"), "new");
    assert.ok(existsSync(journal));
    runTransaction("Rollback", install, journal);
    assert.equal(readFileSync(join(install, "payload.txt"), "utf8"), "old");
    assert.ok(!existsSync(journal));
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test("installer stops exactly one owned running host", async () => {
  const root = mkdtempSync(join(tmpdir(), "voltura-air-installer-host-"));
  const install = join(root, "Voltura Air");
  const journal = join(root, "installer-transaction.json");
  const hostPath = join(install, "VolturaAir.Host.exe");
  let child;
  try {
    mkdirSync(install);
    copyFileSync(process.execPath, hostPath);
    child = spawn(hostPath, ["-e", "setInterval(() => {}, 1000)"], { windowsHide: true });
    await new Promise((resolve, reject) => {
      child.once("spawn", resolve);
      child.once("error", reject);
    });

    runTransaction("StopHost", install, journal);
    await new Promise((resolve) => child.once("exit", resolve));
  } finally {
    child?.kill();
    rmSync(root, { recursive: true, force: true });
  }
});

test("installer recovery completes a verified promoted upgrade", () => {
  const root = mkdtempSync(join(tmpdir(), "voltura-air-installer-recovery-"));
  const install = join(root, "Voltura Air");
  const staging = `${install}.staging-4160`;
  const journal = join(root, "installer-transaction.json");
  try {
    writeInstallerPayload(install, "old");
    writeInstallerPayload(staging, "new");
    runTransaction("Promote", install, journal, staging);
    runTransaction("Recover", install, journal);
    assert.equal(readFileSync(join(install, "payload.txt"), "utf8"), "new");
    assert.ok(!existsSync(journal));
    assert.deepEqual(readdirSync(root).sort(), ["Voltura Air"]);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test("installer recovery completes a journaled upgrade before its first rename", () => {
  const root = mkdtempSync(join(tmpdir(), "voltura-air-installer-pre-rename-"));
  const install = join(root, "Voltura Air");
  const staging = `${install}.staging-4160`;
  const backup = `${install}.backup-abcdef12`;
  const journal = join(root, "installer-transaction.json");
  try {
    writeInstallerPayload(install, "old");
    writeInstallerPayload(staging, "new");
    const manifestHash = (directory) =>
      createHash("sha256")
        .update(readFileSync(join(directory, "installer-payload.sha256")))
        .digest("hex");
    writeFileSync(
      journal,
      JSON.stringify({
        kind: "voltura-air-installer",
        mode: "upgrade",
        installDirectory: install,
        stagingDirectory: staging,
        backupDirectory: backup,
        oldManifestHash: manifestHash(install),
        newManifestHash: manifestHash(staging),
      }),
      "utf8",
    );

    runTransaction("Recover", install, journal);

    assert.equal(readFileSync(join(install, "payload.txt"), "utf8"), "new");
    assert.ok(!existsSync(journal));
    assert.deepEqual(readdirSync(root).sort(), ["Voltura Air"]);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test("clean-install rollback removes only its verified staged payload", () => {
  const root = mkdtempSync(join(tmpdir(), "voltura-air-clean-rollback-"));
  const install = join(root, "Voltura Air");
  const staging = `${install}.staging-4160`;
  const journal = join(root, "installer-transaction.json");
  try {
    writeInstallerPayload(staging, "new");
    runTransaction("Promote", install, journal, staging);
    const promoted = `${install}.promoted`;
    renameSync(install, promoted);
    renameSync(promoted, staging);
    runTransaction("Rollback", install, journal);
    assert.ok(!existsSync(install));
    assert.ok(!existsSync(staging));
    assert.ok(!existsSync(journal));
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});

test("uninstall transaction resumes the exact retained removal", () => {
  const root = mkdtempSync(join(tmpdir(), "voltura-air-uninstall-transaction-"));
  const install = join(root, "Voltura Air");
  const journal = join(root, "installer-transaction.json");
  try {
    writeInstallerPayload(install, "installed");
    runTransaction("StageRemoval", install, journal);
    assert.ok(!existsSync(install));
    assert.ok(existsSync(journal));
    runTransaction("PrepareUninstall", install, journal);
    runTransaction("StageRemoval", install, journal);
    runTransaction("CompleteRemoval", install, journal);
    assert.ok(!existsSync(install));
    assert.ok(!existsSync(journal));
    assert.deepEqual(readdirSync(root), []);
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
});
