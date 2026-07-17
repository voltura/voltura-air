import { act, renderHook } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { ClientMessage } from "../protocol";
import { useConnectionSender, type PendingMovementAck } from "./useConnectionSender";

function createSender(bufferedAmount = 0) {
  const socket = {
    bufferedAmount,
    readyState: WebSocket.OPEN,
    send: vi.fn()
  } as unknown as WebSocket;
  const reconnect = vi.fn();
  const rescheduleHealthCheck = vi.fn();
  const options = {
    lastMovementAckAtRef: { current: 0 },
    lastUserActivityAtRef: { current: 0 },
    nextInputSequenceRef: { current: 1 },
    pendingInputAcksRef: { current: new Map<number, number>() },
    pendingMovementAckRef: { current: null as PendingMovementAck | null },
    reconnectRef: { current: reconnect },
    rescheduleHealthCheckRef: { current: rescheduleHealthCheck },
    socketRef: { current: socket },
    supportsInputAckRef: { current: true },
    supportsVolumeControlRef: { current: false }
  };
  const hook = renderHook(() => useConnectionSender(options));
  return { hook, options, reconnect, rescheduleHealthCheck, socket };
}

afterEach(() => {
  vi.restoreAllMocks();
});

describe("connection sender movement flow control", () => {
  it("bounds movement queued behind an acknowledgement barrier", () => {
    vi.spyOn(Date, "now").mockReturnValue(1_000);
    const { hook, options, socket } = createSender();
    const move = { type: "pointer.move", dx: 1, dy: 2 } satisfies ClientMessage;

    act(() => {
      for (let index = 0; index < 7; index += 1) {
        hook.result.current.send(move);
      }
    });

    expect(socket.send).toHaveBeenCalledTimes(5);
    expect(JSON.parse(vi.mocked(socket.send).mock.calls[0]![0] as string)).toMatchObject({ type: "pointer.move", seq: 1 });
    expect(options.pendingMovementAckRef.current).toEqual({ sequence: 1, followingMovementCount: 4 });
  });

  it("drops movement while the WebSocket send buffer is congested but preserves discrete input", () => {
    vi.spyOn(Date, "now").mockReturnValue(1_000);
    const { hook, rescheduleHealthCheck, socket } = createSender(1024);

    act(() => {
      hook.result.current.send({ type: "pointer.move", dx: 3, dy: 4 });
      hook.result.current.send({ type: "keyboard.special", key: "Enter" });
    });

    expect(socket.send).toHaveBeenCalledTimes(1);
    expect(JSON.parse(vi.mocked(socket.send).mock.calls[0]![0] as string)).toMatchObject({ type: "keyboard.special", key: "Enter" });
    expect(rescheduleHealthCheck).toHaveBeenCalledOnce();
  });

  it("does not reset the health timer for every movement frame", () => {
    vi.spyOn(Date, "now").mockReturnValue(1_000);
    const { hook, rescheduleHealthCheck } = createSender();

    act(() => {
      hook.result.current.send({ type: "pointer.move", dx: 1, dy: 1 });
    });

    expect(rescheduleHealthCheck).not.toHaveBeenCalled();
  });
});
