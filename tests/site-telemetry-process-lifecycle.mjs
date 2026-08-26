const childCloseStates = new WeakMap();

export function trackChild(child) {
  let state = childCloseStates.get(child);
  if (state) return state;
  state = { closed: false, code: null, promise: null };
  state.promise = new Promise((resolve) => {
    child.once("close", (code) => {
      state.closed = true;
      state.code = code;
      resolve(code);
    });
  });
  childCloseStates.set(child, state);
  return state;
}

export async function waitForChildClose(child, timeoutMs, label) {
  const state = trackChild(child);
  if (state.closed) return state.code;
  let timer;
  try {
    return await Promise.race([
      state.promise,
      new Promise((_, reject) => {
        timer = setTimeout(
          () => reject(new Error(`${label} did not exit within ${timeoutMs} ms.`)),
          timeoutMs,
        );
      }),
    ]);
  } finally {
    clearTimeout(timer);
  }
}

export async function terminateChild(child, { timeoutMs = 3000, label = "Child process" } = {}) {
  const state = trackChild(child);
  if (state.closed) return state.code;

  if (child.exitCode === null) child.kill();
  try {
    return await waitForChildClose(child, timeoutMs, label);
  } catch (firstError) {
    if (child.exitCode === null) child.kill("SIGKILL");
    child.stdin?.destroy?.();
    child.stdout?.destroy?.();
    child.stderr?.destroy?.();
    try {
      return await waitForChildClose(child, timeoutMs, label);
    } catch (secondError) {
      child.unref?.();
      throw new AggregateError(
        [firstError, secondError],
        `${label} could not be confirmed stopped after bounded termination.`,
      );
    }
  }
}

export async function stopFixtureHolder(
  holder,
  { cleanupTimeoutMs = 15000, terminationTimeoutMs = 3000, forceCleanup } = {},
) {
  const state = trackChild(holder.child);
  let normalCleanupError = null;
  if (!state.closed) {
    try {
      holder.child.stdin.end("stop\n");
      const exitCode = await waitForChildClose(
        holder.child,
        cleanupTimeoutMs,
        "Telemetry test-table cleanup",
      );
      if (exitCode === 0 && holder.output.join("").includes("TELEMETRY_TEST_TABLES_REMOVED")) {
        return;
      }
      normalCleanupError = new Error(
        `Telemetry test-table cleanup failed with ${exitCode}. ` +
          `${(holder.errors.join("") || holder.output.join("")).slice(-2000)}`,
      );
    } catch (error) {
      normalCleanupError = error;
    }
  } else if (
    holder.child.exitCode === 0 &&
    holder.output.join("").includes("TELEMETRY_TEST_TABLES_REMOVED")
  ) {
    return;
  } else {
    normalCleanupError = new Error(
      `Telemetry test-table holder exited before cleanup. ` +
        `${(holder.errors.join("") || holder.output.join("")).slice(-2000)}`,
    );
  }

  let terminationError = null;
  if (!state.closed) {
    try {
      await terminateChild(holder.child, {
        timeoutMs: terminationTimeoutMs,
        label: "Telemetry test-table holder",
      });
    } catch (error) {
      terminationError = error;
    }
  }

  let fallbackError = null;
  if (typeof forceCleanup === "function") {
    try {
      await forceCleanup();
    } catch (error) {
      fallbackError = error;
    }
  }

  const errors = [normalCleanupError, terminationError, fallbackError].filter(Boolean);
  if (errors.length > 1) {
    throw new AggregateError(errors, "Telemetry test-table cleanup and recovery failed.");
  }
  throw errors[0];
}
