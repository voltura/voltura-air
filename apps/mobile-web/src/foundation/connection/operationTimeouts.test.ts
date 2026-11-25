import { act, renderHook } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { ClientMessage } from "../protocol/messages";
import { useAppLaunch } from "./useAppLaunch";
import { useAwakeControl } from "./useAwakeControl";
import { useClipboardRead } from "./useClipboardRead";
import { usePowerControl } from "./usePowerControl";
import { useTextTransfer } from "./useTextTransfer";
import { useUrlOpen } from "./useUrlOpen";

function useLaunchCase(send: (message: ClientMessage) => void, epoch = 1) {
  const hook = useAppLaunch("paired", epoch, send);
  return {
    request: () => hook.requestAppLaunch("notes"),
    pending: hook.pendingAppLaunchId !== null,
    feedback: hook.appLaunchResult,
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
  const hook = useClipboardRead("paired", 1, send);
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
function useTextCase(send: (message: ClientMessage) => void, epoch = 1) {
  const hook = useTextTransfer("paired", epoch, send);
  return {
    request: () => hook.requestTextTransfer("sample"),
    pending: hook.pendingTextTransfer,
    feedback: hook.textTransferResult,
    complete: (operationId: string) =>
      hook.completeTextTransfer({
        type: "text.send.result",
        operationId,
        succeeded: true,
        message: "done",
      }),
  };
}
function useUrlCase(send: (message: ClientMessage) => void, epoch = 1) {
  const hook = useUrlOpen("paired", epoch, send);
  return {
    request: () => hook.requestUrlOpen("https://example.test"),
    pending: hook.pendingUrlOpen,
    feedback: hook.urlOpenResult,
    complete: (operationId: string) =>
      hook.completeUrlOpen({
        type: "url.open.result",
        operationId,
        succeeded: true,
        message: "done",
      }),
  };
}

function useAwakeCase(send: (message: ClientMessage) => void, epoch = 1) {
  const hook = useAwakeControl("paired", epoch, send);
  return {
    request: () => hook.requestAwakeChange(false),
    pending: hook.pendingAwakeChange !== null,
    feedback: hook.awakeResult,
    complete: (operationId: string) => hook.completeAwakeChange({
      type: "awake.result", operationId, enabled: false, succeeded: true, message: "done",
    }),
  };
}

function usePowerCase(send: (message: ClientMessage) => void, epoch = 1) {
  const hook = usePowerControl("paired", epoch, send);
  return {
    request: () => hook.requestPowerAction("lock"),
    pending: hook.pendingPowerAction !== null,
    feedback: hook.powerActionResult,
    complete: (operationId: string) => hook.completePowerAction({
      type: "system.power.result", operationId, action: "lock", succeeded: true, message: "done",
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

describe("authenticated command lifetime", () => {
  it.each([
    ["application launch", useLaunchCase],
    ["Keep awake", useAwakeCase],
    ["power", usePowerCase],
    ["text transfer", useTextCase],
    ["URL open", useUrlCase],
  ] as const)("retires pending %s commands and feedback when a paired socket is replaced", (_, useCase) => {
    const send = vi.fn();
    const { result, rerender } = renderHook(({ epoch }) => useCase(send, epoch), {
      initialProps: { epoch: 1 },
    });
    act(() => { result.current.request(); });
    const stale = (send.mock.calls[0]![0] as { operationId: string }).operationId;
    expect(result.current.pending).toBe(true);

    rerender({ epoch: 2 });

    expect(result.current.pending).toBe(false);
    expect(result.current.complete(stale)).toBe(false);
    act(() => { result.current.request(); });
    const current = (send.mock.calls[1]![0] as { operationId: string }).operationId;
    act(() => { expect(result.current.complete(current)).toBe(true); });
    expect(result.current.feedback?.succeeded).toBe(true);

    rerender({ epoch: 3 });
    expect(result.current.feedback).toBeNull();
  });
});
