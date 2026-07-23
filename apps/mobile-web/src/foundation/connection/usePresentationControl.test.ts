import { act, renderHook } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { ConnectionState } from "./connectionTypes";
import { usePresentationControl } from "./usePresentationControl";

describe("usePresentationControl", () => {
  afterEach(() => vi.useRealTimers());

  it("allows only one command in flight and completes only its matching result", () => {
    const send = vi.fn();
    const { result } = renderHook(() => usePresentationControl("paired", send));
    let operationId: string | null = null;

    act(() => {
      operationId = result.current.requestPresentationCommand("powerpoint", "next");
      result.current.requestPresentationCommand("powerpoint", "next");
    });

    expect(send).toHaveBeenCalledExactlyOnceWith({
      type: "presentation.command",
      operationId,
      target: "powerpoint",
      action: "next"
    });

    act(() => { result.current.completePresentationCommand({
      type: "presentation.command.result",
      operationId: "unrelated",
      target: "powerpoint",
      action: "next",
      succeeded: false,
      message: "Unrelated",
      laserPointerActive: false
    }); });
    expect(result.current.pendingPresentationCommand).not.toBeNull();

    act(() => { result.current.completePresentationCommand({
      type: "presentation.command.result",
      operationId: operationId!,
      target: "powerpoint",
      action: "next",
      succeeded: true,
      message: "Next slide command sent.",
      laserPointerActive: false
    }); });
    expect(result.current.pendingPresentationCommand).toBeNull();
    expect(result.current.presentationResult?.succeeded).toBe(true);
  });

  it("reports an acknowledgement timeout and stops pending work on disconnect", async () => {
    vi.useFakeTimers();
    const send = vi.fn();
    const { result, rerender } = renderHook(({ state }: { state: ConnectionState }) => usePresentationControl(state, send), {
      initialProps: { state: "paired" as ConnectionState }
    });

    await act(() => result.current.requestPresentationCommand("google-slides", "black"));
    await act(() => vi.advanceTimersByTime(5000));
    expect(result.current.presentationResult?.code).toBe("VAIR-PRESENTATION-RESPONSE-TIMEOUT");

    await act(() => result.current.requestPresentationCommand("google-slides", "next"));
    rerender({ state: "unavailable" });
    expect(result.current.pendingPresentationCommand).toBeNull();
    expect(result.current.presentationResult).toBeNull();
  });

  it("sends idempotent laser cleanup while another presenter command is pending", () => {
    const send = vi.fn();
    const { result } = renderHook(() => usePresentationControl("paired", send));

    act(() => {
      result.current.requestPresentationCommand("powerpoint", "next");
      result.current.requestPresentationCommand("pdf", "pointer", false);
    });

    expect(send).toHaveBeenCalledTimes(2);
    expect(send.mock.calls[1]?.[0]).toMatchObject({
      type: "presentation.command",
      target: "pdf",
      action: "pointer",
      enabled: false
    });
    expect(result.current.pendingPresentationCommand?.action).toBe("next");
  });
});
