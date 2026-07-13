import { act, renderHook } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { useAwakeControl } from "./useAwakeControl";

describe("useAwakeControl", () => {
  afterEach(() => vi.useRealTimers());

  it("sends one state change and clears it when the host responds", () => {
    const send = vi.fn();
    const { result } = renderHook(() => useAwakeControl("paired", send));

    act(() => result.current.requestAwakeChange(true));
    expect(send).toHaveBeenCalledExactlyOnceWith({ type: "awake.set", enabled: true });
    expect(result.current.pendingAwakeChange).toBe(true);

    act(() => result.current.completeAwakeChange({ type: "awake.result", enabled: true, succeeded: true, message: "Enabled" }));
    expect(result.current.pendingAwakeChange).toBeNull();
    expect(result.current.awakeResult?.succeeded).toBe(true);
  });

  it("reports a timeout without sending a duplicate", () => {
    vi.useFakeTimers();
    const send = vi.fn();
    const { result } = renderHook(() => useAwakeControl("paired", send));

    act(() => result.current.requestAwakeChange(false));
    act(() => vi.advanceTimersByTime(5000));

    expect(send).toHaveBeenCalledTimes(1);
    expect(result.current.pendingAwakeChange).toBeNull();
    expect(result.current.awakeResult?.code).toBe("VAIR-AWAKE-RESPONSE-TIMEOUT");
  });
});
