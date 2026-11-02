import { act, renderHook } from "@testing-library/react";
import { useRef } from "react";
import { describe, expect, it } from "vitest";
import type { PendingMovementAck } from "./useConnectionSender";
import { useConnectionRuntimeState } from "./useConnectionRuntimeState";

const terminalCapability = {
  enabled: true,
  permissionGranted: true,
  canUse: true,
  requiresRepair: false,
  active: true,
  ownedByClient: true,
  terminalId: "0123456789abcdef0123456789abcdef",
  shell: "windows-powershell" as const,
  reconnectGraceSeconds: 900,
};

describe("useConnectionRuntimeState", () => {
  it("waits for status receipt even when pairing includes metadata and accepts older status without metadata", () => {
    const { result } = renderHook(() =>
      useConnectionRuntimeState(
        useRef(new Map<number, number>()),
        useRef<PendingMovementAck | null>(null),
      ),
    );
    act(() => result.current.updateHostStatus({ pcName: "PC" }));
    expect(result.current.hasInitialHostStatus).toBe(false);
    act(() => result.current.updateHostStatus(undefined, true));
    expect(result.current.hasInitialHostStatus).toBe(true);
    act(() => result.current.clearRuntimeState());
    expect(result.current.hasInitialHostStatus).toBe(false);
  });

  it("retains explicit input denial for guidance and clears it on disconnect or older status", () => {
    const { result } = renderHook(() =>
      useConnectionRuntimeState(
        useRef(new Map<number, number>()),
        useRef<PendingMovementAck | null>(null),
      ),
    );
    act(() => result.current.updateCapabilities({ remoteInput: false }));
    expect(result.current.remoteInputPermission).toBe(false);
    act(() => result.current.updateCapabilities({}));
    expect(result.current.remoteInputPermission).toBeUndefined();
    act(() => result.current.updateCapabilities({ remoteInput: true }));
    expect(result.current.remoteInputPermission).toBe(true);
    act(() => result.current.clearRuntimeState());
    expect(result.current.remoteInputPermission).toBeUndefined();
  });

  it("retains Terminal only across transient connection unavailability", () => {
    const { result } = renderHook(() => {
      const pendingInputAcksRef = useRef(new Map<number, number>());
      const pendingMovementAckRef = useRef<PendingMovementAck | null>(null);
      return useConnectionRuntimeState(pendingInputAcksRef, pendingMovementAckRef);
    });

    act(() => {
      result.current.updateCapabilities({ terminal: terminalCapability });
    });
    act(() => {
      result.current.clearRuntimeState(true);
    });
    expect(result.current.terminalCapability).toEqual(terminalCapability);

    act(() => {
      result.current.clearRuntimeState();
    });
    expect(result.current.terminalCapability).toBeUndefined();
  });
});
