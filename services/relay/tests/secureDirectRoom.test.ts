import { afterAll, beforeAll, describe, expect, it, vi } from "vitest";
import {
  createHostTranscript,
  decodeEnvelope,
  deriveRouteId,
  encodeBase64Url,
  encodeEnvelope,
  maximumControlMessageBytes,
  maximumInnerMessageBytes,
  maximumDevicesPerRoom,
  relayClose,
  relayEnvelopeKind
} from "../src/core/index";

vi.mock("cloudflare:workers", () => ({
  DurableObject: class {
    protected readonly ctx: unknown;
    protected readonly env: unknown;

    constructor(ctx: unknown, env: unknown) {
      this.ctx = ctx;
      this.env = env;
    }
  }
}));

type WorkerModule = typeof import("../src/cloudflare/worker");

class TestSocket {
  static readonly CONNECTING = 0;
  static readonly OPEN = 1;
  static readonly CLOSING = 2;
  static readonly CLOSED = 3;

  readyState = TestSocket.OPEN;
  bufferedAmount = 0;
  readonly sent: (string | ArrayBuffer | Uint8Array)[] = [];
  closeCode: number | undefined;
  closeReason: string | undefined;
  private attachment: unknown;

  send(value: string | ArrayBuffer | Uint8Array): void { this.sent.push(value); }
  close(code?: number, reason?: string): void {
    this.closeCode = code;
    this.closeReason = reason;
    this.readyState = TestSocket.CLOSED;
  }
  serializeAttachment(value: unknown): void { this.attachment = structuredClone(value); }
  deserializeAttachment(): unknown { return structuredClone(this.attachment); }
}

class TestWebSocketPair {
  readonly 0 = new TestSocket();
  readonly 1 = new TestSocket();
}

class TestResponse {
  readonly status: number;
  readonly webSocket: TestSocket | undefined;

  constructor(_body?: unknown, init: { status?: number; webSocket?: TestSocket } = {}) {
    this.status = init.status ?? 200;
    this.webSocket = init.webSocket;
  }

  static json(): TestResponse { return new TestResponse(null, { status: 200 }); }
}

class TestContext {
  private readonly sockets: { socket: TestSocket; tags: string[] }[] = [];
  readonly storage = {
    alarm: null as number | null,
    deleteAlarm: vi.fn(async () => { this.storage.alarm = null; }),
    setAlarm: vi.fn(async (deadline: number) => { this.storage.alarm = deadline; })
  };

  acceptWebSocket(socket: TestSocket, tags: string[]): void { this.sockets.push({ socket, tags }); }
  getWebSockets(tag?: string): TestSocket[] {
    return this.sockets
      .filter(({ socket, tags }) => socket.readyState === TestSocket.OPEN && (!tag || tags.includes(tag)))
      .map(({ socket }) => socket);
  }
}

const originalResponse = globalThis.Response;
const originalWebSocket = globalThis.WebSocket;
const testGlobals = globalThis as typeof globalThis & { WebSocketPair?: unknown };
const originalWebSocketPair = testGlobals.WebSocketPair;
let worker: WorkerModule;

beforeAll(async () => {
  globalThis.Response = TestResponse as unknown as typeof Response;
  globalThis.WebSocket = TestSocket as unknown as typeof WebSocket;
  testGlobals.WebSocketPair = TestWebSocketPair;
  worker = await import("../src/cloudflare/worker");
});

afterAll(() => {
  globalThis.Response = originalResponse;
  globalThis.WebSocket = originalWebSocket;
  if (originalWebSocketPair === undefined) delete testGlobals.WebSocketPair;
  else testGlobals.WebSocketPair = originalWebSocketPair;
});

