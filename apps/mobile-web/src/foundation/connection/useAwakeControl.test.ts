import { act, renderHook } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { useAwakeControl } from "./useAwakeControl";

describe("useAwakeControl", () => {
  afterEach(() => vi.useRealTimers());

  it("sends one state change and clears it when the host responds", () => {
    const send = vi.fn();
    const { result } = renderHook(() => useAwakeControl("paired", send));

    act(() => { result.current.requestAwakeChange(true); });
    const request = send.mock.calls[0]![0] as { operationId: string };
    expect(send).toHaveBeenCalledExactlyOnceWith({ type: "awake.set", operationId: request.operationId, enabled: true });
    expect(result.current.pendingAwakeChange).toBe(true);

    act(() => { result.current.completeAwakeChange({ type: "awake.result", operationId: request.operationId, enabled: true, succeeded: true, message: "Enabled" }); });
    expect(result.current.pendingAwakeChange).toBeNull();
    expect(result.current.awakeResult?.succeeded).toBe(true);
  });

  it("reports a timeout without sending a duplicate", async () => {
    vi.useFakeTimers();
    const send = vi.fn();
    const { result } = renderHook(() => useAwakeControl("paired", send));

    act(() => { result.current.requestAwakeChange(false); });
    await act(() => vi.advanceTimersByTime(5000));

    expect(send).toHaveBeenCalledTimes(1);
    expect(result.current.pendingAwakeChange).toBeNull();
    expect(result.current.awakeResult?.code).toBe("VAIR-AWAKE-RESPONSE-TIMEOUT");
  });

  it("keeps a same-value retry pending when the old result arrives", async () => {
    vi.useFakeTimers();
    const send = vi.fn();
    const { result } = renderHook(() => useAwakeControl("paired", send));

    act(() => { result.current.requestAwakeChange(true); });
    const firstRequest = send.mock.calls[0]![0] as { operationId?: string };
    expect(firstRequest.operationId).toMatch(/^[A-Za-z0-9-]+$/);
    await act(() => vi.advanceTimersByTime(5000));

    act(() => { result.current.requestAwakeChange(true); });
    const secondRequest = send.mock.calls[1]![0] as { operationId?: string };
    expect(secondRequest.operationId).not.toBe(firstRequest.operationId);

    let oldMatched = true;
    act(() => {
      oldMatched = result.current.completeAwakeChange({
        type: "awake.result", operationId: firstRequest.operationId!, enabled: true,
        succeeded: true, message: "Old result"
      });
    });
    expect(oldMatched).toBe(false);
    expect(result.current.pendingAwakeChange).toBe(true);
    expect(result.current.awakeResult).toBeNull();

    let currentMatched = false;
    act(() => {
      currentMatched = result.current.completeAwakeChange({
        type: "awake.result", operationId: secondRequest.operationId!, enabled: true,
        succeeded: true, message: "Current result"
      });
    });
    expect(currentMatched).toBe(true);
    expect(result.current.pendingAwakeChange).toBeNull();
    expect(result.current.awakeResult?.message).toBe("Current result");
  });
});
