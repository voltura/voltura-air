import { describe, expect, it } from "vitest";
import {
  consumeRelayRate,
  decodeEnvelope,
  maximumBufferedBytes,
  maximumDevicesPerRoom,
  maximumRelayPayloadBytes,
  relayEnvelopeKind,
  RelayRoom,
  type RelayRateState,
  type RelaySocket,
} from "../src/core/index";

class FakeSocket implements RelaySocket {
  bufferedAmount = 0;
  readonly sent: Uint8Array[] = [];
  closed?: { code: number; reason: string };
  send(value: Uint8Array): void {
    this.sent.push(value);
  }
  close(code: number, reason: string): void {
    this.closed = { code, reason };
  }
}

describe("RelayRoom", () => {
  it("forwards opaque device payloads through a session envelope", () => {
    const room = new RelayRoom();
    const host = new FakeSocket();
    const device = new FakeSocket();
    const sessionId = crypto.getRandomValues(new Uint8Array(16));
    expect(room.attachHost(host)).toBe(true);
    expect(room.attachDevice(device, sessionId)).toBe(true);
    expect(room.forwardDevicePayload(device, new Uint8Array([1, 2, 3]))).toBe(true);
    const envelope = decodeEnvelope(host.sent[0]!);
    expect(envelope?.kind).toBe(relayEnvelopeKind.binary);
    expect(envelope?.sessionId).toEqual(sessionId);
    expect(envelope?.payload).toEqual(new Uint8Array([1, 2, 3]));
  });

  it("forwards a maximum encrypted application frame and rejects anything larger", () => {
    const room = new RelayRoom();
    const host = new FakeSocket();
    const device = new FakeSocket();
    const sessionId = crypto.getRandomValues(new Uint8Array(16));
    room.attachHost(host);
    room.attachDevice(device, sessionId);

    expect(room.forwardDevicePayload(device, new Uint8Array(maximumRelayPayloadBytes))).toBe(true);
    expect(room.forwardDevicePayload(device, new Uint8Array(maximumRelayPayloadBytes + 1))).toBe(
      false,
    );
    expect(device.closed?.reason).toBe("Message is too large");
  });

  it("closes devices when the only host disconnects", () => {
    const room = new RelayRoom();
    const host = new FakeSocket();
    const device = new FakeSocket();
    room.attachHost(host);
    room.attachDevice(device, new Uint8Array(16));
    room.detachHost(host);
    expect(device.closed?.reason).toBe("Host disconnected");
    expect(room.deviceCount).toBe(0);
  });

  it("enforces the room device bound", () => {
    const room = new RelayRoom();
    room.attachHost(new FakeSocket());
    for (let index = 0; index < maximumDevicesPerRoom; index += 1) {
      const sessionId = new Uint8Array(16);
      new DataView(sessionId.buffer).setUint32(12, index);
      expect(room.attachDevice(new FakeSocket(), sessionId)).toBe(true);
    }
    const overflow = new FakeSocket();
    expect(room.attachDevice(overflow, crypto.getRandomValues(new Uint8Array(16)))).toBe(false);
    expect(overflow.closed?.reason).toBe("Room is full");
  });

  it("closes a slow consumer instead of growing an unbounded queue", () => {
    const room = new RelayRoom();
    const host = new FakeSocket();
    const device = new FakeSocket();
    const sessionId = new Uint8Array(16);
    room.attachHost(host);
    room.attachDevice(device, sessionId);
    device.bufferedAmount = Number.MAX_SAFE_INTEGER;

    expect(room.forwardHostPayload(sessionId, new Uint8Array([1]))).toBe(false);
    expect(device.closed?.reason).toBe("Relay backpressure limit exceeded");
  });

  it("includes the outgoing frame when enforcing the backpressure bound", () => {
    const room = new RelayRoom();
    const host = new FakeSocket();
    const device = new FakeSocket();
    const sessionId = new Uint8Array(16);
    room.attachHost(host);
    room.attachDevice(device, sessionId);
    device.bufferedAmount = maximumBufferedBytes - 1;

    expect(room.forwardHostPayload(sessionId, new Uint8Array([1, 2]))).toBe(false);
    expect(device.closed?.reason).toBe("Relay backpressure limit exceeded");
  });

  it("applies one shared bounded rate policy to adapter state", () => {
    let state: RelayRateState | undefined;
    for (let index = 0; index < 256; index += 1) {
      const result = consumeRelayRate(state, "device", 1, 1000);
      expect(result.allowed).toBe(true);
      state = result.state;
    }
    expect(consumeRelayRate(state, "device", 1, 1000).allowed).toBe(false);
    expect(consumeRelayRate(state, "device", 1, 2000).allowed).toBe(true);
  });
});
