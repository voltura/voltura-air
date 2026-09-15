import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import { generateKeyPairSync } from "node:crypto";
import { readFileSync } from "node:fs";
import test from "node:test";
import { validateUpdateSigningKey } from "../../scripts/update-signing-key.mjs";

test(
  "Windows setup boundary tests exercise actual PowerShell functions",
  { skip: process.platform !== "win32" },
  () => {
    const result = execFileSync(
      "pwsh.exe",
      ["-NoProfile", "-File", "tests/scripts/setup-windows.test.ps1"],
      { encoding: "utf8", windowsHide: true },
    );
    assert.match(result, /Setup boundary tests passed/u);
  },
);

test(
  "bootstrap entry and common helpers parse with inbox Windows PowerShell",
  { skip: process.platform !== "win32" },
  () => {
    const files = [
      "scripts/setup-windows.ps1",
      "scripts/setup/Setup.Common.psm1",
      "scripts/setup/Setup.Tools.psm1",
    ];
    const command = `Import-Module ./scripts/setup/Setup.Common.psm1 -Force; Import-Module ./scripts/setup/Setup.Tools.psm1 -Force; Get-Command Invoke-SetupCommand -ErrorAction Stop | Out-Null; $failed = $false; foreach ($path in @(${files.map((file) => `'${file}'`).join(",")})) { $tokens = $null; $errors = $null; [void][Management.Automation.Language.Parser]::ParseFile((Join-Path (Get-Location) $path),[ref]$tokens,[ref]$errors); if ($errors) { $failed = $true; $errors | Out-String | Write-Output } }; if ($failed) { exit 1 }`;
    execFileSync("powershell.exe", ["-NoProfile", "-Command", command], {
      stdio: "pipe",
      windowsHide: true,
    });
  },
);

test("signing verification rejects missing, unencrypted, wrong-password and mismatched keys without exposing input", () => {
  const secret = "test-only secret sentinel";
  const pair = generateKeyPairSync("rsa", { modulusLength: 2048 });
  const pem = pair.privateKey.export({
    format: "pem",
    type: "pkcs8",
    cipher: "aes-256-cbc",
    passphrase: secret,
  });
  const publicPem = pair.publicKey.export({ format: "pem", type: "spki" });
  const env = {
    VOLTURA_AIR_UPDATE_SIGNING_KEY_PATH: "fixture.pem",
    VOLTURA_AIR_UPDATE_SIGNING_PASSPHRASE: secret,
  };
  const read = (file) => (file === "fixture.pem" ? pem : publicPem);
  assert.equal(validateUpdateSigningKey("fixture", env, read).asymmetricKeyType, "rsa");
  for (const environment of [
    {},
    { ...env, VOLTURA_AIR_UPDATE_SIGNING_PASSPHRASE: " " },
    { ...env, VOLTURA_AIR_UPDATE_SIGNING_PASSPHRASE: "incorrect" },
  ]) {
    assert.throws(
      () => validateUpdateSigningKey("fixture", environment, read),
      (error) => !error.message.includes(secret) && !error.message.includes(pem),
    );
  }
  const other = generateKeyPairSync("rsa", { modulusLength: 2048 }).publicKey.export({
    format: "pem",
    type: "spki",
  });
  assert.throws(
    () =>
      validateUpdateSigningKey("fixture", env, (file) => (file === "fixture.pem" ? pem : other)),
    /does not match/u,
  );
  assert.throws(
    () =>
      validateUpdateSigningKey("fixture", env, () =>
        pair.privateKey.export({ format: "pem", type: "pkcs8" }),
      ),
    /could not be opened/u,
  );
});

test("setup verification is separate from publication and fresh schema seeds are retryable", () => {
  const setup = readFileSync("scripts/setup/Configure-Windows.ps1", "utf8");
  assert.doesNotMatch(setup, /release:full|release:draft|git.*push|relay:deploy/u);
  const site = readFileSync("scripts/site-dev-init.ps1", "utf8");
  assert.match(site, /CREATE TABLE IF NOT EXISTS/u);
  assert.match(site, /INSERT IGNORE INTO/u);
  assert.ok(
    site.indexOf("Write-SetupAtomicFile $configPath") <
      site.indexOf("CREATE DATABASE IF NOT EXISTS"),
  );
});

test(
  "custom packaging rejects unsafe destinations and uncompressed verification",
  { skip: process.platform !== "win32" },
  () => {
    for (const [args, expected] of [
      [["-OutputDirectory", ".codex-temp/disallowed-package-output"], /must be a subdirectory/u],
      [
        ["-OutputDirectory", "artifacts/setup-check", "-NoInstallerCompression"],
        /compressed package verification only/u,
      ],
    ]) {
      assert.throws(
        () =>
          execFileSync(
            "pwsh.exe",
            ["-NoProfile", "-File", "scripts/package-win.ps1", "-SkipBuild", ...args],
            { stdio: "pipe", windowsHide: true },
          ),
        (error) => expected.test(String(error.stderr)),
      );
    }
  },
);
