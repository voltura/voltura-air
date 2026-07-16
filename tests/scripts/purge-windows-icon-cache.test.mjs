import assert from "node:assert/strict";
import path from "node:path";
import test from "node:test";
import { purgeWindowsIconCache } from "../../scripts/purge-windows-icon-cache.mjs";

test("purges Windows icon caches between stopping the host and restarting Explorer", () => {
  const operations = [];
  const run = (command, args) => {
    operations.push({ command, args });
    return {
      status: 0,
      stdout: command !== "tasklist"
        ? ""
        : args[1].startsWith("PID eq")
          ? '"node.exe","999","Console","1","100,000 K"'
          : '"explorer.exe","123","Console","1","100,000 K"',
      stderr: ""
    };
  };

  purgeWindowsIconCache({
    platform: "win32",
    localAppData: "C:\\Users\\tester\\AppData\\Local",
    systemRoot: "C:\\Windows",
    processId: 999,
    run,
    stopHost: () => operations.push({ stopHost: true }),
    listDirectory: () => ["iconcache_16.db", "ICONCACHE_IDX.DB", "thumbcache_16.db"],
    removeFile: (filePath) => operations.push({ remove: filePath }),
    log: () => {}
  });

  assert.deepEqual(operations, [
    { command: "tasklist", args: ["/FI", "PID eq 999", "/FO", "CSV", "/NH"] },
    { stopHost: true },
    { command: path.join("C:\\Windows", "System32", "ie4uinit.exe"), args: ["-ClearIconCache"] },
    { command: "tasklist", args: ["/FI", "IMAGENAME eq explorer.exe", "/FI", "SESSION eq 1", "/FO", "CSV", "/NH"] },
    { command: "taskkill", args: ["/PID", "123", "/F"] },
    { remove: path.join("C:\\Users\\tester\\AppData\\Local", "IconCache.db") },
    { remove: path.join("C:\\Users\\tester\\AppData\\Local", "Microsoft", "Windows", "Explorer", "iconcache_16.db") },
    { remove: path.join("C:\\Users\\tester\\AppData\\Local", "Microsoft", "Windows", "Explorer", "ICONCACHE_IDX.DB") },
    {
      command: path.join("C:\\Windows", "System32", "WindowsPowerShell", "v1.0", "powershell.exe"),
      args: [
        "-NoProfile",
        "-NonInteractive",
        "-Command",
        "Start-Process -FilePath (Join-Path $env:SystemRoot 'explorer.exe')"
      ]
    },
    { command: path.join("C:\\Windows", "System32", "ie4uinit.exe"), args: ["-show"] }
  ]);
});

test("restarts Explorer when deleting an icon-cache file fails", () => {
  const commands = [];

  assert.throws(
    () => purgeWindowsIconCache({
      platform: "win32",
      localAppData: "C:\\Local",
      systemRoot: "C:\\Windows",
      processId: 999,
      run: (command, args) => {
        commands.push({ command, args });
        return {
          status: 0,
          stdout: command !== "tasklist"
            ? ""
            : args[1].startsWith("PID eq")
              ? '"node.exe","999","Console","1","100,000 K"'
              : '"explorer.exe","123","Console","1","100,000 K"',
          stderr: ""
        };
      },
      stopHost: () => {},
      listDirectory: () => [],
      removeFile: () => {
        throw new Error("cache file is locked");
      },
      log: () => {}
    }),
    /cache file is locked/
  );

  assert.deepEqual(commands.at(-1), {
    command: path.join("C:\\Windows", "System32", "WindowsPowerShell", "v1.0", "powershell.exe"),
    args: [
      "-NoProfile",
      "-NonInteractive",
      "-Command",
      "Start-Process -FilePath (Join-Path $env:SystemRoot 'explorer.exe')"
    ]
  });
});

test("does not launch another Explorer when stopping the current shell fails", () => {
  const commands = [];

  assert.throws(
    () => purgeWindowsIconCache({
      platform: "win32",
      localAppData: "C:\\Local",
      systemRoot: "C:\\Windows",
      processId: 999,
      run: (command, args) => {
        commands.push({ command, args });
        if (command === "taskkill") {
          return { status: 5, stdout: "", stderr: "Access is denied." };
        }

        return {
          status: 0,
          stdout: command !== "tasklist"
            ? ""
            : args[1].startsWith("PID eq")
              ? '"node.exe","999","Console","1","100,000 K"'
              : '"explorer.exe","123","Console","1","100,000 K"',
          stderr: ""
        };
      },
      stopHost: () => {},
      listDirectory: () => [],
      removeFile: () => {},
      log: () => {}
    }),
    /Access is denied/
  );

  assert.equal(commands.some(({ command }) => command.endsWith("powershell.exe")), false);
});

test("refuses to alter caches outside Windows", () => {
  assert.throws(
    () => purgeWindowsIconCache({ platform: "linux" }),
    /only supported on Windows/i
  );
});
