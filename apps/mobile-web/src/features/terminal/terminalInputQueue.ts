import {
  createTerminalInput,
  maximumTerminalPayloadBytes,
  splitTerminalInput,
} from "../../foundation/terminal/terminalRecords";

export const maximumQueuedTerminalInputBytes = 256 * 1024;
export const maximumTerminalBufferedAmountBytes = 1024 * 1024;

interface QueuedInput {
  payloadBytes: number;
  record: ArrayBuffer;
}

export class TerminalInputQueue {
  private channel: RTCDataChannel | null = null;
  private readonly queued: QueuedInput[] = [];
  private queuedBytes = 0;
  private pendingResize: ArrayBuffer | null = null;

  private readonly flushListener = () => this.flush();

  constructor(private readonly onTransportFailure: () => void = () => undefined) {}

  connect(channel: RTCDataChannel) {
    this.disconnect();
    this.channel = channel;
    channel.bufferedAmountLowThreshold = Math.max(
      0,
      maximumTerminalBufferedAmountBytes - maximumTerminalPayloadBytes - 9,
    );
    channel.addEventListener("bufferedamountlow", this.flushListener);
    this.flush();
  }

  disconnect(channel?: RTCDataChannel) {
    if (channel && this.channel !== channel) {
      return;
    }
    this.channel?.removeEventListener("bufferedamountlow", this.flushListener);
    this.channel = null;
  }

  clear() {
    this.queued.length = 0;
    this.queuedBytes = 0;
    this.pendingResize = null;
  }

  enqueue(bytes: Uint8Array): boolean {
    if (bytes.length === 0) {
      return true;
    }
    if (this.queuedBytes + bytes.length > maximumQueuedTerminalInputBytes) {
      return false;
    }
    for (const payload of splitTerminalInput(bytes)) {
      this.queued.push({ payloadBytes: payload.length, record: createTerminalInput(payload) });
    }
    this.queuedBytes += bytes.length;
    this.flush();
    return true;
  }

  enqueueResize(record: ArrayBuffer) {
    this.pendingResize = record;
    this.flush();
  }

  private flush() {
    const channel = this.channel;
    if (channel?.readyState !== "open") {
      return;
    }
    if (this.pendingResize) {
      if (
        channel.bufferedAmount + this.pendingResize.byteLength >
        maximumTerminalBufferedAmountBytes
      ) {
        return;
      }
      const resize = this.pendingResize;
      try {
        channel.send(resize);
      } catch {
        this.fail(channel);
        return;
      }
      if (this.pendingResize === resize) {
        this.pendingResize = null;
      }
    }
    while (this.queued[0]) {
      const next = this.queued[0];
      if (channel.bufferedAmount + next.record.byteLength > maximumTerminalBufferedAmountBytes) {
        return;
      }
      try {
        channel.send(next.record);
      } catch {
        this.fail(channel);
        return;
      }
      this.queued.shift();
      this.queuedBytes -= next.payloadBytes;
    }
  }

  private fail(channel: RTCDataChannel) {
    this.disconnect(channel);
    this.onTransportFailure();
  }
}

const encoder = new TextEncoder();

export function limitTerminalPaste(
  text: string,
  maximumBytes: number,
): {
  text: string;
  truncated: boolean;
} {
  const bytes = encoder.encode(text);
  if (bytes.length <= maximumBytes) {
    return { text, truncated: false };
  }
  const limited = new Uint8Array(maximumBytes);
  const { read } = encoder.encodeInto(text, limited);
  return { text: text.slice(0, read), truncated: true };
}
