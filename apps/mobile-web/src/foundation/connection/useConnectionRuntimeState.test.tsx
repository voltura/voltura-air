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
