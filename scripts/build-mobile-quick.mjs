import { spawnSync } from "node:child_process";
import { createHash, randomUUID } from "node:crypto";
import {
  existsSync,
  mkdirSync,
  readdirSync,
  readFileSync,
  statSync,
  writeFileSync
} from "node:fs";
import { dirname, join, relative, resolve, sep } from "node:path";
import { fileURLToPath } from "node:url";

export const MOBILE_QUICK_BUILD_STATE_SCHEMA_VERSION = 1;

const scriptPath = fileURLToPath(import.meta.url);
const defaultRepositoryRoot = resolve(dirname(scriptPath), "..");

const fixedInputPaths = [
  "package.json",
  "package-lock.json",
  "scripts/build-mobile-quick.mjs",
  "apps/mobile-web/index.html",
  "apps/mobile-web/package.json",
  "apps/mobile-web/tsconfig.json",
  "apps/mobile-web/tsconfig.node.json",
  "apps/mobile-web/vite.config.ts",
  "apps/windows-host/relay-service.json",
  "assets/branding/apple-startup-devices.json"
];

export function getMobileQuickBuildPaths(repositoryRoot, environment = process.env) {
  const isHosted = environment.VOLTURA_AIR_HOSTED === "1";
  const hostedChannel = environment.VOLTURA_AIR_HOSTED_CHANNEL === "development" ? "development" : "stable";
  const outputDirectory = isHosted
    ? join(repositoryRoot, "apps", "public-site", hostedChannel === "development" ? "dev-app" : "app")
    : join(repositoryRoot, "apps", "mobile-web", "dist");
  const stateName = isHosted
    ? `dev-quick-${hostedChannel}-hosted.json`
    : "dev-quick-mobile.json";

  return {
    outputDirectory,
    statePath: join(repositoryRoot, "artifacts", "obj", "mobile-web", stateName)
  };
}

export function collectFiles(directory) {
  if (!existsSync(directory)) {
    return [];
  }

  const files = [];
  const entries = readdirSync(directory, { withFileTypes: true }).sort((left, right) => left.name.localeCompare(right.name));
  for (const entry of entries) {
    const entryPath = join(directory, entry.name);
    if (entry.isDirectory()) {
      files.push(...collectFiles(entryPath));
    } else if (entry.isFile()) {
      files.push(entryPath);
    }
  }
  return files;
}

export function collectMobileQuickInputFiles(repositoryRoot) {
  const mobileRoot = join(repositoryRoot, "apps", "mobile-web");
  const inputFiles = fixedInputPaths.map((inputPath) => join(repositoryRoot, ...inputPath.split("/")));

  for (const directory of [join(mobileRoot, "src"), join(mobileRoot, "public")]) {
    inputFiles.push(...collectFiles(directory));
  }

  if (existsSync(mobileRoot)) {
    for (const entry of readdirSync(mobileRoot, { withFileTypes: true })) {
      if (entry.isFile() && entry.name.startsWith(".env")) {
        inputFiles.push(join(mobileRoot, entry.name));
      }
    }
  }

  const uniqueFiles = new Map(inputFiles.map((inputPath) => [resolve(inputPath), inputPath]));
  return [...uniqueFiles.keys()].sort((left, right) => {
    const leftRelative = relative(repositoryRoot, left).split(sep).join("/");
    const rightRelative = relative(repositoryRoot, right).split(sep).join("/");
    return leftRelative.localeCompare(rightRelative);
  });
}

export function createFileFingerprint(filePaths, baseDirectory) {
  const hash = createHash("sha256");
  const sortedFiles = [...new Set(filePaths.map((filePath) => resolve(filePath)))].sort((left, right) => {
    const leftRelative = relative(baseDirectory, left).split(sep).join("/");
    const rightRelative = relative(baseDirectory, right).split(sep).join("/");
    return leftRelative.localeCompare(rightRelative);
  });

  for (const filePath of sortedFiles) {
    const relativePath = relative(baseDirectory, filePath).split(sep).join("/");
    hash.update(relativePath);
    hash.update("\0");

    if (!existsSync(filePath)) {
      hash.update("missing");
      hash.update("\0");
      continue;
    }

    const stats = statSync(filePath);
    if (!stats.isFile()) {
      hash.update("not-a-file");
      hash.update("\0");
      continue;
    }

    hash.update(readFileSync(filePath));
    hash.update("\0");
  }

  return hash.digest("hex");
}

