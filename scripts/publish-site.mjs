import { spawnSync } from "node:child_process";
import { existsSync } from "node:fs";
import { access, mkdir, readFile, rm, writeFile } from "node:fs/promises";
import path from "node:path";
import process from "node:process";
import { fileURLToPath } from "node:url";
import SftpClient from "ssh2-sftp-client";

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const sourceDirectory = path.join(repositoryRoot, "docs", "site");
const host = "ssh.voltura.se";
const port = 22;
const username = "voltura.se";
const remoteDirectory = "air";

const protectPasswordCommand = `
$request = [Console]::In.ReadToEnd() | ConvertFrom-Json
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $request.path) | Out-Null
$secure = ConvertTo-SecureString -String $request.password -AsPlainText -Force
[System.IO.File]::WriteAllText($request.path, (ConvertFrom-SecureString -SecureString $secure))
`;

const unprotectPasswordCommand = `
$credentialPath = [Console]::In.ReadToEnd()
$ciphertext = [System.IO.File]::ReadAllText($credentialPath).Trim()
$secure = ConvertTo-SecureString -String $ciphertext
$credential = New-Object System.Management.Automation.PSCredential("Voltura Air site publishing", $secure)
[Console]::Out.Write($credential.GetNetworkCredential().Password)
`;

export function getSitePublishPaths(environment = process.env) {
  if (process.platform !== "win32") {
    throw new Error("Site password storage is only supported on Windows.");
  }
  if (!environment.LOCALAPPDATA) {
    throw new Error("Windows did not provide LOCALAPPDATA for site password storage.");
  }

  const storageDirectory = path.join(environment.LOCALAPPDATA, "Voltura Air");
  return {
    credentialPath: path.join(storageDirectory, "site-publish-sftp-password.dpapi"),
    hostFingerprintPath: path.join(storageDirectory, "site-publish-sftp-host.sha256")
  };
}

export function runPowerShell(command, input, options = {}) {
  const systemRoot = options.systemRoot ?? process.env.SystemRoot;
  const run = options.run ?? spawnSync;
  if (!systemRoot) {
    throw new Error("Windows did not provide SystemRoot for protected password storage.");
  }

  const result = run(
    path.join(systemRoot, "System32", "WindowsPowerShell", "v1.0", "powershell.exe"),
    ["-NoProfile", "-NonInteractive", "-Command", command],
    { encoding: "utf8", input, windowsHide: true }
  );
  if (result.error || result.status !== 0) {
    throw new Error("Windows could not access the protected site publishing password.");
  }
  return result.stdout;
}

export function trimClipboardPassword(value) {
  return value.replace(/\r?\n$/u, "");
}

function readClipboardPassword() {
  return trimClipboardPassword(runPowerShell("Get-Clipboard -Raw", ""));
}

async function promptForPassword() {
  if (!process.stdin.isTTY || !process.stdout.isTTY) {
    throw new Error("Run npm run publish:site:password from an interactive terminal first.");
  }

  process.stdout.write("one.com SFTP password (Ctrl+V pastes): ");
  process.stdin.setRawMode(true);
  process.stdin.resume();
  process.stdin.setEncoding("utf8");

  return new Promise((resolve, reject) => {
    let password = "";
    const finish = (error) => {
      process.stdin.off("data", onData);
      process.stdin.setRawMode(false);
      process.stdin.pause();
      process.stdout.write("\n");
      if (error) {
        reject(error);
      } else {
        resolve(password);
      }
    };
    const onData = (input) => {
      if (input === "\u0016") {
        password += readClipboardPassword();
      } else if (input === "\u0003") {
        finish(new Error("Password setup cancelled."));
      } else if (input === "\r" || input === "\n") {
        finish();
      } else if (input === "\u0008" || input === "\u007f") {
        password = password.slice(0, -1);
      } else {
        password += input;
      }
    };
    process.stdin.on("data", onData);
  });
}

export async function storePassword(options = {}) {
  const paths = options.paths ?? getSitePublishPaths();
  const password = options.password ?? await promptForPassword();
  const protect = options.protect ?? runPowerShell;
  if (!password) {
    throw new Error("The SFTP password cannot be empty.");
  }

  protect(protectPasswordCommand, JSON.stringify({ password, path: paths.credentialPath }));
}

export function loadPassword({
  paths = getSitePublishPaths(),
  unprotect = runPowerShell,
  exists = existsSync
} = {}) {
  if (!exists(paths.credentialPath)) {
    throw new Error("No stored SFTP password. Run npm run publish:site:password first.");
  }
  return unprotect(unprotectPasswordCommand, paths.credentialPath);
}

