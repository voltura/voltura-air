import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const read = async (path) => readFile(new URL(`../../${path}`, import.meta.url), "utf8");

const [
  setup,
  setupProject,
  frameGenerator,
  nativeBuild,
  packaging,
  hostProject,
  installer,
  preferences,
  mainWindow
] = await Promise.all([
  read("apps/windows-host/Features/PhoneWebcam/Native/SetupHelper/main.cpp"),
  read("apps/windows-host/Features/PhoneWebcam/Native/SetupHelper/SetupHelper.vcxproj"),
  read("apps/windows-host/Features/PhoneWebcam/Native/VirtualCameraMediaSource/SimpleFrameGenerator.cpp"),
  read("scripts/build-phone-webcam-native.ps1"),
  read("scripts/package-win.ps1"),
  read("apps/windows-host/VolturaAir.Host.csproj"),
  read("installer/VolturaAir.nsi"),
  read("apps/windows-host/Features/Preferences/PreferencesPageView.xaml"),
  read("apps/windows-host/MainWindow.xaml")
]);

test("Phone webcam is a normal app setting and not a Developer-mode gate", () => {
  assert.match(mainWindow, /Content="Phone webcam"/u);
  assert.doesNotMatch(preferences, /Header="Phone webcam"/u);
});

test("installer offers the official VB-CABLE page only after confirmed absence", () => {
  const finish = installer.match(/Function ConfigureVbCableFinishOption(?<body>[\s\S]*?)FunctionEnd/u)?.groups?.body ?? "";
  assert.match(installer, /!define VB_CABLE_URL "https:\/\/vb-audio\.com\/Cable\/"/u);
  assert.match(installer, /MUI_FINISHPAGE_SHOWREADME_NOTCHECKED/u);
  assert.match(finish, /SectionGetFlags \$\{SEC_PHONE_WEBCAM\}/u);
  assert.ok(
    installer.indexOf("Section /o \"Phone Webcam\" SEC_PHONE_WEBCAM") < installer.indexOf("Function ConfigureVbCableFinishOption"),
    "the section index must be declared before the finish-page function uses it"
  );
  assert.match(finish, /--phone-microphone-status/u);
  assert.match(finish, /ExecToStack \/TIMEOUT=8000/u);
  assert.match(finish, /\$0 != 20/u);
  assert.match(finish, /ShowWindow \$mui\.FinishPage\.ShowReadme \$\{SW_HIDE\}/u);
  assert.doesNotMatch(installer, /File[^\n]+VB-CABLE/iu);
  assert.doesNotMatch(installer, /URLDownloadToFile[^\n]+vb-audio/iu);
});

