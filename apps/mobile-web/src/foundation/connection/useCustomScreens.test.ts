import { act, renderHook } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import {
  incompatibleCustomScreenResponseCode,
  incompatibleCustomScreenResponseMessage,
  useCustomScreens,
} from "./useCustomScreens";

describe("useCustomScreens", () => {
  afterEach(() => vi.useRealTimers());

  it("ends a lost definition load with retry guidance and ignores its late reply", () => {
    vi.useFakeTimers();
    const send = vi.fn();
    const { result, unmount } = renderHook(() =>
      useCustomScreens("paired", 4, "catalog.current", send),
    );
    act(() => result.current.requestCustomScreen("screen.one"));
    const { operationId } = send.mock.calls[0]![0] as { operationId: string };
    act(() => {
      vi.advanceTimersByTime(15_000);
    });
    expect(result.current.customScreenGetResult?.code).toBe("VAIR-CUSTOM-SCREEN-RESPONSE-TIMEOUT");
    act(() =>
      expect(
        result.current.completeCustomScreenGet({
          type: "custom.screen.get.result",
          operationId,
          succeeded: false,
        }),
      ).toBe(false),
    );
    act(() => result.current.requestCustomScreen("screen.one"));
    expect(result.current.customScreenGetResult).toBeNull();
    unmount();
    expect(vi.getTimerCount()).toBe(0);
  });

  it("expires each lost invocation while preserving newer pending presses of the same button", () => {
    vi.useFakeTimers();
    const { result } = renderHook(() => useCustomScreens("paired", 4, "catalog.current", vi.fn()));
    act(() => result.current.invokeCustomScreenButton("screen.one", "rev", "button.one"));
    act(() => {
      vi.advanceTimersByTime(10_000);
    });
    act(() => result.current.invokeCustomScreenButton("screen.one", "rev", "button.one"));
    act(() => {
      vi.advanceTimersByTime(20_000);
    });
    expect(result.current.customScreenInvokeResult?.code).toBe("VAIR-CUSTOM-SCREEN-INVOKE-TIMEOUT");
    expect(result.current.pendingCustomScreenButtonIds.has("button.one")).toBe(true);
    act(() => {
      vi.advanceTimersByTime(10_000);
    });
    expect(result.current.pendingCustomScreenButtonIds.size).toBe(0);
    expect(vi.getTimerCount()).toBe(0);
  });

  it("drops pending invoke owners and their timers on disconnect or replacement", () => {
    vi.useFakeTimers();
    const send = vi.fn();
    const { result, rerender, unmount } = renderHook(
      ({ epoch, paired }) =>
        useCustomScreens(paired ? "paired" : "disconnected", epoch, "catalog.current", send),
      { initialProps: { epoch: 4, paired: true } },
    );
    act(() => {
      result.current.requestCustomScreen("screen.one");
      result.current.invokeCustomScreenButton("screen.one", "rev", "button.one");
    });
    const { operationId } = send.mock.calls[1]![0] as { operationId: string };
    rerender({ epoch: 4, paired: false });
    expect(vi.getTimerCount()).toBe(0);
    act(() =>
      expect(
        result.current.completeCustomScreenInvoke({
          type: "custom.screen.invoke.result",
          operationId,
          screenId: "screen.one",
          buttonId: "button.one",
          succeeded: true,
          message: "done",
        }),
      ).toBe(false),
    );
    rerender({ epoch: 5, paired: true });
    expect(result.current.pendingCustomScreenButtonIds.size).toBe(0);
    act(() => result.current.invokeCustomScreenButton("screen.one", "rev", "button.one"));
    unmount();
    expect(vi.getTimerCount()).toBe(0);
  });
  it("completes a correlated rejected definition instead of leaving it pending", () => {
    const send = vi.fn();
    const { result } = renderHook(() => useCustomScreens("paired", 4, "catalog.current", send));

    act(() => {
      result.current.requestCustomScreen("screen.gyro");
    });
    const request = send.mock.calls[0]?.[0] as { operationId: string };

    act(() => {
      expect(result.current.rejectCustomScreenGet(request.operationId)).toBe(true);
    });

    expect(result.current.customScreenDefinition).toBeNull();
    expect(result.current.customScreenGetResult).toEqual({
      type: "custom.screen.get.result",
      operationId: request.operationId,
      succeeded: false,
      code: incompatibleCustomScreenResponseCode,
      message: incompatibleCustomScreenResponseMessage,
    });
    expect(result.current.rejectCustomScreenGet("other-operation")).toBe(false);
  });
});