export function createMobileQuickInputFingerprint(repositoryRoot, environment = process.env) {
  const fileFingerprint = createFileFingerprint(collectMobileQuickInputFiles(repositoryRoot), repositoryRoot);
  const hostedValue = environment.VOLTURA_AIR_HOSTED === "1" ? "1" : "0";
  const hostedChannel = environment.VOLTURA_AIR_HOSTED_CHANNEL === "development" ? "development" : "stable";
  const hash = createHash("sha256");
  hash.update(fileFingerprint);
  hash.update("\0");
  hash.update(`VOLTURA_AIR_HOSTED=${hostedValue}`);
  hash.update("\0");
  hash.update(`VOLTURA_AIR_HOSTED_CHANNEL=${hostedChannel}`);
  return hash.digest("hex");
}

export function createOutputFingerprint(outputDirectory) {
  const outputFiles = collectFiles(outputDirectory);
  return outputFiles.length > 0 ? createFileFingerprint(outputFiles, outputDirectory) : null;
}

export function isCurrentMobileQuickBuild(state, inputFingerprint, outputFingerprint, requestedBuildId = "") {
  const normalizedRequestedBuildId = requestedBuildId.trim();
  return state?.schemaVersion === MOBILE_QUICK_BUILD_STATE_SCHEMA_VERSION
    && state.inputFingerprint === inputFingerprint
    && state.outputFingerprint === outputFingerprint
    && typeof state.webBuildId === "string"
    && state.webBuildId.length > 0
    && (normalizedRequestedBuildId.length === 0 || state.webBuildId === normalizedRequestedBuildId);
}

export function readGeneratedWebBuildId(outputDirectory) {
  const webBuildId = readFileSync(join(outputDirectory, "web-build-id.txt"), "utf8").trim();
  if (webBuildId.length === 0) {
    throw new Error(`The mobile build did not produce a web build ID in ${outputDirectory}.`);
  }
  return webBuildId;
}

function readBuildState(statePath) {
  try {
    return JSON.parse(readFileSync(statePath, "utf8"));
  } catch {
    return null;
  }
}

function writeBuildState(statePath, state) {
  mkdirSync(dirname(statePath), { recursive: true });
  writeFileSync(statePath, `${JSON.stringify(state, null, 2)}\n`, "utf8");
}

function runViteBuild(repositoryRoot, outputDirectory, statePath, inputFingerprint, requestedBuildId) {
  const webBuildId = requestedBuildId || randomUUID();
  const viteCli = join(repositoryRoot, "node_modules", "vite", "bin", "vite.js");
  const result = spawnSync(
    process.execPath,
    [viteCli, "build"],
    {
      cwd: join(repositoryRoot, "apps", "mobile-web"),
      env: { ...process.env, VOLTURA_AIR_WEB_BUILD_ID: webBuildId },
      stdio: "inherit",
      windowsHide: false
    }
  );

  if (result.error) {
    throw new Error(`Failed to start the mobile Vite build: ${result.error.message}`);
  }
  if (result.signal) {
    process.exitCode = 1;
    return;
  }
  if (result.status !== 0) {
    process.exitCode = result.status ?? 1;
    return;
  }

  const generatedWebBuildId = readGeneratedWebBuildId(outputDirectory);
  const outputFingerprint = createOutputFingerprint(outputDirectory);
  if (outputFingerprint === null) {
    throw new Error(`The mobile build produced no files in ${outputDirectory}.`);
  }

  writeBuildState(statePath, {
    schemaVersion: MOBILE_QUICK_BUILD_STATE_SCHEMA_VERSION,
    inputFingerprint,
    outputFingerprint,
    webBuildId: generatedWebBuildId
  });
}

function main() {
  const repositoryRoot = defaultRepositoryRoot;
  const environment = process.env;
  const { outputDirectory, statePath } = getMobileQuickBuildPaths(repositoryRoot, environment);
  const inputFingerprint = createMobileQuickInputFingerprint(repositoryRoot, environment);
  const requestedBuildId = environment.VOLTURA_AIR_WEB_BUILD_ID?.trim() ?? "";
  const state = readBuildState(statePath);

  if (state?.inputFingerprint === inputFingerprint && (!requestedBuildId || state.webBuildId === requestedBuildId)) {
    const outputFingerprint = createOutputFingerprint(outputDirectory);
    if (isCurrentMobileQuickBuild(state, inputFingerprint, outputFingerprint, requestedBuildId)) {
      console.log("Mobile client is up to date; skipping Vite build.");
      return;
    }
  }

  runViteBuild(repositoryRoot, outputDirectory, statePath, inputFingerprint, requestedBuildId);
}

if (process.argv[1] && resolve(process.argv[1]) === scriptPath) {
  try {
    main();
  } catch (error) {
    console.error(`Mobile quick build failed: ${error instanceof Error ? error.message : String(error)}`);
    process.exitCode = 1;
  }
}
