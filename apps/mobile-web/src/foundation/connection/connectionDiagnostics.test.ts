import { describe, expect, it, vi } from "vitest";
import type { ControllerSocket } from "./controllerSocket";

describe("connection failure diagnostics", () => {
  it("bounds retained metadata and never captures socket URLs or traffic", async () => {
    vi.resetModules();
    const { recordConnectionFailure, getConnectionDiagnostics } =
      await import("./connectionDiagnostics");
    const socket = {
      readyState: 1,
      bufferedAmount: 42,
      url: "wss://secret.invalid/?token=private",
    } as ControllerSocket;
    for (let index = 0; index < 25; index++) {
      recordConnectionFailure(`failure-${index}`, "health-timeout", socket, Date.now() - 60000);
    }
    const snapshot = getConnectionDiagnostics();
    expect(snapshot.failures).toHaveLength(20);
    expect(snapshot.failures[0]?.code).toBe("failure-5");
    expect(snapshot.failures.at(-1)).toMatchObject({
      trigger: "health-timeout",
      readyState: 1,
      bufferedBytes: 42,
    });
    expect(JSON.stringify(snapshot)).not.toContain("private");
    snapshot.failures[0]!.code = "mutated";
    expect(getConnectionDiagnostics().failures[0]?.code).toBe("failure-5");
  });
});
