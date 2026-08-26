import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const read = (path) => readFileSync(new URL(`../../${path}`, import.meta.url), "utf8");
const json = (path) => JSON.parse(read(path));

test("repository toolchains are pinned to supported stable releases", () => {
  const root = json("package.json");
  const dotnet = json("global.json");
  const dockerfile = read("services/relay/Dockerfile");

  assert.equal(root.packageManager, "npm@11.19.0");
  assert.deepEqual(root.engines, { node: ">=24.19.0 <25", npm: "11.19.0" });
  assert.deepEqual(dotnet.sdk, {
    version: "10.0.400",
    rollForward: "latestPatch",
    allowPrerelease: false,
  });
  assert.match(read("scripts/check-toolchain.mjs"), /\[18, 9, 0\], "minimum"/u);
  assert.match(dockerfile, /^FROM node:24\.19\.0-alpine@sha256:[a-f0-9]{64} AS build$/mu);
  assert.match(dockerfile, /npm install --global npm@11\.19\.0/u);
  assert.match(dockerfile, /npm ci --workspace/u);
});

test("language toolchains retain their intentional compatibility boundaries", () => {
  const mobile = json("apps/mobile-web/package.json");
  const cursorBuild = read("scripts/build-cursor-watchdog.ps1");
  const compatibility = json("scripts/powershell-compatibility.json");
  const hostPreflight = read("scripts/host-preflight.ps1");

  assert.equal(mobile.devDependencies.typescript, "7.0.2");
  assert.equal(json("services/relay/package.json").devDependencies.typescript, "7.0.2");
  assert.equal(json("package.json").devDependencies.typescript, "7.0.2");
  assert.match(cursorBuild, /\/std:c17 \/analyze \/O2 \/MT \/W4 \/WX/u);
  assert.doesNotMatch(cursorBuild, /\/std:clatest|\/std:c23/u);
  assert.ok(compatibility.windowsPowerShell51.includes("build-cursor-watchdog.ps1"));
  assert.ok(compatibility.powerShell76.includes("package-win.ps1"));
  assert.match(hostPreflight, /Refusing to stop VolturaAir\.Host process/u);
  assert.match(hostPreflight, /Wait-Process -Id \$hostProcess\.Id -Timeout 10/u);
});

test("self-hosted images use reviewed version tags", () => {
  const composition = read("services/relay/self-host/compose.yml");
  assert.match(composition, /coturn\/coturn:4\.15\.0-r0/u);
  assert.match(composition, /nginx:1\.30\.4-alpine/u);
  assert.match(composition, /duckdns:af6dcae5-ls86@sha256:[a-f0-9]{64}/u);
  assert.equal((composition.match(/@sha256:[a-f0-9]{64}/gu) ?? []).length, 3);
  assert.doesNotMatch(composition, /:latest/u);
  assert.doesNotMatch(composition, /node:26|nginx:1\.29|coturn\/coturn:4\.7/u);
});
