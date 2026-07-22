import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const updater = readFileSync(new URL("../../scripts/update-chatgpt-codex.ps1", import.meta.url), "utf8");

test("the ChatGPT/Codex updater verifies deployment when the WinRT adapter omits its result", () => {
  assert.match(updater, /\$null -ne \$deploymentResult -and \[int64\]\$deploymentResult\.ExtendedErrorCode -ne 0/u);
  assert.match(updater, /if \(\$null -eq \$installedAfter\)/u);
  assert.match(updater, /\[version\]\$installedAfter\.Version -lt \$onlineVersion/u);
});
