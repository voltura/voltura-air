import { afterEach, describe, expect, it, vi } from "vitest";
import { connectSecureDirect } from "./secureDirect";

class MockSignalingSocket extends EventTarget {
  static instances: MockSignalingSocket[] = [];
  static readonly CONNECTING = 0;
  static readonly OPEN = 1;
  static readonly CLOSING = 2;
  static readonly CLOSED = 3;
  readyState = MockSignalingSocket.CONNECTING;
  sent: string[] = [];
  constructor(readonly url: string | URL) {
    super();
    MockSignalingSocket.instances.push(this);
  }
  send(value: string) {
    this.sent.push(value);
  }
  close() {
    this.readyState = MockSignalingSocket.CLOSED;
    this.dispatchEvent(new CloseEvent("close"));
  }
  open() {
    this.readyState = MockSignalingSocket.OPEN;
    this.dispatchEvent(new Event("open"));
  }
  message(value: string) {
    this.dispatchEvent(new MessageEvent("message", { data: value }));
  }
}

class MockDataChannel extends EventTarget {
  binaryType: BinaryType = "blob";
  readyState: RTCDataChannelState = "connecting";
  closed = false;
  constructor(readonly label: string) {
    super();
  }
  close() {
    this.closed = true;
    this.readyState = "closed";
    this.dispatchEvent(new Event("close"));
  }
  open() {
    this.readyState = "open";
    this.dispatchEvent(new Event("open"));
  }
}

class MockPeerConnection extends EventTarget {
  static instances: MockPeerConnection[] = [];
  connectionState: RTCPeerConnectionState = "new";
  iceGatheringState: RTCIceGatheringState = "complete";
  localDescription: RTCSessionDescriptionInit | null = null;
  closed = false;
  constructor(readonly configuration: RTCConfiguration) {
    super();
    MockPeerConnection.instances.push(this);
  }
  setRemoteDescription() {
    return Promise.resolve();
  }
  createAnswer(): Promise<RTCSessionDescriptionInit> {
    return Promise.resolve({ type: "answer", sdp: "v=0\r\n" });
  }
  setLocalDescription(description: RTCSessionDescriptionInit) {
    this.localDescription = description;
    return Promise.resolve();
  }
  close() {
    this.closed = true;
  }
  fail() {
    this.connectionState = "failed";
    this.dispatchEvent(new Event("connectionstatechange"));
  }
  provide(channel: MockDataChannel) {
    const event = new Event("datachannel") as Event & { channel: RTCDataChannel };
    Object.defineProperty(event, "channel", { value: channel });
    this.dispatchEvent(event);
  }
}

describe("connectSecureDirect", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    MockSignalingSocket.instances = [];
    MockPeerConnection.instances = [];
  });

  it("sends one answer and keeps the peer alive after signaling closes", async () => {
    vi.stubGlobal("WebSocket", MockSignalingSocket);
    vi.stubGlobal("RTCPeerConnection", MockPeerConnection);
    const route = "r".repeat(22);
    const connection = connectSecureDirect(route);
    const signaling = MockSignalingSocket.instances[0]!;
    signaling.open();
    signaling.message(JSON.stringify({ type: "secure.offer", sdp: "v=0\r\n" }));
    await Promise.resolve();
    await Promise.resolve();
    const peer = MockPeerConnection.instances[0]!;
    const channel = new MockDataChannel("voltura-control");
    peer.provide(channel);
    channel.open();

    const established = await connection;
    expect(JSON.parse(signaling.sent[0]!)).toEqual({ type: "secure.answer", sdp: "v=0\r\n" });
    signaling.close();
    expect(peer.closed).toBe(false);
    expect(channel.closed).toBe(false);

    established.cleanup();
    expect(peer.closed).toBe(true);
    expect(channel.closed).toBe(true);
  });

  it("closes the controller channel when the peer connection permanently fails", async () => {
    vi.stubGlobal("WebSocket", MockSignalingSocket);
    vi.stubGlobal("RTCPeerConnection", MockPeerConnection);
    const connection = connectSecureDirect("r".repeat(22));
    const signaling = MockSignalingSocket.instances[0]!;
    signaling.open();
    signaling.message(JSON.stringify({ type: "secure.offer", sdp: "v=0\r\n" }));
    await Promise.resolve();
    await Promise.resolve();
    const peer = MockPeerConnection.instances[0]!;
    const channel = new MockDataChannel("voltura-control");
    peer.provide(channel);
    channel.open();
    const established = await connection;

    peer.fail();

    expect(channel.closed).toBe(true);
    established.cleanup();
  });
});
