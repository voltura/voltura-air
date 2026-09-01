import { readdir, readFile, writeFile } from "node:fs/promises";
import { createHash } from "node:crypto";
import path from "node:path";
import process from "node:process";
import { fileURLToPath } from "node:url";

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const outputPath = path.join(repoRoot, "apps", "mobile-web", "public", "third-party-notices.txt");
const checkOnly = process.argv.includes("--check");
const nativeBinarySha256 = "88cba93015800e9c33dd0824d68629a5ef8c1d5f50d4fdd836a2c8df69d94e1b";

const components = await collectMobileProductionComponents();

const sections = [
  "VOLTURA AIR MOBILE WEB THIRD-PARTY SOFTWARE NOTICES",
  "====================================================",
  "",
  "Voltura Air gratefully acknowledges the authors and contributors of the",
  "software below. No listed project or contributor endorses or is affiliated",
  "with Voltura Air or Voltura AB. Each component is provided under its own",
  "license and warranty disclaimer.",
  "",
];

for (const component of components) {
  sections.push(
    "-".repeat(72),
    `${component.name} ${component.version}`,
    `License: ${component.license}`,
    `Source: ${component.source}`,
    "-".repeat(72),
    "",
    component.licenseText,
    "",
  );
}

async function collectMobileProductionComponents() {
  const workspaceDirectory = path.join(repoRoot, "apps", "mobile-web");
  const workspacePackage = JSON.parse(
    await readFile(path.join(workspaceDirectory, "package.json"), "utf8"),
  );
  const pending = Object.keys(workspacePackage.dependencies ?? {}).map((name) => ({
    name,
    fromDirectory: workspaceDirectory,
  }));
  const visited = new Set();
  const discovered = [];

  while (pending.length > 0) {
    const request = pending.pop();
    const packageDirectory = await resolvePackageDirectory(request.name, request.fromDirectory);
    if (packageDirectory === null || visited.has(packageDirectory)) continue;
    visited.add(packageDirectory);
    const packageJson = JSON.parse(
      await readFile(path.join(packageDirectory, "package.json"), "utf8"),
    );
    const licenseFiles = (await readdir(packageDirectory, { withFileTypes: true }))
      .filter((entry) => entry.isFile() && /^(?:licen[cs]e|copying)(?:[.-].*)?$/iu.test(entry.name))
      .map((entry) => entry.name)
      .sort((left, right) => left.localeCompare(right));
    if (licenseFiles.length === 0) {
      throw new Error(`${packageJson.name} ${packageJson.version} has no installed license text.`);
    }
    const licenseText = (
      await Promise.all(
        licenseFiles.map(async (fileName) =>
          (await readFile(path.join(packageDirectory, fileName), "utf8")).trim(),
        ),
      )
    )
      .filter((text, index, all) => text && all.indexOf(text) === index)
      .join("\n\n");
    discovered.push({
      name: packageJson.name,
      version: packageJson.version,
      license: packageJson.license ?? "See included license text",
      source: packageSource(packageJson),
      licenseText,
    });
    const runtimeDependencies = {
      ...(packageJson.dependencies ?? {}),
      ...(packageJson.optionalDependencies ?? {}),
      ...Object.fromEntries(
        Object.entries(packageJson.peerDependencies ?? {}).filter(
          ([name]) => packageJson.peerDependenciesMeta?.[name]?.optional !== true,
        ),
      ),
    };
    for (const name of Object.keys(runtimeDependencies)) {
      pending.push({ name, fromDirectory: packageDirectory });
    }
  }

  return discovered.sort(
    (left, right) =>
      left.name.localeCompare(right.name) || left.version.localeCompare(right.version),
  );
}

async function resolvePackageDirectory(name, fromDirectory) {
  let current = fromDirectory;
  while (true) {
    const candidate = path.join(current, "node_modules", ...name.split("/"));
    try {
      await readFile(path.join(candidate, "package.json"), "utf8");
      return candidate;
    } catch (error) {
      if (error?.code !== "ENOENT") throw error;
    }
    const parent = path.dirname(current);
    if (parent === current) return null;
    current = parent;
  }
}

