import assert from "node:assert/strict";
import test from "node:test";
import { readFile } from "node:fs/promises";
import { execFile } from "node:child_process";
import { promisify } from "node:util";

const installer = await readFile(new URL("../../installer/VolturaAir.nsi", import.meta.url), "utf8");
const signing = await readFile(new URL("../../scripts/sign-update-manifest.mjs", import.meta.url), "utf8");
const execFileAsync = promisify(execFile);

test("silent automatic update switch is detected from GetOptions success", () => {
  assert.match(installer, /ClearErrors\s+\$\{GetOptions\} \$CMDLINE "\/AUTOUPDATE" \$0\s+IfErrors auto_update_missing/u);
  assert.match(installer, /IfSilent auto_update_silent/u);
  assert.match(installer, /StrCpy \$AutoUpdate 1/u);
});

test("release signing self-verifies with the pinned host public key", () => {
  assert.match(signing, /apps\/windows-host\/Features\/Updates\/update-signing-public\.pem/u);
  assert.doesNotMatch(signing, /createPublicKey\(privateKey\)/u);
  assert.match(signing, /Update manifest does not verify with the host public key/u);
});

test("release signing accepts a non-empty environment passphrase without prompting", async () => {
  assert.match(signing, /VOLTURA_AIR_UPDATE_SIGNING_PASSPHRASE/u);
  await assert.rejects(
    execFileAsync(process.execPath, ["scripts/sign-update-manifest.mjs", "1.2.3", "artifacts/publish"], {
      env: { ...process.env, VOLTURA_AIR_UPDATE_SIGNING_KEY_PATH: "unused", VOLTURA_AIR_UPDATE_SIGNING_PASSPHRASE: " supplied " }
    }),
    (error) => !String(error.stderr).includes("Update signing passphrase:")
  );
});

test("release signing rejects an environment passphrase containing only whitespace", async () => {
  await assert.rejects(
    execFileAsync(process.execPath, ["scripts/sign-update-manifest.mjs", "1.2.3", "artifacts/publish"], {
      env: { ...process.env, VOLTURA_AIR_UPDATE_SIGNING_KEY_PATH: "unused", VOLTURA_AIR_UPDATE_SIGNING_PASSPHRASE: " \t " }
    }),
    (error) => String(error.stderr).includes("No update signing passphrase was provided.")
  );
});

test("release signing keeps the interactive fallback when the environment passphrase is unset", async () => {
  const env = { ...process.env, VOLTURA_AIR_UPDATE_SIGNING_KEY_PATH: "unused" };
  delete env.VOLTURA_AIR_UPDATE_SIGNING_PASSPHRASE;
  await assert.rejects(
    execFileAsync(process.execPath, ["scripts/sign-update-manifest.mjs", "1.2.3", "artifacts/publish"], { env }),
    (error) => String(error.stderr).includes("An authorized interactive terminal is required")
  );
});
