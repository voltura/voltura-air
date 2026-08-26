import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";
import { stopExistingHost, waitForWindowsProcessExit } from "../../scripts/dev-shared.mjs";

const devSharedSource = readFileSync(
  new URL("../../scripts/dev-shared.mjs", import.meta.url),
  "utf8",
);

test("stops and waits only for the host", () => {
  const operations = [];
  const run = (command, args) => {
    operations.push({ command, args });
    return { stdout: "" };
  };

  stopExistingHost({
    platform: "win32",
    run,
    listHostProcesses: () => [
      {
        pid: 123,
        executablePath: new URL(
          "../../apps/windows-host/bin/Debug/net10.0-windows/VolturaAir.Host.exe",
          import.meta.url,
        ).pathname.slice(1),
      },
    ],
    waitForProcessExit: (imageName, options) => {
      operations.push({ waitFor: imageName, run: options.run });
      return true;
    },
  });

  assert.deepEqual(operations[0], {
    command: "taskkill",
    args: ["/PID", "123", "/T", "/F"],
  });
  assert.equal(operations[1].waitFor, "VolturaAir.Host.exe");
  assert.equal(operations[1].run, run);
  assert.equal(operations.length, 2);
  assert.doesNotMatch(devSharedSource, /watchdog|cursor recovery/iu);
});

test("refuses to continue when the existing host does not exit", () => {
  assert.throws(
    () =>
      stopExistingHost({
        platform: "win32",
        run: () => ({ stdout: "" }),
        listHostProcesses: () => [],
        waitForProcessExit: (imageName) => imageName !== "VolturaAir.Host.exe",
      }),
    /existing Voltura Air host/i,
  );
});

test("refuses to stop a same-named executable outside verified Voltura Air locations", () => {
  assert.throws(
    () =>
      stopExistingHost({
        platform: "win32",
        run: () => assert.fail("An unverified process must not be terminated"),
        listHostProcesses: () => [{ pid: 999, executablePath: "C:\\Temp\\VolturaAir.Host.exe" }],
      }),
    /unverified VolturaAir\.Host\.exe/u,
  );
});

test("waits until the named Windows process exits", () => {
  let checks = 0;
  const sleeps = [];
  const exited = waitForWindowsProcessExit("VolturaAir.Host.exe", {
    run: () => ({
      stdout:
        checks++ < 2
          ? '"VolturaAir.Host.exe","123","Console","1","1,000 K"'
          : "INFO: No tasks are running which match the specified criteria.",
    }),
    now: () => 0,
    sleep: (milliseconds) => sleeps.push(milliseconds),
    timeoutMs: 100,
    pollIntervalMs: 10,
  });

  assert.equal(exited, true);
  assert.deepEqual(sleeps, [10, 10]);
});

test("treats an absent process as already stopped", () => {
  let checks = 0;
  const exited = waitForWindowsProcessExit("VolturaAir.Host.exe", {
    run: () => {
      checks += 1;
      return { stdout: "INFO: No tasks are running which match the specified criteria." };
    },
    sleep: () => assert.fail("An absent process must not be waited on"),
  });

  assert.equal(exited, true);
  assert.equal(checks, 1);
});

test("does not treat a failed process inventory as an absent host", () => {
  assert.throws(
    () =>
      waitForWindowsProcessExit("VolturaAir.Host.exe", {
        run: () => ({ status: 1, stdout: "" }),
      }),
    /Could not inspect/u,
  );
});
