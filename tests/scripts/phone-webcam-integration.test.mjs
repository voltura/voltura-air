import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const read = async (path) => readFile(new URL(`../../${path}`, import.meta.url), "utf8");

const [
  setup,
  frameGenerator,
  nativeBuild,
  packaging,
  hostProject,
  installer,
  preferences,
  mainWindow,
  todo
] = await Promise.all([
  read("apps/windows-host/Features/PhoneWebcam/Native/SetupHelper/main.cpp"),
  read("apps/windows-host/Features/PhoneWebcam/Native/VirtualCameraMediaSource/SimpleFrameGenerator.cpp"),
  read("scripts/build-phone-webcam-native.ps1"),
  read("scripts/package-win.ps1"),
  read("apps/windows-host/VolturaAir.Host.csproj"),
  read("installer/VolturaAir.nsi"),
  read("apps/windows-host/Features/Preferences/PreferencesPageView.xaml"),
  read("apps/windows-host/MainWindow.xaml"),
  read("docs/todo.md")
]);

test("Phone webcam is a normal app setting and not a Developer-mode gate", () => {
  assert.match(mainWindow, /Content="Phone webcam"/u);
  assert.doesNotMatch(preferences, /Header="Phone webcam"/u);
  assert.match(todo, /normal app feature from its first production build/u);
  assert.doesNotMatch(todo, /feature-owned toggle under \*\*Developer tools\*\*/u);
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
  assert.doesNotMatch(setup, /StopService|FrameServer.*Stop/u);
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
  assert.match(installer, /!define WEBCAM_PROTECTED_SETUP "\$PROGRAMFILES64\\Voltura Air Webcam\\VolturaAir\.WebcamSetup\.exe"/u);
  assert.doesNotMatch(removal, /\$INSTDIR\\\$\{WEBCAM_SETUP\}/u);
  assert.match(removal, /\$\{WEBCAM_PROTECTED_SETUP\}/u);
  assert.match(removal, /cleanup-required/u);
  assert.match(removal, /IfFileExists[^\n]+phone_webcam_helper_available phone_webcam_done/u);
  assert.ok(removal.indexOf("cleanup-required") < removal.indexOf('remove\''));
  assert.match(removal, /Abort "Phone webcam removal did not complete\."/u);
});

test("installer-owned Phone Webcam maintenance restores the prior component and app on failure", () => {
  const install = installer.match(/Section \/o "Phone Webcam"(?<body>[\s\S]*?)SectionEnd/u)?.groups?.body ?? "";
  assert.match(install, /CopyFiles \/SILENT "\$\{WEBCAM_PROTECTED_SETUP\}" "\$WebcamRollbackHelper"/u);
  assert.match(install, /"\$WebcamRollbackHelper" install/u);
  assert.match(install, /Call RollbackPromotedInstall/u);
  assert.ok(install.indexOf('"$WebcamRollbackHelper" install') < install.lastIndexOf("Call RollbackPromotedInstall"));
});
