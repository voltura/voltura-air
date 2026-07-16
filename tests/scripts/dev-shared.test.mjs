import assert from "node:assert/strict";
import test from "node:test";
import { stopExistingHost, waitForWindowsProcessExit } from "../../scripts/dev-shared.mjs";

test("stops only the host before waiting for cursor recovery", () => {
  const operations = [];
  const run = (command, args) => {
    operations.push({ command, args });
    return { stdout: "" };
  };

  stopExistingHost({
    platform: "win32",
    run,
    waitForProcessExit: (imageName, options) => {
      operations.push({ waitFor: imageName, run: options.run });
      return true;
    }
  });

  assert.deepEqual(operations[0], {
    command: "taskkill",
    args: ["/IM", "VolturaAir.Host.exe", "/F"]
  });
  assert.equal(operations[1].waitFor, "VolturaAir.CursorWatchdog.exe");
  assert.equal(operations[1].run, run);
});

test("refuses to continue when cursor recovery does not finish", () => {
  assert.throws(
    () => stopExistingHost({
      platform: "win32",
      run: () => ({ stdout: "" }),
      waitForProcessExit: () => false
    }),
    /cursor watchdog/i);
});

test("waits until the named Windows process exits", () => {
  let checks = 0;
  const sleeps = [];
  const exited = waitForWindowsProcessExit("VolturaAir.CursorWatchdog.exe", {
    run: () => ({
      stdout: checks++ < 2
        ? '"VolturaAir.CursorWatchdog.exe","123","Console","1","1,000 K"'
        : "INFO: No tasks are running which match the specified criteria."
    }),
    now: () => 0,
    sleep: (milliseconds) => sleeps.push(milliseconds),
    timeoutMs: 100,
    pollIntervalMs: 10
  });

  assert.equal(exited, true);
  assert.deepEqual(sleeps, [10, 10]);
});
