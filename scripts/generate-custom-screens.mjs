import { spawnSync } from "node:child_process";
import { mkdir, mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import process from "node:process";
import { fileURLToPath } from "node:url";
import { officialScreens, packageFilenames } from "./custom-screens/catalog.mjs";
import { stableJson, validateDefinition } from "./custom-screens/builders/validation.mjs";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const outputDirectory = path.join(root, "artifacts", "custom-screens", "official");
const bundlePath = path.join(root, "artifacts", "custom-screens", "voltura-official-screens.zip");
const checkOnly = process.argv.includes("--check");
const createBundle = process.argv.includes("--official");
const crcTable = Array.from({ length: 256 }, (_, index) => {
  let value = index;
  for (let bit = 0; bit < 8; bit += 1) value = (value & 1) ? (value >>> 1) ^ 0xedb88320 : value >>> 1;
  return value >>> 0;
});

const outputs = buildOutputs();
await validateWithHost(outputs);
if (checkOnly) {
  process.stdout.write(`Validated ${officialScreens.length} deterministic official custom screens.\n`);
} else {
  await mkdir(outputDirectory, { recursive: true });
  for (const [name, bytes] of outputs) {
    await writeIfChanged(path.join(outputDirectory, name), bytes);
  }
  if (createBundle) {
    await mkdir(path.dirname(bundlePath), { recursive: true });
    await writeIfChanged(bundlePath, createZip(outputs));
  }
  process.stdout.write(`Generated ${officialScreens.length} official custom screens${createBundle ? " and the catalog bundle" : ""}.\n`);
}

function buildOutputs() {
  const seenIds = new Set();
  const outputs = new Map();
  const catalog = [];
  for (const definition of officialScreens) {
    validateDefinition(definition);
    if (seenIds.has(definition.screen.id)) throw new Error(`Duplicate official ID ${definition.screen.id}.`);
    seenIds.add(definition.screen.id);
    const filename = packageFilenames.get(definition.screen.id);
    if (!filename) throw new Error(`No package filename for ${definition.screen.id}.`);
    const packageValue = { packageVersion: 1, format: "voltura-air.custom-screen", screen: definition.screen };
    const first = Buffer.from(stableJson(packageValue));
    const second = Buffer.from(stableJson(packageValue));
    if (!first.equals(second)) throw new Error(`Nondeterministic package output for ${definition.screen.id}.`);
    outputs.set(filename, first);
    catalog.push({ ...definition.metadata, packageFilename: filename });
  }
  outputs.set("catalog.json", Buffer.from(stableJson({ catalogVersion: 1, screens: catalog })));
  return outputs;
}

async function validateWithHost(outputs) {
  const directory = await mkdtemp(path.join(os.tmpdir(), "voltura-screens-"));
  try {
    for (const [name, bytes] of outputs) {
      await writeFile(path.join(directory, name), bytes);
    }
    const result = spawnSync(
      "dotnet",
      [
        "test",
        "tests/VolturaAir.Host.Tests/VolturaAir.Host.Tests.csproj",
        "--configuration",
        "Release",
        "--filter",
        "FullyQualifiedName=VolturaAir.Host.Tests.OfficialCustomScreenPackageTests.GeneratedCatalogPassesTheRealPackageReaderAndPortableContract"
      ],
      {
        cwd: root,
        encoding: "utf8",
        env: { ...process.env, VOLTURA_OFFICIAL_SCREEN_DIRECTORY: directory }
      });
    if (result.status !== 0) {
      throw new Error(`The host rejected the generated Custom Screen catalog.\n${result.stdout}${result.stderr}`);
    }
  } finally {
    await rm(directory, { recursive: true, force: true });
  }
}

async function writeIfChanged(filePath, bytes) {
  try {
    if ((await readFile(filePath)).equals(bytes)) return;
  } catch {
    // The output does not exist yet.
  }
  await writeFile(filePath, bytes);
}

function createZip(entries) {
  const localParts = [];
  const centralParts = [];
  let offset = 0;
  for (const [name, data] of [...entries].sort(([left], [right]) => left.localeCompare(right))) {
    const filename = Buffer.from(name, "utf8");
    const crc = crc32(data);
    const local = Buffer.alloc(30);
    local.writeUInt32LE(0x04034b50, 0);
    local.writeUInt16LE(20, 4);
    local.writeUInt16LE(0x0800, 6);
    local.writeUInt16LE(0, 8);
    local.writeUInt16LE(0, 10);
    local.writeUInt16LE(0x5021, 12);
    local.writeUInt32LE(crc, 14);
    local.writeUInt32LE(data.length, 18);
    local.writeUInt32LE(data.length, 22);
    local.writeUInt16LE(filename.length, 26);
    localParts.push(local, filename, data);

    const central = Buffer.alloc(46);
    central.writeUInt32LE(0x02014b50, 0);
    central.writeUInt16LE(20, 4);
    central.writeUInt16LE(20, 6);
    central.writeUInt16LE(0x0800, 8);
    central.writeUInt16LE(0, 10);
    central.writeUInt16LE(0, 12);
    central.writeUInt16LE(0x5021, 14);
    central.writeUInt32LE(crc, 16);
    central.writeUInt32LE(data.length, 20);
    central.writeUInt32LE(data.length, 24);
    central.writeUInt16LE(filename.length, 28);
    central.writeUInt32LE(offset, 42);
    centralParts.push(central, filename);
    offset += local.length + filename.length + data.length;
  }
  const centralDirectory = Buffer.concat(centralParts);
  const end = Buffer.alloc(22);
  end.writeUInt32LE(0x06054b50, 0);
  end.writeUInt16LE(entries.size, 8);
  end.writeUInt16LE(entries.size, 10);
  end.writeUInt32LE(centralDirectory.length, 12);
  end.writeUInt32LE(offset, 16);
  return Buffer.concat([...localParts, centralDirectory, end]);
}

function crc32(bytes) {
  let value = 0xffffffff;
  for (const byte of bytes) value = (value >>> 8) ^ crcTable[(value ^ byte) & 0xff];
  return (value ^ 0xffffffff) >>> 0;
}
