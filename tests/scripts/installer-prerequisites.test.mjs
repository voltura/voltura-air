import assert from "node:assert/strict";
import { execFileSync, spawnSync } from "node:child_process";
import {
  cpSync,
  existsSync,
  mkdtempSync,
  mkdirSync,
  readFileSync,
  readdirSync,
  rmSync,
  statSync,
  writeFileSync,
} from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";

const installerPath = new URL("../../installer/VolturaAir.nsi", import.meta.url);
const packageScriptPath = new URL("../../scripts/package-win.ps1", import.meta.url);
const installer = readFileSync(installerPath, "utf8");
const packageScript = readFileSync(packageScriptPath, "utf8");

test("installer transactions use native 64-bit Windows PowerShell", () => {
  const transactionCalls =
    installer.match(/nsExec::ExecTo(?:Stack|Log) .*INSTALL_TRANSACTION_SCRIPT.*$/gm) ?? [];
  assert.ok(transactionCalls.length > 0);
  assert.ok(
    transactionCalls.every((call) => call.includes("$WINDIR\\Sysnative\\WindowsPowerShell")),
  );
  assert.ok(transactionCalls.every((call) => !call.includes("$SYSDIR\\WindowsPowerShell")));
});

function findMakensis() {
  const candidates = [
    "makensis.exe",
    process.env["PROGRAMFILES(X86)"]
      ? join(process.env["PROGRAMFILES(X86)"], "NSIS", "makensis.exe")
      : undefined,
    process.env.ProgramFiles ? join(process.env.ProgramFiles, "NSIS", "makensis.exe") : undefined,
  ].filter(Boolean);

  for (const candidate of candidates) {
    if (candidate === "makensis.exe") {
      const probe = spawnSync(candidate, ["/VERSION"], {
        encoding: "utf8",
        windowsHide: true,
      });
      if (!probe.error && probe.status === 0) {
        return candidate;
      }
    } else if (existsSync(candidate)) {
      return candidate;
    }
  }

  return undefined;
}

function preprocessInstaller({ frameworkDependent }) {
  const makensis = findMakensis();
  assert.ok(makensis, "NSIS is required for installer preprocessor tests");

  const args = [
    "/SAFEPPO",
    "/DAPP_VERSION=0.7.9",
    "/DAPP_VERSION_QUAD=0.7.9.0",
    "/DAPP_ESTIMATED_SIZE_KB=1",
    "/DRUNTIME=win-x64",
    "/DPUBLISH_DIR=C:\\preprocessor-fixture",
    "/DOUTPUT_FILE=C:\\preprocessor-fixture\\VolturaAir.exe",
    "/DTEST_NO_INSTALLER_COMPRESSION",
  ];
  if (frameworkDependent) {
    args.push("/DFRAMEWORK_DEPENDENT");
  }
  args.push(fileURLToPath(installerPath));

  return execFileSync(makensis, args, {
    encoding: "utf8",
    maxBuffer: 4 * 1024 * 1024,
    windowsHide: true,
  });
}

function snapshotTree(root) {
  return readdirSync(root, { recursive: true })
    .sort()
    .map((relativePath) => {
      const absolutePath = join(root, relativePath);
      const stats = statSync(absolutePath);
      return {
        relativePath,
        directory: stats.isDirectory(),
        size: stats.size,
        modified: stats.mtimeMs,
        contents: stats.isFile() ? readFileSync(absolutePath, "base64") : null,
      };
    });
}

function functionBody(source, name) {
  return (
    source.match(new RegExp(`Function ${name}(?<body>[\\s\\S]*?)FunctionEnd`, "u"))?.groups?.body ??
    ""
  );
}

function decodeWorstCaseCommand(command) {
  const worstCasePath = (letter) => `C:\\${letter.repeat(236)}`;
  return command
    .replaceAll("$WINDIR", worstCasePath("w"))
    .replaceAll("$PROGRAMFILES64", worstCasePath("p"))
    .replaceAll("$PLUGINSDIR", worstCasePath("t"))
    .replaceAll("$\\'", "'")
    .replaceAll("$$", "$");
}

function componentResult({ initiallyInstalled, childExitCode, detectedAfterInstall }) {
  if (initiallyInstalled) {
    return { complete: true, provisional: false, reboot: false };
  }
  if (childExitCode === 0) {
    return {
      complete: detectedAfterInstall,
      provisional: false,
      reboot: false,
    };
  }
  if (childExitCode === 3010) {
    return {
      complete: true,
      provisional: !detectedAfterInstall,
      reboot: true,
    };
  }
  return { complete: false, provisional: false, reboot: false };
}

