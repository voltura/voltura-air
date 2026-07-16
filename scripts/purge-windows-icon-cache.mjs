import { readdirSync, rmSync } from "node:fs";
import { spawnSync } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { stopExistingHost } from "./dev-shared.mjs";

const explorerImageName = "explorer.exe";
const iconCacheFilePattern = /^iconcache(?:_.+)?\.db$/i;

export function purgeWindowsIconCache(options = {}) {
  const platform = options.platform ?? process.platform;
  if (platform !== "win32") {
    throw new Error("The Voltura Air icon-cache purge is only supported on Windows.");
  }

  const localAppData = options.localAppData ?? process.env.LOCALAPPDATA;
  const systemRoot = options.systemRoot ?? process.env.SystemRoot;
  if (!localAppData || !systemRoot) {
    throw new Error("Windows did not provide LOCALAPPDATA and SystemRoot.");
  }

  const run = options.run ?? spawnSync;
  const stopHost = options.stopHost ?? stopExistingHost;
  const listDirectory = options.listDirectory ?? readdirSync;
  const removeFile = options.removeFile ?? removeCacheFile;
  const log = options.log ?? console.log;
  const explorerCacheDirectory = path.join(localAppData, "Microsoft", "Windows", "Explorer");
  const rootIconCache = path.join(localAppData, "IconCache.db");
  const iconCacheResetTool = path.join(systemRoot, "System32", "ie4uinit.exe");
  const powershell = path.join(systemRoot, "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
  const sessionId = getCurrentSessionId(run, options.processId ?? process.pid);

  log("Closing Voltura Air and refreshing the current Windows user's icon cache...");
  stopHost({ platform, run });
  runChecked(run, iconCacheResetTool, ["-ClearIconCache"], "request the Windows icon-cache reset");

  const explorerProcessId = findProcessId(run, explorerImageName, sessionId);
  let explorerStopped = false;
  let purgeError;

  try {
    if (explorerProcessId) {
      runChecked(run, "taskkill", ["/PID", explorerProcessId, "/F"], "stop Windows Explorer");
      explorerStopped = true;
    }

    removeFile(rootIconCache);
    for (const fileName of listCacheFiles(listDirectory, explorerCacheDirectory)) {
      removeFile(path.join(explorerCacheDirectory, fileName));
    }
  } catch (error) {
    purgeError = error;
  }

  let restartError;
  if (explorerStopped) {
    try {
      runChecked(
        run,
        powershell,
        [
          "-NoProfile",
          "-NonInteractive",
          "-Command",
          "Start-Process -FilePath (Join-Path $env:SystemRoot 'explorer.exe')"
        ],
        "restart Windows Explorer"
      );
    } catch (error) {
      restartError = error;
    }
  }

  if (purgeError && restartError) {
    throw new AggregateError(
      [purgeError, restartError],
      "The icon cache could not be purged and Windows Explorer could not be restarted."
    );
  }

  if (purgeError) {
    throw purgeError;
  }

  if (restartError) {
    throw restartError;
  }

  runChecked(run, iconCacheResetTool, ["-show"], "refresh Windows icons");
  log("Windows icon cache purged. Start Voltura Air again to use the current notification icon.");
}

function listCacheFiles(listDirectory, directory) {
  try {
    return listDirectory(directory).filter((fileName) => iconCacheFilePattern.test(fileName));
  } catch (error) {
    if (error?.code === "ENOENT") {
      return [];
    }

    throw error;
  }
}

function removeCacheFile(filePath) {
  try {
    rmSync(filePath, { force: true });
  } catch (error) {
    throw new Error(`Could not remove Windows icon-cache file ${filePath}.`, { cause: error });
  }
}

function getCurrentSessionId(run, processId) {
  const result = run("tasklist", ["/FI", `PID eq ${processId}`, "/FO", "CSV", "/NH"], {
    encoding: "utf8",
    stdio: ["ignore", "pipe", "pipe"]
  });
  assertRunSucceeded(result, "identify the current Windows session");
  const row = parseTaskListRow(result.stdout);
  if (!row || row.processId !== String(processId)) {
    throw new Error("Could not identify the current Windows session.");
  }

  return row.sessionId;
}

function findProcessId(run, imageName, sessionId) {
  const result = run(
    "tasklist",
    ["/FI", `IMAGENAME eq ${imageName}`, "/FI", `SESSION eq ${sessionId}`, "/FO", "CSV", "/NH"],
    {
      encoding: "utf8",
      stdio: ["ignore", "pipe", "pipe"]
    }
  );
  assertRunSucceeded(result, "inspect Windows Explorer");

  const row = parseTaskListRow(result.stdout);
  return row?.imageName.toLowerCase() === imageName.toLowerCase() ? row.processId : null;
}

function parseTaskListRow(output) {
  const match = output?.trim().match(/^"([^"]*)","([^"]*)","([^"]*)","([^"]*)"/);
  return match
    ? { imageName: match[1], processId: match[2], sessionId: match[4] }
    : null;
}

function runChecked(run, command, args, description) {
  const result = run(command, args, {
    encoding: "utf8",
    stdio: ["ignore", "pipe", "pipe"]
  });
  assertRunSucceeded(result, description);
}

function assertRunSucceeded(result, description) {
  if (result.error) {
    throw new Error(`Could not ${description}: ${result.error.message}`, { cause: result.error });
  }

  if (result.status !== 0) {
    const detail = result.stderr?.trim() || result.stdout?.trim();
    throw new Error(`Could not ${description}${detail ? `: ${detail}` : "."}`);
  }
}

const currentFilePath = fileURLToPath(import.meta.url);
if (process.argv[1] && path.resolve(process.argv[1]).toLowerCase() === path.resolve(currentFilePath).toLowerCase()) {
  try {
    purgeWindowsIconCache();
  } catch (error) {
    console.error(error instanceof Error ? error.message : error);
    process.exitCode = 1;
  }
}
