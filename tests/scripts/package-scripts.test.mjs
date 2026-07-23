import assert from "node:assert/strict";
import { existsSync, readFileSync } from "node:fs";
import test from "node:test";

import { commandDescriptions, findStaleDescriptions, findUndocumentedCommands, formatCommandHelp } from "../../scripts/command-help.mjs";

const packageJson = JSON.parse(readFileSync(new URL("../../package.json", import.meta.url), "utf8"));
const mobilePackageJson = JSON.parse(readFileSync(new URL("../../apps/mobile-web/package.json", import.meta.url), "utf8"));
const releaseWorkflow = readFileSync(new URL("../../scripts/legacy/release.yml", import.meta.url), "utf8");
const packageWindowsScript = readFileSync(new URL("../../scripts/package-win.ps1", import.meta.url), "utf8");
const verifyWindowsVersionScript = readFileSync(new URL("../../scripts/verify-windows-version.ps1", import.meta.url), "utf8");
const prepareReleaseScript = readFileSync(new URL("../../scripts/prepare-release.ps1", import.meta.url), "utf8");
const qualityWorkflow = readFileSync(new URL("../../scripts/legacy/quality.yml", import.meta.url), "utf8");
const devScript = readFileSync(new URL("../../scripts/dev.mjs", import.meta.url), "utf8");
const devHostScript = readFileSync(new URL("../../scripts/dev-host.mjs", import.meta.url), "utf8");
const hostProject = readFileSync(new URL("../../apps/windows-host/VolturaAir.Host.csproj", import.meta.url), "utf8");
const directoryBuildProps = readFileSync(new URL("../../Directory.Build.props", import.meta.url), "utf8");

test("documentation coverage runs in the root and pull-request quality gates", () => {
  assert.equal(packageJson.scripts.test.split(" && ")[0], "npm run docs:check");
  assert.match(qualityWorkflow, /run: npm run docs:check/u);
});

test("every root npm command has a current human-readable description", () => {
  assert.equal(packageJson.scripts.help, "node scripts/command-help.mjs");
  assert.equal(packageJson.scripts["ai:create-shortcut"], undefined);
  assert.equal(packageJson.scripts["ai:init"], "npm run ai:update && npm run ai:schedule:create && npm run ai:shortcut:create");
  assert.equal(packageJson.scripts["ai:schedule"], undefined);
  assert.equal(packageJson.scripts["ai:schedule:create"], "node scripts/ai-schedule.mjs");
  assert.equal(packageJson.scripts["ai:schedule:remove"], "powershell -NoProfile -ExecutionPolicy Bypass -File scripts/remove-chatgpt-codex-schedule.ps1");
  assert.equal(packageJson.scripts["ai:shortcut:create"], "powershell -NoProfile -ExecutionPolicy Bypass -File scripts/create-chatgpt-codex-shortcut.ps1");
  assert.equal(packageJson.scripts["ai:shortcut:remove"], "powershell -NoProfile -ExecutionPolicy Bypass -File scripts/remove-chatgpt-codex-shortcut.ps1");
  assert.equal(packageJson.scripts["ai:update"], "powershell -NoProfile -ExecutionPolicy Bypass -File scripts/update-chatgpt-codex.ps1");
  assert.match(commandDescriptions["ai:schedule:create"], /--time HH:mm:ss/u);
  assert.match(commandDescriptions["ai:update"], /ChatGPT\/Codex/u);
  assert.deepEqual(findUndocumentedCommands(packageJson.scripts), []);
  assert.deepEqual(findStaleDescriptions(packageJson.scripts), []);
  assert.match(commandDescriptions.dev, /development loop/u);
});

test("GitHub Actions stay archived but can be restored deliberately", () => {
  assert.equal(packageJson.scripts["actions:restore"], "node scripts/restore-github-actions.mjs");
  assert.equal(packageJson.scripts["release:draft"], "node scripts/release-publish.mjs");
  assert.equal(packageJson.scripts["release:full"], "node scripts/release-publish.mjs --publish-latest");
  assert.equal(packageJson.scripts["release:sync-release-notes"], "node scripts/sync-release-notes.mjs");
  assert.equal(existsSync(new URL("../../.github/workflows/release.yml", import.meta.url)), false);
  assert.equal(existsSync(new URL("../../.github/workflows/quality.yml", import.meta.url)), false);
});