test("unsupported runtimes fail before creating or changing artifacts", () => {
  const fixtureRoot = mkdtempSync(join(tmpdir(), "voltura-air-runtime-"));
  try {
    const scriptsDir = join(fixtureRoot, "scripts");
    const artifactsDir = join(fixtureRoot, "artifacts");
    mkdirSync(scriptsDir);
    mkdirSync(artifactsDir);
    cpSync(packageScriptPath, join(scriptsDir, "package-win.ps1"));
    writeFileSync(join(artifactsDir, "sentinel.txt"), "unchanged", "utf8");
    const before = snapshotTree(fixtureRoot);

    const result = spawnSync(
      "pwsh",
      [
        "-NoProfile",
        "-ExecutionPolicy",
        "Bypass",
        "-File",
        join(scriptsDir, "package-win.ps1"),
        "-Runtime",
        "win-arm64",
      ],
      { encoding: "utf8", windowsHide: true },
    );

    assert.notEqual(result.status, 0);
    assert.match(`${result.stdout}\n${result.stderr}`, /is not supported.*win-x64/su);
    assert.deepEqual(snapshotTree(fixtureRoot), before);
  } finally {
    rmSync(fixtureRoot, { recursive: true, force: true });
  }
});

test("runtime validation precedes every repository and artifact operation", () => {
  const validation = packageScript.indexOf('if ($Runtime -cne "win-x64")');
  assert.ok(validation > 0);
  for (const operation of [
    "$repoRoot = Resolve-Path",
    "$packageJsonPath = Join-Path",
    "New-Item -ItemType Directory",
    "Remove-Item",
    "npm run build",
    "dotnet publish",
    "Compress-Archive",
    "& $makensisPath",
  ]) {
    assert.ok(
      validation < packageScript.indexOf(operation),
      `runtime validation must precede ${operation}`,
    );
  }
});

