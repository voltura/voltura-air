import { readFile, writeFile } from "node:fs/promises";
import { createHash } from "node:crypto";
import path from "node:path";
import process from "node:process";
import { fileURLToPath } from "node:url";

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const outputPath = path.join(repoRoot, "apps", "mobile-web", "public", "third-party-notices.txt");
const checkOnly = process.argv.includes("--check");
const nativeBinarySha256 = "88cba93015800e9c33dd0824d68629a5ef8c1d5f50d4fdd836a2c8df69d94e1b";

const components = [
  {
    name: "@noble/curves",
    version: "2.3.0",
    license: "MIT",
    source: "https://github.com/paulmillr/noble-curves",
  },
  {
    name: "@noble/hashes",
    version: "2.3.0",
    license: "MIT",
    source: "https://github.com/paulmillr/noble-hashes",
  },
  {
    name: "jsqr",
    version: "1.4.0",
    license: "Apache-2.0",
    source: "https://github.com/cozmo/jsQR",
  },
  {
    name: "lucide-react",
    version: "1.34.0",
    license: "ISC and MIT",
    source: "https://github.com/lucide-icons/lucide",
  },
  { name: "react", version: "19.2.8", license: "MIT", source: "https://github.com/facebook/react" },
  {
    name: "react-dom",
    version: "19.2.8",
    license: "MIT",
    source: "https://github.com/facebook/react",
  },
  {
    name: "scheduler",
    version: "0.27.0",
    license: "MIT",
    source: "https://github.com/facebook/react",
  },
];

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
  const packageDirectory = path.join(repoRoot, "node_modules", ...component.name.split("/"));
  const packageJson = JSON.parse(
    await readFile(path.join(packageDirectory, "package.json"), "utf8"),
  );
  if (packageJson.version !== component.version) {
    throw new Error(
      `${component.name} notice expects ${component.version}, but package-lock installed ${packageJson.version}.`,
    );
  }

  const licenseText = (await readFile(path.join(packageDirectory, "LICENSE"), "utf8")).trim();
  sections.push(
    "-".repeat(72),
    `${component.name} ${component.version}`,
    `License: ${component.license}`,
    `Source: ${component.source}`,
    "-".repeat(72),
    "",
    licenseText,
    "",
  );
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
    "Microsoft WebView2 SDK | 1.0.4129.50",
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
    ["Microsoft.Web.WebView2", "1.0.4129.50"],
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
