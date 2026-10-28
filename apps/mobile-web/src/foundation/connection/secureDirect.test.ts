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
  async function begin(signal?: AbortSignal) {
    vi.stubGlobal("WebSocket", MockSignalingSocket);
    vi.stubGlobal("RTCPeerConnection", MockPeerConnection);
    const pending = connectSecureDirect("r".repeat(22), signal);
    const signaling = MockSignalingSocket.instances[0]!;
    signaling.open();
    signaling.message(JSON.stringify({ type: "secure.offer", sdp: "v=0\r\n" }));
    await Promise.resolve();
    await Promise.resolve();
    return { pending, signaling, peer: MockPeerConnection.instances[0]! };
  }
  it("fails setup immediately when the peer fails before a channel exists", async () => {
    const { pending, peer } = await begin();
    let failed = false;
    const completed = pending.catch(() => {
      failed = true;
    });
    peer.fail();
    for (let tick = 0; tick < 12; tick++) {
      await Promise.resolve();
    }
    expect(failed).toBe(true);
    await completed;
    expect(peer.closed).toBe(true);
  });
  it("fails a stalled SDP step when its channel closes during setup", async () => {
    const description = vi
      .spyOn(MockPeerConnection.prototype, "createAnswer")
      .mockImplementationOnce(
        () =>
          new Promise(() => {
            /* Deliberately stalled native operation. */
          }),
      );
    const { pending, peer } = await begin();
    let failed = false;
    const completed = pending.catch(() => {
      failed = true;
    });
    const channel = new MockDataChannel("voltura-control");
    peer.provide(channel);
    channel.close();
    for (let tick = 0; tick < 10; tick++) {
      await Promise.resolve();
    }
    expect(failed).toBe(true);
    await completed;
    expect(peer.closed).toBe(true);
    description.mockRestore();
  });
  it("removes its ICE observer when setup is aborted", async () => {
    const abort = new AbortController();
    const { pending, peer } = await begin(abort.signal);
    peer.iceGatheringState = "gathering";
    const add = vi.spyOn(peer, "addEventListener"),
      remove = vi.spyOn(peer, "removeEventListener");
    for (let tick = 0; tick < 12; tick++) {
      await Promise.resolve();
    }
    const listener = add.mock.calls.find(([name]) => name === "icegatheringstatechange")?.[1];
    expect(listener).toBeDefined();
    const completed = pending.catch(() => undefined);
    abort.abort();
    await completed;
    expect(remove).toHaveBeenCalledWith("icegatheringstatechange", listener);
    add.mockRestore();
    remove.mockRestore();
  });
  it("does not return an already-open channel after a duplicate channel invalidates setup", async () => {
    const { pending, peer } = await begin();
    peer.iceGatheringState = "gathering";
    const accepted = new MockDataChannel("voltura-control");
    accepted.readyState = "open";
    peer.provide(accepted);
    peer.provide(new MockDataChannel("voltura-control"));
    let failed = false;
    const completed = pending.catch(() => {
      failed = true;
    });
    for (let tick = 0; tick < 12; tick++) {
      await Promise.resolve();
    }
    expect(failed).toBe(true);
    await completed;
    expect(accepted.closed).toBe(true);
  });
  it("closes a late DataChannel delivered after setup cancellation", async () => {
    const abort = new AbortController();
    const { pending, peer } = await begin(abort.signal);
    const completed = pending.catch(() => undefined);
    abort.abort();
    await completed;
    const late = new MockDataChannel("voltura-control");
    peer.provide(late);
    expect(late.closed).toBe(true);
  });
  it("rejects an opened channel that closes before SDP setup finishes", async () => {
    const { pending, peer } = await begin();
    const channel = new MockDataChannel("voltura-control");
    channel.readyState = "open";
    peer.provide(channel);
    channel.close();
    await expect(pending).rejects.toThrow();
    expect(peer.closed).toBe(true);
  });
  it("aborts stalled SDP work when signaling closes before the answer", async () => {
    vi.spyOn(MockPeerConnection.prototype, "createAnswer").mockImplementationOnce(
      () =>
        new Promise(() => {
          /* Deliberately stalled native operation. */
        }),
    );
    const { pending, signaling, peer } = await begin();
    let failed = false;
    const completed = pending.catch(() => {
      failed = true;
    });
    signaling.close();
    for (let tick = 0; tick < 12; tick++) {
      await Promise.resolve();
    }
    expect(failed).toBe(true);
    await completed;
    expect(peer.closed).toBe(true);
  });
  it("removes pending channel observers without waiting for a native close event", async () => {
    const abort = new AbortController();
    const { pending, peer } = await begin(abort.signal);
    const channel = new MockDataChannel("voltura-control");
    channel.close = () => {
      channel.readyState = "closing";
    };
    const remove = vi.spyOn(channel, "removeEventListener");
    peer.provide(channel);
    const completed = pending.catch(() => undefined);
    abort.abort();
    await completed;
    for (const type of ["open", "close", "error"]) {
      expect(remove).toHaveBeenCalledWith(type, expect.any(Function));
    }
  });
  it("fails immediately for a queued DataChannel that has already closed", async () => {
    const { pending, peer } = await begin();
    const channel = new MockDataChannel("voltura-control");
    channel.readyState = "closed";
    let failed = false;
    const completed = pending.catch(() => {
      failed = true;
    });
    peer.provide(channel);
    for (let tick = 0; tick < 12; tick++) {
      await Promise.resolve();
    }
    expect(failed).toBe(true);
    await completed;
  });
  afterEach(() => {
    vi.restoreAllMocks();
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