test("help filters root npm commands by a case-insensitive name fragment", () => {
  const aiHelp = formatCommandHelp(packageJson.scripts, "ai:");
  const checkHelp = formatCommandHelp(packageJson.scripts, "check");

  assert.match(aiHelp, /ai:update/u);
  assert.match(aiHelp, /Purpose:/u);
  assert.match(aiHelp, /Runs:/u);
  assert.doesNotMatch(aiHelp, /branch:sync/u);
  assert.match(checkHelp, /size:check/u);
  assert.match(checkHelp, /ui:tokens:check/u);
  assert.doesNotMatch(checkHelp, /branch:sync/u);
});

test("strong source-size warnings require current reviews in pull-request quality", () => {
  assert.equal(packageJson.scripts["size:check"], "node scripts/report-source-sizes.mjs --check");
  assert.match(qualityWorkflow, /run: npm run size:check/u);
});

test("host partial ownership runs in root and pull-request quality gates", () => {
  assert.equal(packageJson.scripts["host:ownership:check"], "node scripts/check-host-partial-ownership.mjs");
  assert.match(packageJson.scripts.test, /npm run host:ownership:check/u);
  assert.match(qualityWorkflow, /run: npm run host:ownership:check/u);
});

test("the production mobile build enforces its measured JavaScript budget", () => {
  assert.match(mobilePackageJson.scripts.build, /vite build && npm run bundle:check/u);
  assert.equal(mobilePackageJson.scripts["bundle:check"], "node ../../scripts/check-mobile-bundle-size.mjs");
});