function packageSource(packageJson) {
  const repository =
    typeof packageJson.repository === "string"
      ? packageJson.repository
      : packageJson.repository?.url;
  if (typeof repository === "string") {
    const normalized = repository
      .replace(/^git\+/u, "")
      .replace(/^git:\/\//u, "https://")
      .replace(/\.git$/u, "");
    return /^[\w.-]+\/[\w.-]+$/u.test(normalized) ? `https://github.com/${normalized}` : normalized;
  }
  return typeof packageJson.homepage === "string"
    ? packageJson.homepage
    : `https://www.npmjs.com/package/${packageJson.name}`;
}

const generated = `${sections.join("\n").trimEnd()}\n`;
if (checkOnly) {
  const existing = await readFile(outputPath, "utf8");
  if (existing !== generated) {
    throw new Error("Mobile third-party notices are stale. Run npm run third-party:generate.");
  }
  await verifyMaintainedInventory();
} else {
  await writeFile(outputPath, generated, "utf8");
  console.log(`Generated ${path.relative(repoRoot, outputPath)}`);
}

async function verifyMaintainedInventory() {
  const inventoryPath = path.join(repoRoot, "THIRD-PARTY-NOTICES.md");
  const inventory = await readFile(inventoryPath, "utf8");
  const hostProject = await readFile(
    path.join(repoRoot, "apps", "windows-host", "VolturaAir.Host.csproj"),
    "utf8",
  );
  const centralPackages = await readFile(path.join(repoRoot, "Directory.Packages.props"), "utf8");
  const relayPackage = JSON.parse(
    await readFile(path.join(repoRoot, "services", "relay", "package.json"), "utf8"),
  );
  const requiredInventoryText = [
    "libdatachannel 0.24.5",
    "OpenSSL 3.6.3",
    "Microsoft WebView2 SDK | 1.0.4191.47",
    "Markdig | 1.3.2",
    "QRCoder | 1.8.0",
    "Vortice.Windows | 3.8.3",
    "Vortice.Mathematics | 2.1.1",
    "SharpGen.Runtime and SharpGen.Runtime.COM | 2.4.2-beta",
    "Concentus | 2.2.2",
    "NAudio.Wasapi and NAudio.Core | 3.0.1",
    "`ws` 8.21.3",
  ];
  for (const expected of requiredInventoryText) {
    if (!inventory.includes(expected)) {
      throw new Error(`Third-party inventory is missing exact runtime record: ${expected}`);
    }
  }

  const directHostPackages = [
    ["Microsoft.Web.WebView2", "1.0.4191.47"],
    ["Markdig", "1.3.2"],
    ["QRCoder", "1.8.0"],
    ["Vortice.Direct3D11", "3.8.3"],
    ["Vortice.D3DCompiler", "3.8.3"],
    ["Vortice.MediaFoundation", "3.8.3"],
    ["Vortice.Mathematics", "2.1.1"],
    ["Concentus", "2.2.2"],
    ["NAudio.Wasapi", "3.0.1"],
  ];
  for (const [name, version] of directHostPackages) {
    const escapedName = name.replaceAll(".", "\\.");
    const escapedVersion = version.replaceAll(".", "\\.");
    if (
      !new RegExp(`PackageReference Include="${escapedName}"`, "u").test(hostProject) ||
      !new RegExp(`PackageVersion Include="${escapedName}" Version="${escapedVersion}"`, "u").test(
        centralPackages,
      )
    ) {
      throw new Error(
        `Host dependency ${name} ${version} no longer matches the maintained third-party inventory.`,
      );
    }
  }

  const installedWsPackage = JSON.parse(
    await readFile(path.join(repoRoot, "node_modules", "ws", "package.json"), "utf8"),
  );
  if (installedWsPackage.version !== "8.21.3" || relayPackage.dependencies?.ws !== "^8.21.3") {
    throw new Error("Relay ws dependency no longer matches the maintained third-party inventory.");
  }

  const nativeBinary = await readFile(
    path.join(repoRoot, "apps", "windows-host", "Native", "win-x64", "datachannel.dll"),
  );
  const actualSha256 = createHash("sha256").update(nativeBinary).digest("hex");
  if (actualSha256 !== nativeBinarySha256) {
    throw new Error("datachannel.dll changed without an updated source, hash, and notice record.");
  }

  const requiredNoticePaths = [
    ["apps", "windows-host", "ThirdPartyNotices", "README.txt"],
    ["apps", "windows-host", "ThirdPartyNotices", "libdatachannel", "SOURCE.txt"],
    ["apps", "windows-host", "ThirdPartyNotices", "managed", "Microsoft.Web.WebView2-LICENSE.txt"],
    ["apps", "windows-host", "ThirdPartyNotices", "managed", "Microsoft.Web.WebView2-NOTICE.txt"],
    ["apps", "windows-host", "ThirdPartyNotices", "managed", "Markdig-LICENSE.txt"],
    ["apps", "windows-host", "ThirdPartyNotices", "managed", "QRCoder-LICENSE.txt"],
    ["apps", "windows-host", "ThirdPartyNotices", "managed", "Vortice-SharpGen-LICENSE.txt"],
    ["apps", "windows-host", "ThirdPartyNotices", "managed", "Concentus-LICENSE.txt"],
    ["apps", "windows-host", "ThirdPartyNotices", "managed", "NAudio-LICENSE.txt"],
  ];
  for (const segments of requiredNoticePaths) {
    const contents = await readFile(path.join(repoRoot, ...segments), "utf8");
    if (!contents.trim()) {
      throw new Error(`Third-party notice is empty: ${segments.join("/")}`);
    }
  }
}