describe("Secure Direct room", () => {
  it("authenticates the route owner and routes exactly one offer and answer", async () => {
    const { room, context, host, routeId } = await authenticatedRoom();
    const deviceResponse = await room.fetch(internalRequest("secure-device", routeId));
    expect(deviceResponse.status).toBe(101);
    const device = context.getWebSockets("secure-device")[0]!;
    const connected = decodeSentEnvelope(host.sent.pop());
    expect(connected.kind).toBe(relayEnvelopeKind.connected);
    expect(connected.payload).toHaveLength(16);

    const offer = JSON.stringify({ type: "secure.offer", sdp: "v=0\r\n" });
    await room.webSocketMessage(host as unknown as WebSocket,
      Uint8Array.from(encodeEnvelope(connected.sessionId, new TextEncoder().encode(offer), relayEnvelopeKind.text)).buffer);
    expect(device.sent).toEqual([offer]);

    const answer = JSON.stringify({ type: "secure.answer", sdp: "v=0\r\n" });
    await room.webSocketMessage(device as unknown as WebSocket, answer);
    const forwarded = decodeSentEnvelope(host.sent.pop());
    expect(forwarded.kind).toBe(relayEnvelopeKind.text);
    expect(new TextDecoder().decode(forwarded.payload)).toBe(answer);
    expect(device.closeCode).toBe(1000);

    await room.webSocketClose(device as unknown as WebSocket);
    expect(decodeSentEnvelope(host.sent.pop()).kind).toBe(relayEnvelopeKind.disconnected);
  });

  it("rejects unauthenticated hosts and caps pending devices without replacing the host", async () => {
    const invalidContext = new TestContext();
    const invalidRoom = new worker.SecureDirectRoomObject(invalidContext as never, {} as never);
    await invalidRoom.fetch(internalRequest("secure-host", "A".repeat(22)));
    const invalidHost = invalidContext.getWebSockets("secure-host")[0]!;
    await invalidRoom.webSocketMessage(invalidHost as unknown as WebSocket, "{}");
    expect(invalidHost.closeCode).toBe(relayClose.unauthorized);

    const { room, context, host, routeId } = await authenticatedRoom();
    for (let index = 0; index < maximumDevicesPerRoom; index += 1) {
      expect((await room.fetch(internalRequest("secure-device", routeId))).status).toBe(101);
    }
    expect((await room.fetch(internalRequest("secure-device", routeId))).status).toBe(503);
    expect(host.readyState).toBe(TestSocket.OPEN);
    expect(context.getWebSockets("secure-device")).toHaveLength(maximumDevicesPerRoom);
  });

  it("rejects oversized authentication and answer messages at the room boundary", async () => {
    const context = new TestContext();
    const room = new worker.SecureDirectRoomObject(context as never, {} as never);
    await room.fetch(internalRequest("secure-host", "A".repeat(22)));
    const host = context.getWebSockets("secure-host")[0]!;
    await room.webSocketMessage(host as unknown as WebSocket, "x".repeat(maximumControlMessageBytes + 1));
    expect(host.closeCode).toBe(relayClose.tooLarge);

    const authenticated = await authenticatedRoom();
    await authenticated.room.fetch(internalRequest("secure-device", authenticated.routeId));
    const device = authenticated.context.getWebSockets("secure-device")[0]!;
    await authenticated.room.webSocketMessage(
      device as unknown as WebSocket,
      "x".repeat(maximumInnerMessageBytes + 1));
    expect(device.closeCode).toBe(relayClose.tooLarge);
  });

  it("expires pending devices and closes only pending devices when the host disconnects", async () => {
    const { room, context, host, routeId } = await authenticatedRoom();
    await room.fetch(internalRequest("secure-device", routeId));
    const timedOut = context.getWebSockets("secure-device")[0]!;
    const attachment = timedOut.deserializeAttachment() as { negotiationExpiresAt: number };
    attachment.negotiationExpiresAt = 0;
    timedOut.serializeAttachment(attachment);

    await room.alarm();
    expect(timedOut.closeCode).toBe(relayClose.unavailable);
    await room.webSocketClose(timedOut as unknown as WebSocket);
    expect(decodeSentEnvelope(host.sent.pop()).kind).toBe(relayEnvelopeKind.disconnected);

    await room.fetch(internalRequest("secure-device", routeId));
    const remaining = context.getWebSockets("secure-device")[0]!;
    await room.webSocketClose(host as unknown as WebSocket);
    expect(remaining.closeCode).toBe(relayClose.unavailable);
    expect(context.storage.deleteAlarm).toHaveBeenCalled();
  });
});

