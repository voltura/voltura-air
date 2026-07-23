import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const installer = await readFile(
  new URL("../../installer/VolturaAir.nsi", import.meta.url),
  "utf8"
);

test("installer becomes visible after launch without remaining always on top", () => {
  const restore = installer.match(
    /Function RestoreInstallerWindow(?<body>[\s\S]*?)FunctionEnd/u
  )?.groups?.body ?? "";
  const finish = installer.match(
    /Function FinishInstallerWindowActivation(?<body>[\s\S]*?)FunctionEnd/u
  )?.groups?.body ?? "";

  assert.match(restore, /ShowWindow \$HWNDPARENT \$\{SW_RESTORE\}/u);
  assert.match(restore, /SetWindowPos\(p \$HWNDPARENT, p -1,/u);
  assert.match(installer, /!define MUI_PAGE_CUSTOMFUNCTION_SHOW FinishInstallerWindowActivation\s*!insertmacro MUI_PAGE_WELCOME/u);
  assert.match(finish, /SetForegroundWindow\(p \$HWNDPARENT\)/u);
  assert.match(finish, /SetWindowPos\(p \$HWNDPARENT, p -2,/u);
  assert.ok(finish.indexOf("SetForegroundWindow") < finish.indexOf("p -2"));
});
