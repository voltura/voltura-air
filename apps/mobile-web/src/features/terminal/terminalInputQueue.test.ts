import { describe, expect, it, vi } from "vitest";
import {
  limitTerminalPaste,
  maximumQueuedTerminalInputBytes,
  maximumTerminalBufferedAmountBytes,
  TerminalInputQueue,
} from "./terminalInputQueue";

class FakeChannel {
  readonly addEventListener = vi.fn();
  bufferedAmount = 0;
  bufferedAmountLowThreshold = 0;
  readonly removeEventListener = vi.fn();
  readyState = "open";
  readonly send = vi.fn();
}

describe("TerminalInputQueue", () => {
  it("keeps WebRTC buffering bounded and flushes after bufferedamountlow", () => {
    const queue = new TerminalInputQueue();
    const channel = new FakeChannel();
    channel.bufferedAmount = maximumTerminalBufferedAmountBytes;
    queue.connect(channel as unknown as RTCDataChannel);

    expect(queue.enqueue(new Uint8Array(64 * 1024))).toBe(true);
    expect(channel.send).not.toHaveBeenCalled();

    channel.bufferedAmount = 0;
    const listener = channel.addEventListener.mock.calls.find(
      ([type]) => type === "bufferedamountlow",
    )?.[1] as (() => void) | undefined;
    listener?.();
    expect(channel.send).toHaveBeenCalledTimes(4);
  });

  it("rejects input beyond the explicit 256 KiB queue instead of growing", () => {
    const queue = new TerminalInputQueue();
    expect(queue.enqueue(new Uint8Array(maximumQueuedTerminalInputBytes))).toBe(true);
    expect(queue.enqueue(Uint8Array.of(1))).toBe(false);
  });

  it("reports a transport failure when DataChannel send throws", () => {
    const failed = vi.fn();
    const queue = new TerminalInputQueue(failed);
    const channel = new FakeChannel();
    channel.send.mockImplementation(() => {
      throw new DOMException("closed", "OperationError");
    });
    queue.connect(channel as unknown as RTCDataChannel);

    expect(queue.enqueue(Uint8Array.of(1))).toBe(true);
    expect(failed).toHaveBeenCalledOnce();
    expect(channel.removeEventListener).toHaveBeenCalledWith(
      "bufferedamountlow",
      expect.any(Function),
    );
  });

  it("coalesces resize records behind the same WebRTC buffer cap", () => {
    const queue = new TerminalInputQueue();
    const channel = new FakeChannel();
    channel.bufferedAmount = maximumTerminalBufferedAmountBytes;
    queue.connect(channel as unknown as RTCDataChannel);
    const first = new ArrayBuffer(5);
    const latest = new ArrayBuffer(5);

    queue.enqueueResize(first);
    queue.enqueueResize(latest);
    expect(channel.send).not.toHaveBeenCalled();

    channel.bufferedAmount = 0;
    const listener = channel.addEventListener.mock.calls.find(
      ([type]) => type === "bufferedamountlow",
    )?.[1] as (() => void) | undefined;
    listener?.();
    expect(channel.send).toHaveBeenCalledOnce();
    expect(channel.send).toHaveBeenCalledWith(latest);
  });

  it("limits paste without splitting a UTF-8 code point", () => {
    const limited = limitTerminalPaste(`${"a".repeat(65_535)}å`, 65_536);
    expect(limited.truncated).toBe(true);
    expect(new TextEncoder().encode(limited.text)).toHaveLength(65_535);
  });
});