test("both NSIS compiler invocations treat warnings as errors", () => {
  assert.equal(packageScript.match(/^\s*"\/WX" `$/gmu)?.length, 2);
  assert.equal(packageScript.match(/& \$makensisPath/gu)?.length, 2);
});

test("preprocessor output scopes prerequisites to the standard installer", () => {
  const full = preprocessInstaller({ frameworkDependent: false });
  const standard = preprocessInstaller({ frameworkDependent: true });
  const prerequisiteNames = [
    "TestWindowsDesktopRuntime",
    "TestAspNetCoreRuntime",
    "InstallWindowsDesktopRuntime",
    "InstallAspNetCoreRuntime",
    "CleanupWindowsDesktopRuntime",
    "CleanupAspNetCoreRuntime",
  ];

  for (const name of prerequisiteNames) {
    assert.doesNotMatch(full, new RegExp(`(?:Function|Call) ${name}\\b`, "u"));
    assert.match(standard, new RegExp(`Function ${name}\\b`, "u"));
  }
  for (const name of prerequisiteNames.slice(0, 4)) {
    assert.match(standard, new RegExp(`Call ${name}\\b`, "u"));
  }
});

test("prerequisite acquisition and verification remain inline and component-specific", () => {
  assert.doesNotMatch(installer, /prerequisite[^"'\r\n]*\.ps1/iu);
  assert.match(installer, /https:\/\/aka\.ms\/dotnet\/10\.0\/windowsdesktop-runtime-win-x64\.exe/u);
  assert.match(installer, /https:\/\/aka\.ms\/dotnet\/10\.0\/aspnetcore-runtime-win-x64\.exe/u);
  assert.match(installer, /\$PLUGINSDIR\\VolturaAir-WindowsDesktop\.exe/u);
  assert.match(installer, /\$PLUGINSDIR\\VolturaAir-AspNetCore\.exe/u);
  assert.match(installer, /Status-ne \[System\.Management\.Automation\.SignatureStatus\]::Valid/u);
  assert.match(installer, /\$null-eq \$\$s\.SignerCertificate/u);
  assert.match(installer, /O=Microsoft Corporation/u);
  assert.match(installer, /\/install \/quiet \/norestart/u);
  assert.match(installer, /GetExitCodeProcess/u);
  assert.equal(installer.match(/\$\$ProgressPreference=\$\\'SilentlyContinue\$\\'/gu)?.length, 2);
  assert.equal(installer.match(/Invoke-WebRequest -UseBasicParsing -TimeoutSec 300/gu)?.length, 2);
  assert.match(installer, /\^Microsoft\\\.WindowsDesktop\\\.App 10\\\.0\\\.\$\\'/u);
  assert.match(installer, /\^Microsoft\\\.AspNetCore\\\.App 10\\\.0\\\.\$\\'/u);
});

test("all staged prerequisite commands fit the active default NSIS capacity", () => {
  const commands = [...installer.matchAll(/StrCpy \$2 '(?<command>[^\r\n]+)'/gu)].map(
    (match) => match.groups.command,
  );

  assert.equal(commands.length, 6);
  for (const command of commands) {
    const expanded = decodeWorstCaseCommand(command);
    assert.ok(
      expanded.length <= 1023,
      `worst-case command length ${expanded.length} exceeds the 1,023-character payload`,
    );
  }
  assert.match(installer, /IntCmp \$3 \$\{NSIS_MAX_STRLEN\}/u);
  assert.doesNotMatch(installer, /MAX_(?:COMMAND|POWERSHELL)|1023|1024/u);
});

test("component state handling covers installed, success, restart, and failure results", () => {
  assert.deepEqual(
    componentResult({
      initiallyInstalled: true,
      childExitCode: undefined,
      detectedAfterInstall: true,
    }),
    { complete: true, provisional: false, reboot: false },
  );
  assert.deepEqual(
    componentResult({
      initiallyInstalled: false,
      childExitCode: 0,
      detectedAfterInstall: true,
    }),
    { complete: true, provisional: false, reboot: false },
  );
  assert.deepEqual(
    componentResult({
      initiallyInstalled: false,
      childExitCode: 0,
      detectedAfterInstall: false,
    }),
    { complete: false, provisional: false, reboot: false },
  );
  assert.deepEqual(
    componentResult({
      initiallyInstalled: false,
      childExitCode: 3010,
      detectedAfterInstall: true,
    }),
    { complete: true, provisional: false, reboot: true },
  );
  assert.deepEqual(
    componentResult({
      initiallyInstalled: false,
      childExitCode: 3010,
      detectedAfterInstall: false,
    }),
    { complete: true, provisional: true, reboot: true },
  );
  assert.deepEqual(
    componentResult({
      initiallyInstalled: false,
      childExitCode: 1603,
      detectedAfterInstall: false,
    }),
    { complete: false, provisional: false, reboot: false },
  );

  for (const name of ["InstallWindowsDesktopRuntime", "InstallAspNetCoreRuntime"]) {
    const body = functionBody(installer, name);
    assert.match(body, /\$0 == 0/u);
    assert.match(body, /\$0 == 3010/u);
    assert.match(body, /SetRebootFlag true/u);
    assert.match(body, /provisionally complete pending restart/u);
    assert.match(body, /installer failed with exit code \$0/u);
  }
});

test("both component results preserve aggregate restart state", () => {
  for (const desktopExitCode of [0, 3010]) {
    for (const aspNetExitCode of [0, 3010]) {
      const desktop = componentResult({
        initiallyInstalled: false,
        childExitCode: desktopExitCode,
        detectedAfterInstall: desktopExitCode === 0,
      });
      const aspNet = componentResult({
        initiallyInstalled: false,
        childExitCode: aspNetExitCode,
        detectedAfterInstall: aspNetExitCode === 0,
      });
      assert.equal(
        desktop.reboot || aspNet.reboot,
        desktopExitCode === 3010 || aspNetExitCode === 3010,
      );
      assert.equal(desktop.complete && aspNet.complete, true);
    }
  }

  for (const initiallyInstalled of [
    [true, false],
    [false, true],
    [true, true],
  ]) {
    const desktop = componentResult({
      initiallyInstalled: initiallyInstalled[0],
      childExitCode: initiallyInstalled[0] ? undefined : 0,
      detectedAfterInstall: true,
    });
    const aspNet = componentResult({
      initiallyInstalled: initiallyInstalled[1],
      childExitCode: initiallyInstalled[1] ? undefined : 0,
      detectedAfterInstall: true,
    });
    assert.equal(desktop.complete && aspNet.complete, true);
  }
});

test("prerequisites finish before the running app or installation is changed", () => {
  const installSection =
    installer.match(/Section "Voltura Air \(required\)" SEC_CORE(?<body>[\s\S]*?)SectionEnd/u)
      ?.groups?.body ?? "";
  const prerequisitesComplete = installSection.lastIndexOf("Call InstallAspNetCoreRuntime");
  assert.ok(prerequisitesComplete >= 0);
  assert.ok(prerequisitesComplete < installSection.indexOf("Call StopRunningApp"));
  assert.ok(prerequisitesComplete < installSection.indexOf('SetOutPath "$StagingDirectory"'));
  const prerequisitesCompleteInInstaller = installer.indexOf("Call InstallAspNetCoreRuntime");
  assert.ok(prerequisitesCompleteInInstaller < installer.indexOf("CreateShortcut"));
  assert.ok(prerequisitesCompleteInInstaller < installer.indexOf("WriteRegStr"));
});

test("installer journals promotion and removal before changing registration", () => {
  const installSection =
    installer.match(/Section "Voltura Air \(required\)" SEC_CORE([\s\S]*?)SectionEnd/u)?.[1] ?? "";
  const uninstallSection = installer.match(/Section "Uninstall"([\s\S]*?)SectionEnd/u)?.[1] ?? "";
  assert.match(
    installSection,
    /-Action Verify[\s\S]*-Action Promote[\s\S]*--installer-health-check/u,
  );
  assert.match(
    installSection,
    /-Action Verify[\s\S]*SetOutPath "\$PLUGINSDIR"[\s\S]*-Action Promote/u,
  );
  assert.doesNotMatch(installSection, /RMDir \/r "\$INSTDIR"/u);
  assert.ok(
    uninstallSection.indexOf("-Action StageRemoval") <
      uninstallSection.indexOf("DeleteRegKey HKCU"),
  );
  assert.ok(
    uninstallSection.indexOf("-Action CompleteRemoval") <
      uninstallSection.indexOf("DeleteRegKey HKCU"),
  );
  assert.doesNotMatch(uninstallSection, /RMDir \/r "\$INSTDIR"/u);
});

test("cleanup and controlled failures cover every prerequisite stage", () => {
  for (const [installName, cleanupName] of [
    ["InstallWindowsDesktopRuntime", "CleanupWindowsDesktopRuntime"],
    ["InstallAspNetCoreRuntime", "CleanupAspNetCoreRuntime"],
  ]) {
    const body = functionBody(installer, installName);
    assert.ok((body.match(new RegExp(`Call ${cleanupName}`, "gu")) ?? []).length >= 4);
    assert.match(body, /Downloading/u);
    assert.match(body, /Verifying/u);
    assert.match(body, /Requesting elevation/u);
    assert.match(body, /Installing/u);
    assert.match(body, /Installing the .* runtime\.\.\./u);
    assert.match(
      body,
      /SetDetailsPrint listonly\s+DetailPrint "The Microsoft runtime installer can take several minutes\."\s+DetailPrint "Voltura Air setup continues automatically when it finishes\."\s+SetDetailsPrint both/u,
    );
    assert.match(body, /Validating/u);
    assert.match(body, /requires a restart/u);
    assert.match(body, /Controlled failure/u);
  }
  assert.doesNotMatch(installer, /MessageBox[^\r\n]*\$1/u);
});

test("runtime installers launch directly through NSIS elevation and preserve exit codes", () => {
  assert.match(
    installer,
    /!macro ExecElevatedAndWait FILE_PATH PARAMETERS RESULT_VAR[\s\S]*?ShellExecuteEx[\s\S]*?WaitForSingleObject[\s\S]*?GetExitCodeProcess[\s\S]*?!macroend/u,
  );
  assert.match(
    functionBody(installer, "InstallWindowsDesktopRuntime"),
    /!insertmacro ExecElevatedAndWait "\$PLUGINSDIR\\VolturaAir-WindowsDesktop\.exe" "\/install \/quiet \/norestart" \$0/u,
  );
  assert.match(
    functionBody(installer, "InstallAspNetCoreRuntime"),
    /!insertmacro ExecElevatedAndWait "\$PLUGINSDIR\\VolturaAir-AspNetCore\.exe" "\/install \/quiet \/norestart" \$0/u,
  );
  assert.doesNotMatch(installer, /Start-Process[\s\S]*?-Verb RunAs[\s\S]*?-Wait/u);
});

test("restart-required completion defaults to later and cannot launch the app", () => {
  assert.match(installer, /!define MUI_FINISHPAGE_REBOOTLATER_DEFAULT/u);
  assert.match(installer, /SetErrorLevel 3010/u);
  assert.match(installer, /SetErrorLevel 0/u);

  const standard = preprocessInstaller({ frameworkDependent: true });
  const finishPage =
    standard.match(/Function "mui\.FinishPage\.Pre_[^"]+"(?<body>[\s\S]*?)FunctionEnd/u)?.groups
      ?.body ?? "";
  assert.match(finishPage, /IfRebootFlag/u);
  assert.match(finishPage, /MUI_TEXT_FINISH_REBOOTLATER/u);
  assert.match(finishPage, /SendMessage \$mui\.FinishPage\.RebootLater 0x00F1 1 0/u);
  assert.ok(
    finishPage.indexOf("MUI_TEXT_FINISH_REBOOTLATER") < finishPage.indexOf('"Start Voltura Air"'),
  );
});
