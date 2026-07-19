import { act, renderHook } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { usePowerControl } from "./usePowerControl";

describe("usePowerControl", () => {
  afterEach(() => vi.useRealTimers());

  it("keeps a same-action retry pending when the old result arrives", async () => {
    vi.useFakeTimers();
    const send = vi.fn();
    const { result } = renderHook(() => usePowerControl("paired", send));

    act(() => { result.current.requestPowerAction("lock"); });
    const firstRequest = send.mock.calls[0]![0] as { operationId?: string };
    expect(firstRequest.operationId).toMatch(/^[A-Za-z0-9-]+$/);
    await act(() => vi.advanceTimersByTime(5000));

    act(() => { result.current.requestPowerAction("lock"); });
    const secondRequest = send.mock.calls[1]![0] as { operationId?: string };
    expect(secondRequest.operationId).not.toBe(firstRequest.operationId);
    expect(result.current.pendingPowerAction).toBe("lock");

    let oldMatched = true;
    act(() => {
      oldMatched = result.current.completePowerAction({
        type: "system.power.result", operationId: firstRequest.operationId!, action: "lock",
        succeeded: true, message: "Old result"
      });
    });
    expect(oldMatched).toBe(false);
    expect(result.current.pendingPowerAction).toBe("lock");
    expect(result.current.powerActionResult).toBeNull();

    let currentMatched = false;
    act(() => {
      currentMatched = result.current.completePowerAction({
        type: "system.power.result", operationId: secondRequest.operationId!, action: "lock",
        succeeded: true, message: "Current result"
      });
    });
    expect(currentMatched).toBe(true);
    expect(result.current.pendingPowerAction).toBeNull();
    expect(result.current.powerActionResult?.message).toBe("Current result");
  });
});
