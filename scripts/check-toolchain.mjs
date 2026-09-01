import { execFileSync } from "node:child_process";
import { existsSync, readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const repositoryRoot = fileURLToPath(new URL("..", import.meta.url));
const failures = [];

function versionParts(value) {
  const match = /^v?(\d+)\.(\d+)(?:\.(\d+))?/u.exec(value.trim());
  return match ? [Number(match[1]), Number(match[2]), Number(match[3] ?? 0)] : null;
}

function commandVersion(command, args = []) {
  try {
    const resolvedCommand = command === "npm" ? process.execPath : command;
    const resolvedArguments =
      command === "npm"
        ? [
            process.env.npm_execpath ??
              join(dirname(process.execPath), "node_modules", "npm", "bin", "npm-cli.js"),
            ...args,
          ]
        : args;
    return execFileSync(resolvedCommand, resolvedArguments, {
      cwd: repositoryRoot,
      encoding: "utf8",
      stdio: ["ignore", "pipe", "pipe"],
    }).trim();
  } catch (error) {
    failures.push(`${command} could not be executed: ${error.stderr?.trim() || error.message}`);
    return "";
  }
}

function requireVersion(label, actual, expected, comparison = "exact") {
  const parts = versionParts(actual);
  if (!parts) {
    failures.push(`${label} returned an unrecognized version: ${actual || "<empty>"}`);
    return;
  }
  const [major, minor, patch] = parts;
  const [expectedMajor, expectedMinor, expectedPatch] = expected;
  const valid =
    comparison === "major-minor"
      ? major === expectedMajor && minor === expectedMinor
      : comparison === "minimum"
        ? major * 1_000_000 + minor * 1_000 + patch >=
          expectedMajor * 1_000_000 + expectedMinor * 1_000 + expectedPatch
        : major === expectedMajor && minor === expectedMinor && patch === expectedPatch;
  const expectation =
    comparison === "major-minor"
      ? `${expected.join(".")}.x`
      : comparison === "minimum"
        ? `${expected.join(".")} or newer`
        : expected.join(".");
  if (!valid) failures.push(`${label} ${actual} does not satisfy ${expectation}.`);
}

requireVersion("Node.js", process.versions.node, [24, 20, 0]);
requireVersion("npm", commandVersion("npm", ["--version"]), [12, 0, 2]);
requireVersion(".NET SDK", commandVersion("dotnet", ["--version"]), [10, 0, 400]);
const dotnetRuntimes = commandVersion("dotnet", ["--list-runtimes"]);
for (const runtime of [
  "Microsoft.AspNetCore.App",
  "Microsoft.NETCore.App",
  "Microsoft.WindowsDesktop.App",
]) {
  if (!dotnetRuntimes.split(/\r?\n/u).some((line) => line.startsWith(`${runtime} 10.0.11 `))) {
    failures.push(`${runtime} 10.0.11 is required by the pinned build SDK.`);
  }
}
requireVersion(
  "PowerShell",
  commandVersion("pwsh", ["-NoProfile", "-Command", "$PSVersionTable.PSVersion.ToString()"]),
  [7, 6, 4],
);
requireVersion("PHP", commandVersion("php", ["-r", "echo PHP_VERSION;"]), [8, 5, 9], "minimum");

const globalJson = JSON.parse(readFileSync(join(repositoryRoot, "global.json"), "utf8"));
if (
  globalJson.sdk?.version !== "10.0.400" ||
  globalJson.sdk?.rollForward !== "latestPatch" ||
  globalJson.sdk?.allowPrerelease !== false
) {
  failures.push("global.json must pin the supported .NET 10.0.400 feature band without previews.");
}

const packageJson = JSON.parse(readFileSync(join(repositoryRoot, "package.json"), "utf8"));
if (
  packageJson.packageManager !== "npm@12.0.2" ||
  packageJson.engines?.node !== ">=24.20.0 <25" ||
  packageJson.engines?.npm !== "12.0.2"
) {
  failures.push("package.json must declare the Node 24 LTS and npm 12.0.2 toolchain contract.");
}

const dockerfile = readFileSync(join(repositoryRoot, "services", "relay", "Dockerfile"), "utf8");
if (
  !dockerfile.startsWith(
    "FROM node:24.20.0-alpine@sha256:e67514e5d0f6c46656005e1b693b2ec9d52e80b641307de684d4a015ba7a4eaf AS build\n",
  ) ||
  !dockerfile.includes("npm@12.0.2") ||
  !dockerfile.includes("npm ci --workspace")
) {
  failures.push(
    "The relay Dockerfile is not aligned with the Node/npm contract and locked install.",
  );
}

const nsisCandidates = [
  join(process.env.ProgramFiles || "", "NSIS", "makensis.exe"),
  join(process.env["ProgramFiles(x86)"] || "", "NSIS", "makensis.exe"),
];
const nsis = nsisCandidates.find(existsSync);
if (!nsis) {
  failures.push("NSIS makensis.exe was not found under Program Files.");
} else {
  requireVersion("NSIS", commandVersion(nsis, ["/VERSION"]), [3, 12, 0], "minimum");
}

const vswhere = join(
  process.env["ProgramFiles(x86)"] || "",
  "Microsoft Visual Studio",
  "Installer",
  "vswhere.exe",
);
if (!existsSync(vswhere)) {
  failures.push("Visual Studio Installer vswhere.exe was not found.");
} else {
  const installationVersion = commandVersion(vswhere, [
    "-latest",
    "-products",
    "*",
    "-version",
    "[18.9,19.0)",
    "-requires",
    "Microsoft.VisualStudio.Component.VC.Tools.x86.x64",
    "-property",
    "installationVersion",
  ]);
  if (!installationVersion) {
    failures.push(
      "Visual Studio 2026 18.9 or newer with the Desktop development with C++ workload is required; found none.",
    );
  } else {
    requireVersion("Visual Studio 2026", installationVersion, [18, 9, 0], "minimum");
  }
}

if (failures.length > 0) {
  console.error(`Toolchain check failed:\n- ${failures.join("\n- ")}`);
  process.exit(1);
}

console.log(
  "Toolchain check passed: Node 24.20.0, npm 12.0.2, .NET SDK 10.0.400/runtime 10.0.11, PowerShell 7.6.4, PHP 8.5.9+, Visual Studio 2026 18.9+, and NSIS.",
);
