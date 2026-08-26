import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { p256 } from "@noble/curves/nist.js";
import { base64Url, createPairingKeyMaterial } from "./pairingCredentials";
import { revokePcPairing } from "./relayPairingRevocation";
import { RelayEncryptedChannel } from "./relaySessionCrypto";

const secureDirectMock = vi.hoisted(() => ({ connect: vi.fn() }));
vi.mock("./secureDirect", () => ({ connectSecureDirect: secureDirectMock.connect }));

function decodeBase64Url(value: string): Uint8Array {
  const binary = atob(
    value
      .replace(/-/g, "+")
      .replace(/_/g, "/")
      .padEnd(value.length + ((4 - (value.length % 4)) % 4), "="),
  );
  return Uint8Array.from(binary, (character) => character.charCodeAt(0));
}

describe("createPairingKeyMaterial", () => {
  it("exports the protocol-defined uncompressed P-256 public key", () => {
    const key = createPairingKeyMaterial();

    expect(key).not.toBeNull();
    const publicKey = decodeBase64Url(key!.reconnectPublicKey);
    expect(publicKey).toHaveLength(65);
    expect(publicKey[0]).toBe(0x04);
  });
});

class MockWebSocket {
  static CONNECTING = 0;
  static OPEN = 1;
  static CLOSING = 2;
  static CLOSED = 3;
  static instances: MockWebSocket[] = [];

  readyState = MockWebSocket.CONNECTING;
  private readonly listeners = new Map<string, ((event: MessageEvent) => void)[]>();

  constructor(url: string) {
    void url;
    MockWebSocket.instances.push(this);
  }

  addEventListener(type: string, listener: (event: MessageEvent) => void) {
    this.listeners.set(type, [...(this.listeners.get(type) ?? []), listener]);
  }

  removeEventListener(type: string, listener: (event: MessageEvent) => void) {
    this.listeners.set(
      type,
      (this.listeners.get(type) ?? []).filter((candidate) => candidate !== listener),
    );
  }

  close = vi.fn(() => {
    this.readyState = MockWebSocket.CLOSED;
  });
  send = vi.fn();

  dispatch(type: string, data?: unknown) {
    for (const listener of this.listeners.get(type) ?? []) {
      listener(type === "close" ? (data as MessageEvent) : ({ data } as MessageEvent));
    }
  }
}

class MockDataChannel extends EventTarget {
  readyState: RTCDataChannelState = "open";
  send = vi.fn();
  close = vi.fn(() => {
    this.readyState = "closed";
    this.dispatchEvent(new Event("close"));
  });

  message(data: string) {
    this.dispatchEvent(new MessageEvent("message", { data }));
  }
}

