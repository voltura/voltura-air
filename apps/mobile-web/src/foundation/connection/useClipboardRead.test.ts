import { act, renderHook } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { ClipboardGetResultMessage, ClientMessage } from "../protocol/messages";
import type { ConnectionState } from "./connectionTypes";
import { useClipboardRead } from "./useClipboardRead";

describe("useClipboardRead", () => {
  afterEach(() => vi.useRealTimers());

  it("keeps visible and device clipboard reads separate", async () => {
    const send = vi.fn<(payload: ClientMessage) => void>();
    const { result } = renderHook(() => useClipboardRead("paired", 1, send));

    let deviceResult: Promise<ClipboardGetResultMessage> | null = null;
    act(() => {
      deviceResult = result.current.requestClipboardReadForDevice();
    });
    const sentRequest = send.mock.calls[0]![0];
    if (sentRequest.type !== "clipboard.get") {
      throw new Error("Expected a clipboard.get request.");
    }
    const operationId = sentRequest.operationId;

    act(() => {
      result.current.completeClipboardRead({
        type: "clipboard.get.result",
        operationId,
        succeeded: true,
        message: "Read",
        text: "Fresh PC text",
      });
    });

    await expect(deviceResult).resolves.toMatchObject({ succeeded: true, text: "Fresh PC text" });
    expect(result.current.clipboardText).toBe("");
    expect(result.current.clipboardReadResult).toBeNull();
  });

  it("supersedes an unfinished device copy with a fresh request", async () => {
    const send = vi.fn<(payload: ClientMessage) => void>();
    const { result } = renderHook(() => useClipboardRead("paired", 1, send));

    let first: ReturnType<typeof result.current.requestClipboardReadForDevice> = null;
    let second: ReturnType<typeof result.current.requestClipboardReadForDevice> = null;
    act(() => {
      first = result.current.requestClipboardReadForDevice();
      second = result.current.requestClipboardReadForDevice();
    });

    await expect(first).resolves.toMatchObject({
      succeeded: false,
      code: "VAIR-CLIPBOARD-SUPERSEDED",
    });
    const sentRequest = send.mock.calls[1]![0];
    if (sentRequest.type !== "clipboard.get") {
      throw new Error("Expected a clipboard.get request.");
    }
    const secondOperationId = sentRequest.operationId;
    act(() => {
      result.current.completeClipboardRead({
        type: "clipboard.get.result",
        operationId: secondOperationId,
        succeeded: true,
        message: "Read",
        text: "Newest",
      });
    });
    await expect(second).resolves.toMatchObject({ succeeded: true, text: "Newest" });
  });

  it("settles a device clipboard read on timeout and disconnect", async () => {
    vi.useFakeTimers();
    const send = vi.fn<(payload: ClientMessage) => void>();
    const { result, rerender } = renderHook(
      ({ state }: { state: ConnectionState }) => useClipboardRead(state, 1, send),
      { initialProps: { state: "paired" as ConnectionState } },
    );

    let timedOut: ReturnType<typeof result.current.requestClipboardReadForDevice> = null;
    act(() => {
      timedOut = result.current.requestClipboardReadForDevice();
    });
    await act(() => vi.advanceTimersByTime(5000));
    await expect(timedOut).resolves.toMatchObject({ code: "VAIR-CLIPBOARD-RESPONSE-TIMEOUT" });

    let disconnected: ReturnType<typeof result.current.requestClipboardReadForDevice> = null;
    act(() => {
      disconnected = result.current.requestClipboardReadForDevice();
    });
    rerender({ state: "disconnected" });
    await expect(disconnected).resolves.toMatchObject({ code: "VAIR-CLIPBOARD-DISCONNECTED" });
  });

  it("cancels a pending device clipboard read when its screen leaves", async () => {
    const send = vi.fn<(payload: ClientMessage) => void>();
    const { result } = renderHook(() => useClipboardRead("paired", 1, send));
    let pending: ReturnType<typeof result.current.requestClipboardReadForDevice> = null;
    act(() => {
      pending = result.current.requestClipboardReadForDevice();
    });

    act(() => {
      result.current.cancelClipboardReadForDevice();
    });

    await expect(pending).resolves.toMatchObject({ code: "VAIR-CLIPBOARD-CANCELED" });
  });

  it("retires visible text and pending reads when the authenticated connection changes", async () => {
    const send = vi.fn<(payload: ClientMessage) => void>();
    const { result, rerender } = renderHook(({ epoch }) => useClipboardRead("paired", epoch, send), {
      initialProps: { epoch: 1 },
    });
    const response = (operationId: string): ClipboardGetResultMessage => ({
      type: "clipboard.get.result",
      operationId,
      succeeded: true,
      message: "Read",
      text: "PC text",
    });
    let firstId: string | null = null;
    act(() => {
      firstId = result.current.requestClipboardRead();
    });
    act(() => {
      expect(result.current.completeClipboardRead(response(firstId!))).toBe(true);
    });
    expect(result.current.clipboardText).toBe("PC text");
    let staleId: string | null = null;
    let pending: ReturnType<typeof result.current.requestClipboardReadForDevice> = null;
    act(() => {
      staleId = result.current.requestClipboardRead();
      pending = result.current.requestClipboardReadForDevice();
    });

    rerender({ epoch: 2 });

    expect(result.current.clipboardText).toBe("");
    expect(result.current.pendingClipboardRead).toBe(false);
    expect(result.current.completeClipboardRead(response(staleId!))).toBe(false);
    await expect(pending).resolves.toMatchObject({ code: "VAIR-CLIPBOARD-DISCONNECTED" });
    act(() => {
      expect(result.current.requestClipboardRead()).toEqual(expect.any(String));
    });
  });
});