test("Windows Modify reuses the existing installer for Phone Webcam on and off", () => {
  const init = installer.match(/Function \.onInit(?<body>[\s\S]*?)FunctionEnd/u)?.groups?.body ?? "";
  const finalize = installer.match(/Section -FinalizeInstall(?<body>[\s\S]*?)SectionEnd/u)?.groups?.body ?? "";
  const removal = installer.match(/Section -ApplyPhoneWebcamRemoval(?<body>[\s\S]*?)SectionEnd/u)?.groups?.body ?? "";
  const uninstall = installer.match(/Section "Uninstall"(?<body>[\s\S]*?)SectionEnd/u)?.groups?.body ?? "";
  assert.match(installer, /!define MAINTENANCE_INSTALLER "\$LOCALAPPDATA\\Voltura Air\\VolturaAir-Modify\.exe"/u);
  assert.match(init, /Call ConfigureInstalledWebcamSelection/u);
  assert.match(installer, /Function ConfigureInstalledWebcamSelection[\s\S]*SectionSetFlags \$\{SEC_PHONE_WEBCAM\} \$\{SF_SELECTED\}[\s\S]*SectionSetFlags \$\{SEC_PHONE_WEBCAM\} 0/u);
  assert.match(installer, /Section -PrepareMaintenanceEntry[\s\S]*Call PrepareMaintenanceInstaller/u);
  assert.match(installer, /CopyFiles \/SILENT "\$EXEPATH" "\$MaintenanceInstallerPending"/u);
  assert.match(installer, /MoveFileExW\(w "\$MaintenanceInstallerPending", w "\$\{MAINTENANCE_INSTALLER\}", i 9\)/u);
  assert.match(finalize, /Call PromoteMaintenanceInstaller[\s\S]*WriteRegStr HKCU "\$\{UNINSTALL_KEY\}" "ModifyPath" "\$\\"\$\{MAINTENANCE_INSTALLER\}\$\\""/u);
  assert.match(finalize, /Call DeletePreparedMaintenanceArtifacts[\s\S]*Call RestorePhoneWebcamAfterFailure[\s\S]*Call RollbackPromotedInstall/u);
  assert.match(finalize, /Call RestorePhoneWebcamAfterFailure[\s\S]*Call RollbackPromotedInstall/u);
  assert.match(finalize, /WriteRegDWORD HKCU "\$\{UNINSTALL_KEY\}" "NoModify" 0/u);
  assert.match(finalize, /WriteRegDWORD HKCU "\$\{UNINSTALL_KEY\}" "NoRepair" 1/u);
  assert.match(removal, /SectionGetFlags \$\{SEC_PHONE_WEBCAM\}[\s\S]*cleanup-required[\s\S]*VolturaAir\.WebcamSetup\.exe" remove/u);
  assert.match(uninstall, /\.removing-main[\s\S]*Rename "\$\{MAINTENANCE_INSTALLER\}" "\$4"[\s\S]*Delete \/REBOOTOK "\$4"/u);
  assert.match(uninstall, /\.removing-main[\s\S]*\.removing-pending[\s\S]*\.removing-rollback/u);
  assert.match(uninstall, /uninstall_drain_maintenance:[\s\S]*Delete \/REBOOTOK "\$4"[\s\S]*IfFileExists "\$\{MAINTENANCE_INSTALLER\}" uninstall_maintenance_cleanup_failed/u);
  assert.ok(uninstall.indexOf("uninstall_maintenance_cleanup_ready:") < uninstall.indexOf("-Action CompleteRemoval"));
  assert.ok(uninstall.indexOf("-Action CompleteRemoval") < uninstall.indexOf('DeleteRegKey HKCU "${UNINSTALL_KEY}"'));
});

test("native camera keeps the reviewed bounded version-one frame contract", () => {
  assert.match(frameGenerator, /VolturaAirWebcam-v1/u);
  assert.match(frameGenerator, /sequence == 0 \|\| sequence <= m_latestSequence/u);
  assert.match(frameGenerator, /memcmp\(header, "VAWC", 4\) == 0/u);
  assert.doesNotMatch(frameGenerator, /m_latestArrival/u);
  assert.match(frameGenerator, /FrameWidth = 1920/u);
  assert.match(frameGenerator, /FrameHeight = 1080/u);
});

test("production setup is idempotent on removal and retains transactional fault boundaries", () => {
  assert.match(setup, /InstalledDirectoryName\[\] = L"Voltura Air Webcam"/u);
  assert.doesNotMatch(setup, /Voltura Air Webcam Spike/u);
  assert.match(setup, /VOLTURA_WEBCAM_FAULT/u);
  assert.match(setup, /if \(!g_allowFaultInjection\) return false/u);
  assert.doesNotMatch(setup, /WaitForSingleObject\(info\.hProcess, INFINITE\)/u);
  assert.match(setup, /ElevatedOperationTimeoutMilliseconds/u);
  assert.match(setup, /VerifyRegisteredOwner\(ownerSid\)/u);
  assert.match(setup, /state=camera-absent/u);
  assert.match(setup, /state=remove-rolled-back/u);
  assert.match(setup, /cleanup-required/u);
  assert.match(setup, /cleanupRequired/u);
  assert.match(setup, /FileMatchesPackagedSource/u);
  assert.match(setup, /updateRequired/u);
  assert.match(setup, /InstallSystemFilesElevated\(arguments\[2\]\)/u);
  assert.doesNotMatch(setup, /BuiltSourceDll/u);
  assert.match(setup, /FindResourceW[\s\S]+RT_RCDATA/u);
  assert.match(setup, /executable\.c_str\(\), GENERIC_READ, FILE_SHARE_READ \| FILE_SHARE_DELETE/u);
  assert.match(setup, /FileReleaseRetryMilliseconds = 10 \* 1000/u);
  assert.match(setup, /MoveReleasedFile\(installedDll, stagedDll\)/u);
  assert.match(setup, /StopCameraServicesForRemoval\(\)/u);
  assert.match(setup, /L"FrameServerMonitor"/u);
  assert.match(setup, /L"FrameServer"/u);
  assert.match(setup, /ControlService\(service, SERVICE_CONTROL_STOP/u);
  assert.match(setup, /ServiceStopTimeoutMilliseconds/u);
  assert.match(setup, /DeleteReleasedFile\(stagedDll\)/u);
  assert.match(setup, /MOVEFILE_DELAY_UNTIL_REBOOT/u);
  assert.match(setup, /state=helper-removal-deferred/u);
  assert.match(setup, /WritePackagedSource\(installedDll\)/u);
  assert.match(setup, /RejectReparsePointIfPresent\(installDirectory\)/u);
  assert.match(setup, /FILE_ATTRIBUTE_REPARSE_POINT/u);
  assert.doesNotMatch(setup, /CopyFileW\([^\n]*sourceDll/u);
  assert.match(setup, /verify-packaged-source/u);
  assert.match(nativeBuild, /security-test-backup/u);
  assert.match(nativeBuild, /WriteAllBytes\(\$mediaSourcePath/u);
  assert.match(setupProject, /<SubSystem>Windows<\/SubSystem>/u);
  assert.match(setupProject, /<EntryPointSymbol>wmainCRTStartup<\/EntryPointSymbol>/u);
  assert.match(nativeBuild, /setup helper must use the Windows GUI subsystem/u);
  assert.match(nativeBuild, /windowless Phone webcam setup helper/u);
  assert.match(nativeBuild, /function Invoke-WindowlessSetupHelper/u);
  assert.match(nativeBuild, /UseShellExecute = \$false/u);
  assert.match(nativeBuild, /CreateNoWindow = \$true/u);
  assert.match(nativeBuild, /RedirectStandardOutput = \$true/u);
  assert.match(nativeBuild, /Invoke-WindowlessSetupHelper[\s\S]*verify-packaged-source/u);
  assert.match(setup, /info\.nShow = SW_HIDE/u);
});

test("Windows packages require the complete Phone webcam payload", () => {
  assert.match(packaging, /build-phone-webcam-native\.ps1/u);
  assert.match(packaging, /PhoneWebcam\\VolturaAir\.WebcamSetup\.exe/u);
  assert.match(packaging, /PhoneWebcam\\MICROSOFT-WINDOWS-CAMERA-LICENSE\.txt/u);
  assert.doesNotMatch(packaging, /Copy-Item[^\n]+VirtualCameraMediaSource\.dll/u);
});

test("development host builds include the embedded Phone webcam setup helper", () => {
  assert.match(hostProject, /BuildPhoneWebcamDevelopmentPayload/u);
  assert.match(hostProject, /'\$\(Configuration\)' != 'Release'/u);
  assert.match(hostProject, /Inputs="[^"]*build-phone-webcam-native\.ps1;@\(PhoneWebcamNativeBuildInput\)"/u);
  assert.match(hostProject, /Outputs="\$\(OutDir\)PhoneWebcam\\VolturaAir\.WebcamSetup\.exe"/u);
  assert.match(hostProject, /build-phone-webcam-native\.ps1/u);
  assert.match(nativeBuild, /MediaSourceOutput/u);
  assert.match(nativeBuild, /legacyExternalPayload/u);
});

test("uninstall removes Phone webcam before deleting its recovery helper", () => {
  const uninstall = installer.match(/Section "Uninstall"(?<body>[\s\S]*?)SectionEnd/u)?.groups?.body ?? "";
  assert.match(uninstall, /Call un\.RemovePhoneWebcam/u);
  assert.ok(uninstall.indexOf("Call un.RemovePhoneWebcam") < uninstall.indexOf("-Action StageRemoval"));
  const removal = installer.match(/Function un\.RemovePhoneWebcam(?<body>[\s\S]*?)FunctionEnd/u)?.groups?.body ?? "";
  const uninstallInit = installer.match(/Function un\.onInit(?<body>[\s\S]*?)FunctionEnd/u)?.groups?.body ?? "";
  assert.match(installer, /!define WEBCAM_PROTECTED_SETUP "\$PROGRAMFILES64\\Voltura Air Webcam\\VolturaAir\.WebcamSetup\.exe"/u);
  assert.doesNotMatch(removal, /\$INSTDIR\\\$\{WEBCAM_SETUP\}/u);
  assert.match(uninstallInit, /File \/oname=VolturaAir\.WebcamSetup\.exe/u);
  assert.match(removal, /"\$PLUGINSDIR\\VolturaAir\.WebcamSetup\.exe" cleanup-required/u);
  assert.match(removal, /"\$PLUGINSDIR\\VolturaAir\.WebcamSetup\.exe" remove/u);
  assert.doesNotMatch(removal, /"\$\{WEBCAM_PROTECTED_SETUP\}" (?:cleanup-required|remove)/u);
  assert.match(removal, /cleanup-required/u);
  assert.doesNotMatch(removal, /IfFileExists "\$\{WEBCAM_PROTECTED_SETUP\}"/u);
  assert.ok(removal.indexOf("cleanup-required") < removal.indexOf('remove\''));
  assert.match(removal, /Abort "Phone webcam removal did not complete\."/u);
});

test("installer-owned Phone Webcam maintenance restores the prior component and app on failure", () => {
  const install = installer.match(/Section \/o "Phone Webcam"(?<body>[\s\S]*?)SectionEnd/u)?.groups?.body ?? "";
  const restore = installer.match(/Function RestorePhoneWebcamAfterFailure(?<body>[\s\S]*?)FunctionEnd/u)?.groups?.body ?? "";
  assert.ok(
    install.indexOf("File /oname=VolturaAir.WebcamSetup.exe") <
      install.indexOf('"$PLUGINSDIR\\VolturaAir.WebcamSetup.exe" cleanup-required')
  );
  assert.match(install, /"\$PLUGINSDIR\\VolturaAir\.WebcamSetup\.exe" cleanup-required/u);
  assert.match(install, /"\$PLUGINSDIR\\VolturaAir\.WebcamSetup\.exe" remove/u);
  assert.doesNotMatch(install, /"\$\{WEBCAM_PROTECTED_SETUP\}" (?:cleanup-required|remove)/u);
  assert.match(install, /CopyFiles \/SILENT "\$\{WEBCAM_PROTECTED_SETUP\}" "\$WebcamRollbackHelper"/u);
  assert.match(install, /Call RestorePhoneWebcamAfterFailure/u);
  assert.match(restore, /\$WebcamRollbackAvailable == 1[\s\S]*"\$WebcamRollbackHelper" install/u);
  assert.match(install, /Call RollbackPromotedInstall/u);
  assert.ok(install.indexOf("Call RestorePhoneWebcamAfterFailure") < install.lastIndexOf("Call RollbackPromotedInstall"));
});
