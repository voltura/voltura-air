import { act, renderHook } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { ClientMessage } from "../protocol/messages";
import { useAppLaunch } from "./useAppLaunch";
import { useClipboardRead } from "./useClipboardRead";
import { useTextTransfer } from "./useTextTransfer";
import { useUrlOpen } from "./useUrlOpen";

function useLaunchCase(send: (message: ClientMessage) => void) {
  const hook = useAppLaunch("paired", send);
  return {
    request: () => hook.requestAppLaunch("notes"),
    pending: hook.pendingAppLaunchId !== null,
    complete: (operationId: string) =>
      hook.completeAppLaunch({
        type: "app.launch.result",
        operationId,
        actionId: "notes",
        succeeded: true,
        message: "done",
      }),
  };
}
function useClipboardCase(send: (message: ClientMessage) => void) {
  const hook = useClipboardRead("paired", send);
  return {
    request: hook.requestClipboardRead,
    pending: hook.pendingClipboardRead,
    complete: (operationId: string) =>
      hook.completeClipboardRead({
        type: "clipboard.get.result",
        operationId,
        succeeded: true,
        message: "done",
        text: "sample",
      }),
  };
}
function useTextCase(send: (message: ClientMessage) => void) {
  const hook = useTextTransfer("paired", send);
  return {
    request: () => hook.requestTextTransfer("sample"),
    pending: hook.pendingTextTransfer,
    complete: (operationId: string) =>
      hook.completeTextTransfer({
        type: "text.send.result",
        operationId,
        succeeded: true,
        message: "done",
      }),
  };
}
function useUrlCase(send: (message: ClientMessage) => void) {
  const hook = useUrlOpen("paired", send);
  return {
    request: () => hook.requestUrlOpen("https://example.test"),
    pending: hook.pendingUrlOpen,
    complete: (operationId: string) =>
      hook.completeUrlOpen({
        type: "url.open.result",
        operationId,
        succeeded: true,
        message: "done",
      }),
  };
}

afterEach(() => vi.useRealTimers());

describe("operation response deadline ownership", () => {
  it.each([
    ["launch", useLaunchCase, 5_000],
    ["clipboard", useClipboardCase, 5_000],
    ["text transfer", useTextCase, 15_000],
    ["URL", useUrlCase, 8_000],
  ] as const)(
    "rearms the %s deadline when completion and the next request share a render",
    (_, useCase, duration) => {
      vi.useFakeTimers();
      const send = vi.fn();
      const { result, unmount } = renderHook(() => useCase(send));
      act(() => {
        result.current.request();
      });
      const { operationId } = send.mock.calls[0]![0] as { operationId: string };
      act(() => {
        vi.advanceTimersByTime(2_000);
      });
      act(() => {
        expect(result.current.complete(operationId)).toBe(true);
        result.current.request();
      });
      expect(send).toHaveBeenCalledTimes(2);
      act(() => {
        vi.advanceTimersByTime(duration - 2_000);
      });
      expect(result.current.pending).toBe(true);
      act(() => {
        vi.advanceTimersByTime(2_000);
      });
      expect(result.current.pending).toBe(false);
      unmount();
      expect(vi.getTimerCount()).toBe(0);
    },
  );
});
