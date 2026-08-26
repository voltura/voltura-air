import { spawnSync } from "node:child_process";
import { readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const repositoryRoot = fileURLToPath(new URL("..", import.meta.url));

function run(command, args, options = {}) {
  const resolvedCommand = command === "npm" ? process.execPath : command;
  const resolvedArguments =
    command === "npm"
      ? [join(dirname(process.execPath), "node_modules", "npm", "bin", "npm-cli.js"), ...args]
      : args;
  const result = spawnSync(resolvedCommand, resolvedArguments, {
    cwd: repositoryRoot,
    encoding: "utf8",
    ...options,
  });
  if (result.error) throw result.error;
  return result;
}

const audit = run("npm", ["audit", "--audit-level=moderate", "--json"]);
const auditReport = JSON.parse(audit.stdout || "{}");
if (audit.status !== 0 || (auditReport.metadata?.vulnerabilities?.total ?? 0) !== 0) {
  console.error(audit.stdout || audit.stderr);
  process.exit(1);
}

const outdated = run("npm", ["outdated", "--workspaces", "--include-workspace-root", "--json"]);
const outdatedReport = JSON.parse(outdated.stdout || "{}");
const unexpectedOutdated = Object.entries(outdatedReport).filter(([name, value]) => {
  const entries = Array.isArray(value) ? value : [value];
  const accepted =
    name === "@types/node"
      ? "24.13.3"
      : name === "@cloudflare/workers-types"
        ? "5.20260825.1"
        : null;
  return (
    !accepted || entries.some((entry) => entry.current !== accepted || entry.wanted !== accepted)
  );
});
if (unexpectedOutdated.length > 0) {
  console.error(
    `Unexpected npm updates are available: ${unexpectedOutdated.map(([name]) => name).join(", ")}`,
  );
  process.exit(1);
}

const acceptedUpstreamTestRunnerPackages = new Map([
  ["Microsoft.ApplicationInsights", ["2.23.0", "3.1.2"]],
  ["Microsoft.Bcl.AsyncInterfaces", ["6.0.0", "10.0.11"]],
  ["Microsoft.Testing.Extensions.Telemetry", ["1.9.1", "2.3.3"]],
  ["Microsoft.Testing.Extensions.TrxReport.Abstractions", ["1.9.1", "2.3.3"]],
  ["Microsoft.Testing.Platform", ["1.9.1", "2.3.3"]],
  ["Microsoft.Testing.Platform.MSBuild", ["1.9.1", "2.3.3"]],
  ["System.Numerics.Tensors", ["9.0.0", "10.0.11"]],
]);

for (const mode of ["--outdated", "--vulnerable", "--deprecated"]) {
  const result = run("dotnet", [
    "list",
    "VolturaAir.slnx",
    "package",
    mode,
    "--include-transitive",
    "--format",
    "json",
  ]);
  if (result.status !== 0) {
    process.stderr.write(result.stderr);
    process.exit(result.status || 1);
  }
  const report = JSON.parse(result.stdout || "{}");
  const packages = (report.projects ?? []).flatMap((project) =>
    (project.frameworks ?? []).flatMap((framework) => [
      ...(framework.topLevelPackages ?? []),
      ...(framework.transitivePackages ?? []),
    ]),
  );
  const unexpected =
    mode === "--outdated"
      ? packages.filter((entry) => {
          const accepted = acceptedUpstreamTestRunnerPackages.get(entry.id);
          return (
            !accepted ||
            entry.resolvedVersion !== accepted[0] ||
            entry.latestVersion !== accepted[1]
          );
        })
      : packages;
  if (unexpected.length > 0) {
    console.error(
      `Unexpected NuGet ${mode.slice(2)} findings: ${unexpected.map((entry) => entry.id).join(", ")}`,
    );
    process.exit(1);
  }
}

const source = readFileSync(
  new URL("../apps/windows-host/ThirdPartyNotices/libdatachannel/SOURCE.txt", import.meta.url),
  "utf8",
);
if (!source.includes("OpenSSL 3.6.3") || !source.includes("libdatachannel version: 0.24.5")) {
  console.error(
    "Native dependency provenance must identify libdatachannel 0.24.5 and OpenSSL 3.6.3.",
  );
  process.exit(1);
}

const composition = readFileSync(
  new URL("../services/relay/self-host/compose.yml", import.meta.url),
  "utf8",
);
const requiredImages = [
  "coturn/coturn:4.15.0-r0@sha256:0feee4fc1f45c7c053c8fee3e1ab941b1a1b9a0429bc01e18126735410770bfd",
  "nginx:1.30.4-alpine@sha256:97d490c12ba55b4946b01546d1c3ed324e8d41ab1c9fcb2a616aa470620e5b46",
  "lscr.io/linuxserver/duckdns:af6dcae5-ls86@sha256:ac65c0d1d2cdfd380ab7b450d97b8c80b2a30f2e086bd79b4cce2fc7190d33a6",
];
if (
  requiredImages.some((image) => !composition.includes(image)) ||
  composition.includes(":latest")
) {
  console.error("Self-hosted container images must match the reviewed immutable digest set.");
  process.exit(1);
}

console.log(
  "Dependency check passed: npm and NuGet graphs have no known vulnerable, deprecated, or unexpected outdated packages.",
);
