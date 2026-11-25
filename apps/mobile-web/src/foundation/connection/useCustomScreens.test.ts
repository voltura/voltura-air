import { act, renderHook } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { CustomScreenDefinition } from "../protocol/messages";
import {
  incompatibleCustomScreenResponseCode,
  incompatibleCustomScreenResponseMessage,
  useCustomScreens,
} from "./useCustomScreens";

function definition(id: string, revision: string): CustomScreenDefinition {
  return {
    id, revision, name: id, orientationLayoutsEnabled: false, showNavigationHeader: true, sections: [],
  };
}

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

  it("retires a pending lookup and its errors when the host advertises a new catalog", () => {
    vi.useFakeTimers();
    const send = vi.fn();
    const { result, rerender } = renderHook(
      ({ revision }) => useCustomScreens("paired", 4, revision, send),
      { initialProps: { revision: "catalog.old" } },
    );
    act(() => result.current.requestCustomScreen("screen.one"));
    const oldOperation = (send.mock.calls[0]![0] as { operationId: string }).operationId;
    rerender({ revision: "catalog.latest" });
    expect(vi.getTimerCount()).toBe(0);

    act(() => {
      expect(result.current.rejectCustomScreenGet(oldOperation)).toBe(false);
      expect(result.current.completeCustomScreenGet({
        type: "custom.screen.get.result", operationId: oldOperation, succeeded: true,
        screen: definition("screen.one", "previous"),
      })).toBe(false);
    });
    expect(result.current.customScreenDefinition).toBeNull();
    expect(result.current.customScreenGetResult).toBeNull();

    act(() => result.current.requestCustomScreen("screen.one"));
    const currentOperation = (send.mock.calls[1]![0] as { operationId: string }).operationId;
    act(() => {
      expect(result.current.completeCustomScreenGet({
        type: "custom.screen.get.result", operationId: currentOperation, succeeded: true,
        screen: definition("screen.one", "latest"),
      })).toBe(true);
    });
    expect(result.current.customScreenDefinition?.revision).toBe("latest");
  });

  it.each([
    ["screen.two", "old"],
    ["screen.one", "new"],
  ])("keeps the current definition for %s at revision %s after a stale invocation", (id, revision) => {
    const send = vi.fn();
    const { result } = renderHook(() => useCustomScreens("paired", 4, "catalog.current", send));
    act(() => result.current.invokeCustomScreenButton("screen.one", "old", "button.one"));
    const stale = (send.mock.calls[0]![0] as { operationId: string }).operationId;
    act(() => result.current.requestCustomScreen(id));
    const request = (send.mock.calls[1]![0] as { operationId: string }).operationId;
    act(() => {
      result.current.completeCustomScreenGet({
        type: "custom.screen.get.result", operationId: request, succeeded: true,
        screen: definition(id, revision),
      });
      result.current.completeCustomScreenInvoke({
        type: "custom.screen.invoke.result", operationId: stale, screenId: "screen.one",
        buttonId: "button.one", succeeded: false, code: "stale-screen", message: "Screen changed.",
      });
    });
    expect(result.current.customScreenDefinition).toEqual(definition(id, revision));
  });
});
