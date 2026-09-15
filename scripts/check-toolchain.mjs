import { execFileSync } from "node:child_process";
import { existsSync, readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";
import { supportsToolVersion, toolVersionExpectation } from "./toolchain-versions.mjs";

const repositoryRoot = fileURLToPath(new URL("..", import.meta.url));
const failures = [];
const toolchain = JSON.parse(readFileSync(join(repositoryRoot, "scripts/toolchain.json"), "utf8"));
const requirement = (name) => toolchain.tools[name];
const version = (name) => requirement(name).minimum.split(".").map(Number);
const policy = (name) => requirement(name).policy;

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
  if (!supportsToolVersion(actual, expected, comparison)) {
    failures.push(
      `${label} ${actual || "<empty>"} does not satisfy ${toolVersionExpectation(expected, comparison)}.`,
    );
  }
}
requireVersion("Node.js", process.versions.node, version("node"), policy("node"));
requireVersion("npm", commandVersion("npm", ["--version"]), version("npm"), policy("npm"));
requireVersion(
  ".NET SDK",
  commandVersion("dotnet", ["--version"]),
  version("dotnet"),
  policy("dotnet"),
);
const dotnetRuntimes = commandVersion("dotnet", ["--list-runtimes"]);
for (const runtime of [
  "Microsoft.AspNetCore.App",
  "Microsoft.NETCore.App",
  "Microsoft.WindowsDesktop.App",
]) {
  if (
    !dotnetRuntimes
      .split(/\r?\n/u)
      .some(
        (line) =>
          line.startsWith(`${runtime} `) &&
          supportsToolVersion(line.split(" ")[1], version("runtime"), policy("runtime")),
      )
  ) {
    failures.push(`${runtime} 10.0.11 or a newer 10.0 patch is required.`);
  }
}
requireVersion(
  "PowerShell",
  commandVersion("pwsh", ["-NoProfile", "-Command", "$PSVersionTable.PSVersion.ToString()"]),
  version("powershell"),
  policy("powershell"),
);
requireVersion(
  "PHP",
  commandVersion("php", ["-r", "echo PHP_VERSION;"]),
  version("php"),
  policy("php"),
);

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
  packageJson.engines?.npm !== ">=12.0.2 <12.1"
) {
  failures.push(
    "package.json must declare the Node 24 LTS and npm 12.0 patch-line toolchain contract.",
  );
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
  requireVersion("NSIS", commandVersion(nsis, ["/VERSION"]), version("nsis"), policy("nsis"));
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
    toolchain.visualStudio.range,
    "-requires",
    ...toolchain.visualStudio.components.filter((id) => !id.includes(".Workload.")),
    "-property",
    "installationVersion",
  ]);
  if (!installationVersion) {
    failures.push(
      "Visual Studio 2026 18.9 or newer with the Desktop development with C++ workload is required; found none.",
    );
  } else {
    requireVersion(
      "Visual Studio 2026",
      installationVersion,
      version("visualStudio"),
      policy("visualStudio"),
    );
  }
}

if (failures.length > 0) {
  console.error(`Toolchain check failed:\n- ${failures.join("\n- ")}`);
  process.exit(1);
}

console.log(
  "Toolchain check passed: Node 24 LTS (24.20.0+), npm 12.0.2+, .NET SDK 10.0.4xx/runtime 10.0.11+, PowerShell 7.6 LTS (7.6.6+), PHP 8.5.9+, Visual Studio 2026 18.9+, and NSIS.",
);