describe("Worker route isolation", () => {
  it("rejects oversized Relay host authentication before parsing", async () => {
    const context = new TestContext();
    const room = new worker.RelayRoomObject(context as never, {} as never);
    const routeId = "A".repeat(22);
    const response = await room.fetch({ url: `https://relay.internal/connect?role=host&route=${routeId}` } as Request);
    expect(response.status).toBe(101);
    const host = context.getWebSockets("host")[0]!;

    await room.webSocketMessage(host as unknown as WebSocket, "x".repeat(maximumControlMessageBytes + 1));

    expect(host.closeCode).toBe(relayClose.tooLarge);
  });

  it("keeps the existing Relay route on RELAY_ROOMS", async () => {
    const relayFetch = vi.fn(async () => new TestResponse(null, { status: 101 }) as unknown as Response);
    const secureFetch = vi.fn(async () => new TestResponse(null, { status: 101 }) as unknown as Response);
    const environment = {
      ALLOWED_DEVICE_ORIGIN: "https://voltura.se",
      RELAY_ROOMS: { getByName: vi.fn(() => ({ fetch: relayFetch })) },
      SECURE_DIRECT_ROOMS: { getByName: vi.fn(() => ({ fetch: secureFetch })) }
    };
    const routeId = "A".repeat(22);
    const request = new Request(`https://relay.example/v1/device/${routeId}`, {
      headers: { "CF-Connecting-IP": "192.0.2.1", Origin: "https://voltura.se", Upgrade: "websocket" }
    });

    expect((await worker.default.fetch(request, environment as never)).status).toBe(101);
    expect(environment.RELAY_ROOMS.getByName).toHaveBeenCalledWith(routeId);
    expect(environment.SECURE_DIRECT_ROOMS.getByName).not.toHaveBeenCalled();
  });
});

async function authenticatedRoom() {
  const context = new TestContext();
  const keys = await crypto.subtle.generateKey({ name: "ECDSA", namedCurve: "P-256" }, true, ["sign", "verify"]);
  const publicKey = encodeBase64Url(new Uint8Array(await crypto.subtle.exportKey("raw", keys.publicKey)));
  const routeId = await deriveRouteId(publicKey);
  const room = new worker.SecureDirectRoomObject(context as never, {} as never);
  expect((await room.fetch(internalRequest("secure-host", routeId))).status).toBe(101);
  const host = context.getWebSockets("secure-host")[0]!;

  await room.webSocketMessage(host as unknown as WebSocket,
    JSON.stringify({ type: "relay.host.hello", routeId, publicKey }));
  const challenge = JSON.parse(host.sent.pop() as string) as { challenge: string };
  const signature = encodeBase64Url(new Uint8Array(await crypto.subtle.sign(
    { name: "ECDSA", hash: "SHA-256" }, keys.privateKey,
    Uint8Array.from(createHostTranscript(routeId, challenge.challenge)).buffer)));
  await room.webSocketMessage(host as unknown as WebSocket,
    JSON.stringify({ type: "relay.host.proof", signature }));
  expect(JSON.parse(host.sent.pop() as string)).toEqual({ type: "relay.host.accepted", protocol: 1 });
  return { room, context, host, routeId };
}

function internalRequest(role: "secure-host" | "secure-device", routeId: string): Request {
  const source = role === "secure-device" ? `&source=${"B".repeat(22)}` : "";
  return { url: `https://relay.internal/secure-connect?role=${role}&route=${routeId}${source}` } as Request;
}

function decodeSentEnvelope(value: string | ArrayBuffer | Uint8Array | undefined) {
  expect(value).toBeInstanceOf(Uint8Array);
  const envelope = decodeEnvelope(value as Uint8Array);
  expect(envelope).not.toBeNull();
  return envelope!;
}
