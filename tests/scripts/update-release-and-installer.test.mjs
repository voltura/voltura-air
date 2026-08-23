import assert from "node:assert/strict";
import test from "node:test";
import { readFile } from "node:fs/promises";

const installer = await readFile(new URL("../../installer/VolturaAir.nsi", import.meta.url), "utf8");
const signing = await readFile(new URL("../../scripts/sign-update-manifest.mjs", import.meta.url), "utf8");

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
