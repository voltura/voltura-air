import { act, renderHook } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { MobileHostDiagnosticsSnapshot } from "../protocol/messages";
import { useDiagnostics } from "./useDiagnostics";

const snapshot: MobileHostDiagnosticsSnapshot = {
  generatedAt: "2026-08-25T12:00:00.000Z",
  hostVersion: "1.1.0",
  connectionMethod: "Direct",
  enhancedCapabilities: "enabled",
  relayStatus: "Not used",
  relayEndpointType: "Unavailable",
  relayFailureCode: "Unavailable",
  pairingState: "Paired",
  windowsLockPolicy: "Block while locked",
  applicationLogging: "Disabled",
  applicationLogRetention: "7 days",
  pairedDeviceCount: 1,
  connectedDeviceCount: 1,
  pcName: "DESKTOP",
  selectedAdapter: "Ethernet",
  selectedIp: "192.168.1.10",
  selectedPort: 51395,
  advisories: [],
  computer: {
    windows: "Windows 11 Pro, version 24H2, build 26100",
    system: "Example Model",
    processor: "Example CPU",
    logicalProcessors: "8",
    primaryDisplay: "3840 × 2160 at 60 Hz",
    installedMemory: "16.0 GiB",
    availableMemory: "8.0 GiB",
    systemDisk: "500.0 GiB total, 200.0 GiB free",
    systemUptime: "1d 2h 3m",
  },
};

describe("useDiagnostics", () => {
  afterEach(() => {
    vi.restoreAllMocks();
    vi.useRealTimers();
  });
  it("sends one request per user action and never schedules polling", () => {
    const send = vi.fn();
    const setIntervalSpy = vi.spyOn(globalThis, "setInterval");
    const { result } = renderHook(() => useDiagnostics("paired", 1, send));

    let operationId: string | null = null;
    act(() => {
      operationId = result.current.requestDiagnostics();
    });
    act(() => {
      expect(result.current.requestDiagnostics()).toBeNull();
    });

    expect(send).toHaveBeenCalledExactlyOnceWith({ type: "diagnostics.get", operationId });
    expect(setIntervalSpy).not.toHaveBeenCalled();
  });

  it("expires a lost reply, permits retry, and ignores the late response", () => {
    vi.useFakeTimers();
    const send = vi.fn();
    const { result, unmount } = renderHook(() => useDiagnostics("paired", 1, send));
    let operationId = "";
    act(() => {
      operationId = result.current.requestDiagnostics()!;
    });
    act(() => {
      vi.advanceTimersByTime(15_000);
    });
    expect(result.current.pending).toBe(false);
    expect(result.current.failure?.code).toBe("diagnostics-timeout");
    act(() => {
      expect(
        result.current.completeDiagnostics({
          type: "diagnostics.get.result",
          operationId,
          succeeded: true,
          message: "ready",
          snapshot,
        }),
      ).toBe(false);
      expect(result.current.requestDiagnostics()).not.toBeNull();
    });
    unmount();
    expect(vi.getTimerCount()).toBe(0);
  });

  it("invalidates pending responses on disconnect even before the epoch changes", () => {
    const send = vi.fn();
    const { result, rerender } = renderHook(
      ({ connected }) => useDiagnostics(connected ? "paired" : "unavailable", 1, send),
      {
        initialProps: { connected: true },
      },
    );
    let operationId = "";
    act(() => {
      operationId = result.current.requestDiagnostics()!;
    });
    rerender({ connected: false });
    act(() => {
      expect(
        result.current.completeDiagnostics({
          type: "diagnostics.get.result",
          operationId,
          succeeded: true,
          message: "ready",
          snapshot,
        }),
      ).toBe(false);
    });
    rerender({ connected: true });
    act(() => {
      expect(result.current.requestDiagnostics()).not.toBeNull();
    });
    expect(result.current.snapshot).toBeNull();
  });

  it("keeps the last snapshot usable when a manual refresh fails", () => {
    const send = vi.fn();
    const { result } = renderHook(() => useDiagnostics("paired", 1, send));

    let operationId = "";
    act(() => {
      operationId = result.current.requestDiagnostics()!;
    });
    act(() => {
      result.current.completeDiagnostics({
        type: "diagnostics.get.result",
        operationId,
        succeeded: true,
        message: "Diagnostics ready.",
        snapshot,
      });
    });
    expect(result.current.snapshot).toEqual(snapshot);

    act(() => {
      operationId = result.current.requestDiagnostics()!;
    });
    expect(result.current.snapshot).toEqual(snapshot);
    act(() => {
      result.current.completeDiagnostics({
        type: "diagnostics.get.result",
        operationId,
        succeeded: false,
        code: "diagnostics-unavailable",
        message: "Try again.",
      });
    });

    expect(result.current.snapshot).toEqual(snapshot);
    expect(result.current.failure).toEqual({
      code: "diagnostics-unavailable",
      message: "Try again.",
    });
  });
});