describe("revokePcPairing", () => {
  beforeEach(() => {
    MockWebSocket.instances = [];
    secureDirectMock.connect.mockReset();
    const items = new Map<string, string>();
    vi.stubGlobal("localStorage", {
      clear: () => {
        items.clear();
      },
      getItem: (key: string) => items.get(key) ?? null,
      removeItem: (key: string) => {
        items.delete(key);
      },
      setItem: (key: string, value: string) => {
        items.set(key, value);
      },
    });
    vi.stubGlobal("WebSocket", MockWebSocket);
    vi.stubGlobal(
      "matchMedia",
      vi.fn(() => ({ matches: false })),
    );
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("closes a rejected best-effort revocation socket", async () => {
    localStorage.setItem(
      "voltura-air.reconnect-key.client-a.pc-b",
      createPairingKeyMaterial()!.privateKey,
    );
    const revocation = revokePcPairing(
      { customName: false, id: "pc-b", name: "PC B", url: "http://pc-b.local:51395" },
      "client-a",
      "Phone",
      null,
    );
    const socket = MockWebSocket.instances[0]!;

    socket.readyState = MockWebSocket.OPEN;
    socket.dispatch("open");
    socket.dispatch("message", JSON.stringify({ type: "pair.rejected", reason: "device-revoked" }));

    await expect(revocation).resolves.toBe(false);
    expect(socket.close).toHaveBeenCalledOnce();
  });

  it("bounds an unopened best-effort revocation socket", async () => {
    vi.useFakeTimers();
    localStorage.setItem(
      "voltura-air.reconnect-key.client-a.pc-b",
      createPairingKeyMaterial()!.privateKey,
    );
    const revocation = revokePcPairing(
      { customName: false, id: "pc-b", name: "PC B", url: "http://pc-b.local:51395" },
      "client-a",
      "Phone",
      null,
    );
    const socket = MockWebSocket.instances[0]!;

    vi.advanceTimersByTime(10_000);

    await expect(revocation).resolves.toBe(false);
    expect(socket.close).toHaveBeenCalledOnce();
  });

  it("waits for encrypted delivery and host closure on an active relay session", async () => {
    const activeSocket = new MockWebSocket("wss://relay.test/ws") as unknown as WebSocket;
    (activeSocket as unknown as MockWebSocket).readyState = MockWebSocket.OPEN;
    let completeSend: (() => void) | undefined;
    const encryptedSend = vi.fn(
      () =>
        new Promise<void>((resolve) => {
          completeSend = resolve;
        }),
    );
    let completed = false;

    const revocation = revokePcPairing(
      {
        customName: false,
        id: "pc-b",
        name: "PC B",
        url: "https://relay.test",
        transportMode: "relay",
      },
      "client-a",
      "Phone",
      activeSocket,
      encryptedSend,
    ).then((result) => {
      completed = true;
      return result;
    });
    (activeSocket as unknown as MockWebSocket).dispatch("close", {
      code: 1000,
      reason: "Host closed session",
    });

    await Promise.resolve();
    expect(completed).toBe(false);
    completeSend!();
    await expect(revocation).resolves.toBe(true);
    expect(encryptedSend).toHaveBeenCalledWith(JSON.stringify({ type: "pair.disconnect" }));
  });

  it("does not treat an abnormal network close as revocation confirmation", async () => {
    const activeSocket = new MockWebSocket("wss://relay.test/ws") as unknown as WebSocket;
    (activeSocket as unknown as MockWebSocket).readyState = MockWebSocket.OPEN;

    const revocation = revokePcPairing(
      {
        customName: false,
        id: "pc-b",
        name: "PC B",
        url: "https://relay.test",
        transportMode: "relay",
      },
      "client-a",
      "Phone",
      activeSocket,
      () => Promise.resolve(),
    );
    await Promise.resolve();
    (activeSocket as unknown as MockWebSocket).dispatch("close", { code: 1006, reason: "" });

    await expect(revocation).resolves.toBe(false);
  });

  it("requires a host acknowledgement before confirming active Secure Direct revocation", async () => {
    const channel = new MockDataChannel();
    const revocation = revokePcPairing(
      {
        customName: false,
        id: "pc-b",
        name: "PC B",
        url: "https://voltura.se/s/rrrrrrrrrrrrrrrrrrrrrr",
        transportMode: "secure-direct",
      },
      "client-a",
      "Phone",
      channel as unknown as RTCDataChannel,
    );

    expect(channel.send).toHaveBeenCalledWith(JSON.stringify({ type: "pair.disconnect" }));
    channel.close();

    await expect(revocation).resolves.toBe(false);
  });

  it("confirms active Secure Direct revocation after the host acknowledgement", async () => {
    const channel = new MockDataChannel();
    const revocation = revokePcPairing(
      {
        customName: false,
        id: "pc-b",
        name: "PC B",
        url: "https://voltura.se/s/rrrrrrrrrrrrrrrrrrrrrr",
        transportMode: "secure-direct",
      },
      "client-a",
      "Phone",
      channel as unknown as RTCDataChannel,
    );

    channel.message(JSON.stringify({ type: "pair.disconnect.accepted" }));

    await expect(revocation).resolves.toBe(true);
  });

  it("rejects an inactive Secure Direct peer whose accepted identity does not match the saved PC", async () => {
    const channel = new MockDataChannel();
    const cleanup = vi.fn();
    secureDirectMock.connect.mockResolvedValue({ channel, cleanup });
    localStorage.setItem(
      "voltura-air.reconnect-key.client-a.pc-b",
      createPairingKeyMaterial()!.privateKey,
    );
    const revocation = revokePcPairing(
      {
        customName: false,
        hostIdentityFingerprint: "f".repeat(22),
        id: "pc-b",
        name: "PC B",
        relayRouteId: "r".repeat(22),
        transportMode: "secure-direct",
        url: `https://voltura.se/s/${"r".repeat(22)}`,
      },
      "client-a",
      "Phone",
      null,
    );
    await vi.waitFor(() => {
      expect(channel.send).toHaveBeenCalled();
    });
    channel.message(
      JSON.stringify({ type: "pair.challenge", clientId: "client-a", challenge: "c".repeat(43) }),
    );
    channel.message(
      JSON.stringify({
        type: "pair.accepted",
        clientId: "client-a",
        pcName: "Wrong PC",
        paired: true,
        hostIdentity: { publicKey: "p".repeat(87), fingerprint: "g".repeat(22) },
      }),
    );

    await expect(revocation).resolves.toBe(false);
    expect(channel.send).not.toHaveBeenCalledWith(JSON.stringify({ type: "pair.disconnect" }));
    expect(cleanup).toHaveBeenCalledOnce();
  });

  it("completes relay key exchange before revoking an inactive pairing", async () => {
    const hostIdentity = p256.keygen();
    const hostEphemeral = p256.keygen();
    const routeId = "r".repeat(22);
    const hostIdentityPublicKey = base64Url(p256.getPublicKey(hostIdentity.secretKey, false));
    const hostEphemeralPublicKey = base64Url(p256.getPublicKey(hostEphemeral.secretKey, false));
    const nonce = base64Url(new Uint8Array(32).fill(7));
    localStorage.setItem(
      "voltura-air.reconnect-key.client-a.pc-b",
      createPairingKeyMaterial()!.privateKey,
    );
    const revocation = revokePcPairing(
      {
        customName: false,
        hostIdentityPublicKey,
        id: "pc-b",
        name: "PC B",
        relayRouteId: routeId,
        transportMode: "relay",
        url: "https://relay.test",
      },
      "client-a",
      "Phone",
      null,
    );
    const socket = MockWebSocket.instances[0]!;
    socket.readyState = MockWebSocket.OPEN;
    socket.dispatch("open");
    socket.dispatch(
      "message",
      JSON.stringify({
        type: "pair.challenge",
        clientId: "client-a",
        challenge: base64Url(new Uint8Array(32).fill(5)),
      }),
    );
    socket.dispatch(
      "message",
      JSON.stringify({
        type: "session.key.challenge",
        routeId,
        clientId: "client-a",
        hostEphemeralPublicKey,
        nonce,
      }),
    );
    const proof = JSON.parse(String(socket.send.mock.calls.at(-1)![0])) as {
      clientEphemeralPublicKey: string;
    };
    const transcriptText = [
      "voltura-air-relay-session-v1",
      routeId,
      "client-a",
      hostIdentityPublicKey,
      hostEphemeralPublicKey,
      proof.clientEphemeralPublicKey,
      nonce,
    ].join("\n");
    const transcript = new TextEncoder().encode(transcriptText);
    socket.dispatch(
      "message",
      JSON.stringify({
        type: "session.key.accepted",
        signature: base64Url(p256.sign(transcript, hostIdentity.secretKey, { lowS: false })),
      }),
    );
    const shared = p256
      .getSharedSecret(
        hostEphemeral.secretKey,
        decodeBase64Url(proof.clientEphemeralPublicKey),
        false,
      )
      .slice(1, 33);
    const hostChannel = RelayEncryptedChannel.createHostForConformance(shared, transcript);
    let acceptedFrame: ArrayBuffer | null = null;
    await hostChannel.send(
      (frame) => {
        acceptedFrame = frame;
      },
      JSON.stringify({
        type: "pair.accepted",
        clientId: "client-a",
        pcName: "PC B",
        paired: true,
      }),
    );
    socket.dispatch("message", acceptedFrame);
    await vi.waitFor(() => {
      expect(socket.send.mock.calls.at(-1)![0]).toBeInstanceOf(ArrayBuffer);
    });
    const disconnectFrame = socket.send.mock.calls.at(-1)![0] as ArrayBuffer;

    expect(await hostChannel.decryptText(disconnectFrame)).toBe(
      JSON.stringify({ type: "pair.disconnect" }),
    );
    socket.dispatch("close", { code: 1000, reason: "Host closed session" });
    await expect(revocation).resolves.toBe(true);
  });
});