test("quick phone development rebuilds the host-served client without validation", () => {
  assert.equal(packageJson.scripts["dev:quick"], "node scripts/dev.mjs --quick");
  assert.equal(mobilePackageJson.scripts["build:quick"], "vite build");
  assert.match(devScript, /process\.argv\.includes\("--quick"\)/u);
  assert.match(devScript, /childEnv\.VOLTURA_AIR_USE_VITE_CLIENT = "0"/u);
  assert.match(devScript, /childEnv\.VOLTURA_AIR_SKIP_CURSOR_WATCHDOG_BUILD = "1"/u);
  assert.match(devScript, /delete childEnv\.VOLTURA_AIR_CLIENT_URL/u);
  assert.match(devScript, /if \(quickStart\)[\s\S]*spawnCommand\("npm", \["run", "build:quick"/u);
  assert.doesNotMatch(devScript, /if \(quickStart\)[\s\S]*runCommand\("npm", \["run", "build:quick"/u);
  assert.match(devScript, /persistentChildren\.push\(spawnCommand\("npm", \["run", "dev:host"\]/u);
  assert.match(devScript, /if \(!quickStart\)[\s\S]*vite\.js/u);
  assert.match(devHostScript, /if \(useViteClient\)[\s\S]*else \{\s*await waitForClientFiles\(\)/u);
  assert.match(devHostScript, /VOLTURA_AIR_SKIP_CURSOR_WATCHDOG_BUILD/u);
  assert.match(devHostScript, /args\.push\("-p:SkipCursorWatchdogBuild=true"\)/u);
  assert.match(devHostScript, /stopExistingHost\(\)[\s\S]*startHost\(\)/u);
  assert.doesNotMatch(devHostScript, /"--no-build"/u);
  assert.doesNotMatch(devHostScript, /--isolated-test-mode/u);
  assert.doesNotMatch(devHostScript, /IntermediateOutputPath/u);
  assert.match(directoryBuildProps, /Condition="'\$\(DesignTimeBuild\)' != 'true' AND '\$\(BuildingInsideVisualStudio\)' != 'true' AND '\$\(Configuration\)' != 'Release'"/u);
  assert.match(directoryBuildProps, /<Configuration Condition="'\$\(Configuration\)' == ''">Debug<\/Configuration>/u);
  assert.match(directoryBuildProps, /<OutputPath>bin\\cli\\\$\(Configuration\)\\<\/OutputPath>/u);
  assert.match(directoryBuildProps, /<IntermediateOutputPath>obj\\cli\\\$\(Configuration\)\\<\/IntermediateOutputPath>/u);
  assert.match(releaseWorkflow, /npm run package:win -- -Version \$env:RELEASE_VERSION -Runtime \$env:RUNTIME/u);
  assert.match(hostProject, /!\$\(MSBuildProjectFile\.EndsWith\('_wpftmp\.csproj'\)\)/u);
  assert.match(hostProject, /'\$\(SkipCursorWatchdogBuild\)' != 'true' OR !Exists\('\$\(OutDir\)VolturaAir\.CursorWatchdog\.exe'\)/u);
});

test("full maintenance stops the host before deleting locked build outputs", () => {
  assert.equal(packageJson.scripts.clean, undefined);

  const steps = packageJson.scripts["maintenance:full"].split(" && ");

  assert.equal(steps[0], "npm run cache:purge");
  assert.ok(steps.indexOf("npm run cache:purge") < steps.indexOf("npm run clean:temp"));
  assert.ok(steps.indexOf("npm run clean:temp") < steps.indexOf("npm run clean:git"));
  assert.ok(steps.indexOf("npm run clean:git") < steps.indexOf("npm run deps:update"));
});

test("release publication rejects existing tags and asset replacement", () => {
  assert.match(releaseWorkflow, /Reject an existing release tag/u);
  assert.match(releaseWorkflow, /"release", "create"/u);
  assert.match(releaseWorkflow, /--prerelease/u);
  assert.doesNotMatch(releaseWorkflow, /force-with-lease|tag --force|--clobber|release upload/u);
});

test("release publication derives its inputs from the root package version", () => {
  assert.match(releaseWorkflow, /workflow_dispatch:\s*\n\s+push:/u);
  assert.match(releaseWorkflow, /paths:\s*\n\s+- package\.json/u);
  assert.match(releaseWorkflow, /previousVersion -eq \$version/u);
  assert.match(releaseWorkflow, /should_publish/u);
  assert.match(releaseWorkflow, /tag = "v\$version"/u);
  assert.match(releaseWorkflow, /runtime=win-x64/u);
  assert.match(releaseWorkflow, /group: release-\$\{\{ needs\.resolve-release\.outputs\.tag \}\}/u);
  assert.doesNotMatch(releaseWorkflow, /inputs\.(release_tag|version|runtime)/u);
  assert.doesNotMatch(releaseWorkflow, /workflow_dispatch:\s*\n\s+inputs:/u);
});

test("release packaging retains the standard Release build outputs", () => {
  assert.equal(packageWindowsScript.match(/dotnet publish apps\/windows-host\/VolturaAir\.Host\.csproj/gu)?.length, 2);
  assert.equal(packageWindowsScript.match(/-c Release/gu)?.length, 2);
  assert.match(packageWindowsScript, /-o \$publishDir/u);
  assert.match(packageWindowsScript, /-o \$frameworkDependentPublishDir/u);
  assert.match(verifyWindowsVersionScript, /apps\\windows-host\\bin\\Release/u);
  assert.match(directoryBuildProps, /'\$\(Configuration\)' != 'Release'/u);
});

test("release preparation synchronizes version-bearing files without editing the workflow", () => {
  assert.match(prepareReleaseScript, /\$rootPackagePath = 'package\.json'/u);
  assert.match(prepareReleaseScript, /\$mobilePackagePath = 'apps\\mobile-web\\package\.json'/u);
  assert.match(prepareReleaseScript, /\$packageLockPath = 'package-lock\.json'/u);
  assert.match(prepareReleaseScript, /\$hostProjectPath = 'apps\\windows-host\\VolturaAir\.Host\.csproj'/u);
  assert.doesNotMatch(prepareReleaseScript, /releaseWorkflowPath|release\.yml/u);
});
