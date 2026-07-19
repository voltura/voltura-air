import { act, renderHook } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { useAppLaunch } from "./useAppLaunch";

describe("useAppLaunch", () => {
  afterEach(() => vi.useRealTimers());

  it("sends one opaque action ID and clears it on the matching host response", () => {
    const send = vi.fn();
    const { result } = renderHook(() => useAppLaunch("paired", send));

    act(() => { result.current.requestAppLaunch("custom.notes"); });
    expect(send).toHaveBeenCalledExactlyOnceWith(expect.objectContaining({ type: "app.launch", actionId: "custom.notes" }));
    const operationId: string = (send.mock.calls[0]![0] as { operationId: string }).operationId;
    expect(result.current.pendingAppLaunchId).toBe("custom.notes");

    act(() => { result.current.completeAppLaunch({
      type: "app.launch.result",
      operationId,
      actionId: "custom.notes",
      succeeded: true,
      message: "Started Notes."
    }); });
    expect(result.current.pendingAppLaunchId).toBeNull();
    expect(result.current.appLaunchResult?.message).toBe("Started Notes.");
  });

  it("ignores an unrelated result and reports a timeout", async () => {
    vi.useFakeTimers();
    const send = vi.fn();
    const { result } = renderHook(() => useAppLaunch("paired", send));

    act(() => { result.current.requestAppLaunch("preset.browser"); });
    act(() => { result.current.completeAppLaunch({
      type: "app.launch.result",
      operationId: "unrelated-operation",
      actionId: "custom.other",
      succeeded: true,
      message: "Unrelated"
    }); });
    expect(result.current.pendingAppLaunchId).toBe("preset.browser");

    await act(() => vi.advanceTimersByTime(5000));
    expect(send).toHaveBeenCalledTimes(1);
    expect(result.current.pendingAppLaunchId).toBeNull();
    expect(result.current.appLaunchResult?.code).toBe("VAIR-APP-LAUNCH-RESPONSE-TIMEOUT");
  });

  it("clears completed launch feedback after a few seconds", async () => {
    vi.useFakeTimers();
    const send = vi.fn();
    const { result } = renderHook(() => useAppLaunch("paired", send));

    act(() => { result.current.requestAppLaunch("preset.vlc"); });
    const operationId: string = (send.mock.calls[0]![0] as { operationId: string }).operationId;
    act(() => { result.current.completeAppLaunch({
      type: "app.launch.result",
      operationId,
      actionId: "preset.vlc",
      succeeded: true,
      message: "Started VLC."
    }); });

    expect(result.current.appLaunchResult?.message).toBe("Started VLC.");
    await act(() => vi.advanceTimersByTime(3999));
    expect(result.current.appLaunchResult).not.toBeNull();
    await act(() => vi.advanceTimersByTime(1));
    expect(result.current.appLaunchResult).toBeNull();
  });

  it("does not complete a retry when a delayed result has the same action ID", async () => {
    vi.useFakeTimers();
    const send = vi.fn();
    const { result } = renderHook(() => useAppLaunch("paired", send));

    act(() => { result.current.requestAppLaunch("preset.browser"); });
    const firstOperationId: string = (send.mock.calls[0]![0] as { operationId: string }).operationId;
    await act(() => vi.advanceTimersByTime(5000));
    act(() => { result.current.requestAppLaunch("preset.browser"); });
    const secondOperationId: string = (send.mock.calls[1]![0] as { operationId: string }).operationId;

    act(() => { result.current.completeAppLaunch({
      type: "app.launch.result",
      operationId: firstOperationId,
      actionId: "preset.browser",
      succeeded: true,
      message: "Started Browser."
    }); });

    expect(secondOperationId).not.toBe(firstOperationId);
    expect(result.current.pendingAppLaunchId).toBe("preset.browser");
    expect(result.current.appLaunchResult).toBeNull();
  });
});
