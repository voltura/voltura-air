import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const installer = await readFile(
  new URL("../../installer/VolturaAir.nsi", import.meta.url),
  "utf8",
);

test("interactive unset installs require an explicit usage-statistics choice", () => {
  assert.match(installer, /IfSilent usage_consent_init_done/u);
  assert.match(
    installer,
    /ReadRegDWORD \$0 HKCU "\$\{SETTINGS_KEY\}" "\$\{USAGE_CONSENT_VALUE\}"/u,
  );
  assert.match(installer, /Page custom UsageStatisticsPageCreate UsageStatisticsPageLeave/u);
  assert.match(installer, /EnableWindow \$0 0/u);
  assert.match(installer, /UsageConsentChoice == 0[\s\S]*Abort/u);
});

test("the safe choice receives initial focus and choices have equal geometry", () => {
  assert.match(installer, /NSD_CreateButton\} 0 84u 48% 28u "Allow usage statistics"/u);
  assert.match(installer, /NSD_CreateButton\} 52% 84u 48% 28u "Do not allow"/u);
  assert.match(
    installer,
    /SendMessage \$HWNDPARENT \$\{WM_NEXTDLGCTL\} \$UsageConsentDenyButton 1/u,
  );
  assert.doesNotMatch(installer, /SendMessage \$UsageConsentDenyButton \$\{WM_SETFOCUS\}/u);
});

test("consent is persisted only after host health and transaction commit", () => {
  const health = installer.indexOf("--installer-health-check --isolated-test-mode");
  const commit = installer.indexOf("-Action Commit -InstallDirectory");
  const persistCall = installer.indexOf("Call PersistUsageStatisticsConsent");
  assert.ok(health >= 0 && commit > health && persistCall > commit);
  const functionStart = installer.indexOf("Function PersistUsageStatisticsConsent");
  const functionEnd = installer.indexOf("FunctionEnd", functionStart);
  const persistence = installer.slice(functionStart, functionEnd);
  const primary = persistence.slice(0, persistence.indexOf("usage_consent_write_failed:"));
  const staleIdentityRead = primary.indexOf(
    'ReadRegStr $0 HKCU "${SETTINGS_KEY}" "${USAGE_ID_VALUE}"',
  );
  const staleIdentityDelete = primary.indexOf(
    'DeleteRegValue HKCU "${SETTINGS_KEY}" "${USAGE_ID_VALUE}"',
  );
  const consentWrite = primary.indexOf(
    'WriteRegDWORD HKCU "${SETTINGS_KEY}" "${USAGE_CONSENT_VALUE}" $UsageConsentChoice',
  );
  assert.ok(
    staleIdentityRead >= 0 &&
      staleIdentityDelete > staleIdentityRead &&
      consentWrite > staleIdentityDelete,
  );
  assert.match(
    primary,
    /WriteRegDWORD HKCU "\$\{SETTINGS_KEY\}" "\$\{USAGE_CONSENT_VALUE\}" \$UsageConsentChoice[\s\S]*ReadRegDWORD/u,
  );
  assert.equal(primary.match(/WriteRegDWORD/gu)?.length, 1);
  assert.match(
    persistence,
    /usage_consent_write_failed:[\s\S]*WriteRegDWORD[^\n]* 2[\s\S]*ReadRegDWORD/u,
  );
  assert.match(
    persistence,
    /usage_consent_delete_fallback:[\s\S]*DeleteRegValue[\s\S]*ReadRegDWORD[\s\S]*usage_consent_warn_unknown:/u,
  );
  assert.match(persistence, /usage_consent_warn_unknown:[\s\S]*UsageConsentStateUnknown 1/u);
  assert.match(
    installer,
    /UsageConsentStateUnknown == 1[\s\S]*BM_SETCHECK[\s\S]*EnableWindow \$mui\.FinishPage\.Run 0/u,
  );
});
