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
    failures.push(
      `${runtime} ${toolVersionExpectation(version("runtime"), policy("runtime"))} is required.`,
    );
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
  globalJson.sdk?.version !== requirement("dotnet").minimum ||
  globalJson.sdk?.rollForward !== "disable" ||
  globalJson.sdk?.allowPrerelease !== false
) {
  failures.push(
    `global.json must pin SDK ${requirement("dotnet").minimum} without roll-forward or previews.`,
  );
}

const packageJson = JSON.parse(readFileSync(join(repositoryRoot, "package.json"), "utf8"));
const [nodeMajor] = version("node");
const [npmMajor, npmMinor] = version("npm");
const nodeMinimum = requirement("node").minimum;
const npmMinimum = requirement("npm").minimum;
if (
  packageJson.packageManager !== `npm@${npmMinimum}` ||
  packageJson.engines?.node !== `>=${nodeMinimum} <${nodeMajor + 1}` ||
  packageJson.engines?.npm !== `>=${npmMinimum} <${npmMajor}.${npmMinor + 1}`
) {
  failures.push("package.json must match the Node and npm versions in scripts/toolchain.json.");
}

const dockerfile = readFileSync(join(repositoryRoot, "services", "relay", "Dockerfile"), "utf8");
if (
  !/^sha256:[a-f0-9]{64}$/u.test(toolchain.relayNodeImageDigest) ||
  dockerfile.split(/\r?\n/u, 1)[0] !==
    `FROM node:${nodeMinimum}-alpine@${toolchain.relayNodeImageDigest} AS build` ||
  !dockerfile.includes(`npm@${npmMinimum}`) ||
  !dockerfile.includes("npm ci --omit=dev --workspace")
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
  `Toolchain check passed: Node ${nodeMinimum} LTS, npm ${npmMinimum}, .NET SDK ${requirement("dotnet").minimum}, runtime ${requirement("runtime").minimum}, PowerShell ${requirement("powershell").minimum} LTS, PHP ${requirement("php").minimum}, Visual Studio 2026, and NSIS.`,
);