export async function clearStoredPassword({ paths = getSitePublishPaths(), remove = rm } = {}) {
  try {
    await remove(paths.credentialPath);
  } catch (error) {
    if (error.code !== "ENOENT") {
      throw error;
    }
  }
}

async function readKnownHostFingerprint(fingerprintPath, read = readFile) {
  try {
    return (await read(fingerprintPath, "utf8")).trim() || null;
  } catch (error) {
    if (error.code === "ENOENT") {
      return null;
    }
    throw error;
  }
}

async function connectSftp({ paths, password, createSftp, read, write, makeDirectory }) {
  const knownFingerprint = await readKnownHostFingerprint(paths.hostFingerprintPath, read);
  const sftp = createSftp();
  let observedFingerprint;
  let connected = false;

  try {
    await sftp.connect({
      host,
      port,
      username,
      password,
      hostHash: "sha256",
      hostVerifier: (serverFingerprint) => {
        observedFingerprint = serverFingerprint;
        return !knownFingerprint || serverFingerprint === knownFingerprint;
      }
    });
    connected = true;

    if (!knownFingerprint) {
      await makeDirectory(path.dirname(paths.hostFingerprintPath), { recursive: true });
      await write(paths.hostFingerprintPath, `${observedFingerprint}\n`, { encoding: "utf8", flag: "wx" });
    }
    return sftp;
  } catch (error) {
    if (connected) {
      await sftp.end();
    }
    throw error;
  }
}

export function formatRemoteListing(entries) {
  return entries
    .sort((left, right) => left.name.localeCompare(right.name))
    .map((entry) => `${entry.type === "d" ? "[directory]" : "[file]"} ${entry.name}`);
}

export function generateStatisticsReport({
  run = spawnSync,
  npmCliPath = process.env.npm_execpath
} = {}) {
  if (!npmCliPath) {
    throw new Error("Site publishing must be run through npm: npm run publish:site");
  }
  const result = run(process.execPath, [
    npmCliPath,
    "run", "code:statistics", "--",
    "--report", "--no-open", "--quiet"
  ], {
    cwd: repositoryRoot,
    encoding: "utf8",
    stdio: ["ignore", "ignore", "inherit"],
    windowsHide: true
  });
  if (result.error) {
    throw result.error;
  }
  if (result.status !== 0) {
    throw new Error(`Code statistics generation failed with exit code ${result.status ?? "unknown"}.`);
  }
}

export async function runSitePublication({
  generate = generateStatisticsReport,
  publish = publishSite
} = {}) {
  generate();
  return publish();
}

export async function publishSite({
  paths = getSitePublishPaths(),
  password = loadPassword({ paths }),
  createSftp = () => new SftpClient(),
  read = readFile,
  write = writeFile,
  makeDirectory = mkdir,
  source = sourceDirectory,
  log = console.log
} = {}) {
  await access(source);
  const sftp = await connectSftp({ paths, password, createSftp, read, write, makeDirectory });
  try {
    await sftp.mkdir(remoteDirectory, true);
    await sftp.uploadDir(source, remoteDirectory);
    log(`Published ${path.relative(repositoryRoot, source)} to ${host}:${remoteDirectory}`);
  } finally {
    await sftp.end();
  }
}

export async function listSite({
  paths = getSitePublishPaths(),
  password = loadPassword({ paths }),
  createSftp = () => new SftpClient(),
  read = readFile,
  write = writeFile,
  makeDirectory = mkdir,
  log = console.log
} = {}) {
  const sftp = await connectSftp({ paths, password, createSftp, read, write, makeDirectory });
  try {
    const entries = await sftp.list(remoteDirectory);
    log(`${host}:${remoteDirectory}`);
    for (const line of formatRemoteListing(entries)) {
      log(line);
    }
    return entries;
  } finally {
    await sftp.end();
  }
}

async function main() {
  if (process.argv[2] === "--store-password") {
    await storePassword();
    console.log("Stored the one.com SFTP password for this Windows account.");
  } else if (process.argv[2] === "--clear-password") {
    await clearStoredPassword();
    console.log("Removed the stored one.com SFTP password.");
  } else if (process.argv[2] === "--list") {
    await listSite();
  } else {
    await runSitePublication();
  }
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  main().catch((error) => {
    console.error(`Site publishing failed: ${error.message}`);
    process.exitCode = 1;
  });
}
