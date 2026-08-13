import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const read = async (path) => readFile(new URL(`../../${path}`, import.meta.url), "utf8");

const [
  setup,
  frameGenerator,
  nativeBuild,
  packaging,
  installer,
  preferences,
  mainWindow,
  todo
] = await Promise.all([
  read("apps/windows-host/Features/PhoneWebcam/Native/SetupHelper/main.cpp"),
  read("apps/windows-host/Features/PhoneWebcam/Native/VirtualCameraMediaSource/SimpleFrameGenerator.cpp"),
  read("scripts/build-phone-webcam-native.ps1"),
  read("scripts/package-win.ps1"),
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
  assert.match(frameGenerator, /GetTickCount64\(\) - m_latestArrival > 500/u);
  assert.match(frameGenerator, /FrameWidth = 1920/u);
  assert.match(frameGenerator, /FrameHeight = 1080/u);
});

test("production setup is idempotent on removal and retains transactional fault boundaries", () => {
  assert.match(setup, /InstalledDirectoryName\[\] = L"Voltura Air Webcam"/u);
  assert.doesNotMatch(setup, /Voltura Air Webcam Spike/u);
  assert.match(setup, /VOLTURA_WEBCAM_FAULT/u);
  assert.match(setup, /state=camera-absent/u);
  assert.match(setup, /state=remove-rolled-back/u);
  assert.match(setup, /cleanup-required/u);
  assert.match(setup, /cleanupRequired/u);
  assert.match(setup, /FileMatchesPackagedSource/u);
  assert.match(setup, /updateRequired/u);
  assert.match(setup, /InstallSystemFilesElevated\(arguments\[2\]\)/u);
  assert.doesNotMatch(setup, /BuiltSourceDll/u);
  assert.match(setup, /FindResourceW[\s\S]+RT_RCDATA/u);
  assert.match(setup, /CreateFileW\([\s\S]+executable\.c_str\(\), GENERIC_READ, FILE_SHARE_READ/u);
  assert.match(setup, /WritePackagedSource\(installedDll\)/u);
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

test("uninstall removes Phone webcam before deleting its recovery helper", () => {
  const uninstall = installer.match(/Section "Uninstall"(?<body>[\s\S]*?)SectionEnd/u)?.groups?.body ?? "";
  assert.match(uninstall, /Call un\.RemovePhoneWebcam/u);
  assert.ok(uninstall.indexOf("Call un.RemovePhoneWebcam") < uninstall.indexOf('RMDir /r "$INSTDIR"'));
  const removal = installer.match(/Function un\.RemovePhoneWebcam(?<body>[\s\S]*?)FunctionEnd/u)?.groups?.body ?? "";
  assert.match(installer, /!define WEBCAM_SETUP "PhoneWebcam\\VolturaAir\.WebcamSetup\.exe"/u);
  assert.match(removal, /\$\{WEBCAM_SETUP\}/u);
  assert.match(removal, /cleanup-required/u);
  assert.match(removal, /IfFileExists[^\n]+phone_webcam_helper_available 0/u);
  assert.match(removal, /cleanup component is missing[\s\S]+Abort/u);
  assert.ok(removal.indexOf("cleanup-required") < removal.indexOf('remove\''));
  assert.match(removal, /Abort "Phone webcam removal did not complete\."/u);
});
