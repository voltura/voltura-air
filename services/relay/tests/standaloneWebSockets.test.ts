import { once, EventEmitter } from "node:events";
import { Readable } from "node:stream";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { createHostTranscript, deriveRouteId, encodeBase64Url } from "../src/core/index";

const serverEvents = vi.hoisted(() => new Map<string, (...args: unknown[]) => void>());
const clients = vi.hoisted(() => new Set<FakeWebSocket>());

vi.mock("node:http", () => ({
  createServer: (requestListener: (...args: unknown[]) => void) => {
    serverEvents.set("request", requestListener);
    return {
      on: (event: string, listener: (...args: unknown[]) => void) =>
        serverEvents.set(event, listener),
      listen: vi.fn(),
    };
  },
}));

vi.mock("ws", () => ({
  WebSocket: { OPEN: 1 },
  WebSocketServer: class {
    readonly clients = clients;

    handleUpgrade(
      _request: unknown,
      transport: { webSocket: FakeWebSocket },
      _head: Buffer,
      accept: (socket: FakeWebSocket) => void,
    ) {
      const socket = transport.webSocket;
      clients.add(socket);
      socket.once("close", () => clients.delete(socket));
      accept(socket);
    }
  },
}));

class FakeWebSocket extends EventEmitter {
  readonly bufferedAmount = 0;
  readyState = 1;
  readonly ping = vi.fn();
  readonly terminate = vi.fn(() => this.close());

  send(value: string) {
    this.emit("outgoing", value);
  }

  close() {
    this.readyState = 3;
    this.emit("close");
  }
}

describe("standalone WebSocket lifecycle", () => {
  beforeEach(async () => {
    vi.useFakeTimers();
    vi.stubEnv("RELAY_ALLOWED_ORIGIN", "https://voltura.se");
    vi.stubEnv("TURN_SHARED_SECRET", "a-test-secret-for-local-turn-credentials");
    vi.stubEnv("TURN_URLS", "turns:turn.example.invalid:443?transport=tcp");
    vi.resetModules();
    clients.clear();
    serverEvents.clear();
    await import("../src/standalone/server");
  });

  afterEach(() => {
    vi.unstubAllEnvs();
    vi.clearAllTimers();
    vi.useRealTimers();
  });

  it("accepts a query string on the standalone health route", async () => {
    const response = { writeHead: vi.fn().mockReturnThis(), end: vi.fn() };

    await serverEvents.get("request")!({ method: "GET", url: "/v1/health?probe=1" }, response);

    expect(response.writeHead).toHaveBeenCalledWith(200, { "Content-Type": "application/json" });
    expect(response.end).toHaveBeenCalledWith(
      JSON.stringify({ service: "standalone", protocol: 1, status: "ok" }),
    );
  });

  it.each([
    [undefined, 900],
    ["terminal", 3600],
  ] as const)(
    "reports the exact coturn username expiry for purpose %s",
    async (purpose, lifetime) => {
      vi.setSystemTime(new Date("2026-11-28T12:00:00.123Z"));
      const { keys, routeId } = await connectAuthenticatedHost();
      const timestamp = String(Date.now());
      const nonce = encodeBase64Url(crypto.getRandomValues(new Uint8Array(32)));
      const transcript = new TextEncoder().encode(
        purpose
          ? `voltura-air-relay-turn-v2\n${routeId}\n${timestamp}\n${nonce}\n${purpose}`
          : `voltura-air-relay-turn-v1\n${routeId}\n${timestamp}\n${nonce}`,
      );
      const signature = encodeBase64Url(
        new Uint8Array(
          await crypto.subtle.sign({ name: "ECDSA", hash: "SHA-256" }, keys.privateKey, transcript),
        ),
      );
      const request = Object.assign(
        Readable.from([Buffer.from(JSON.stringify({ timestamp, nonce, signature, purpose }))]),
        { method: "POST", url: `/v1/turn/${routeId}` },
      );
      const response = { writeHead: vi.fn().mockReturnThis(), end: vi.fn() };

      await serverEvents.get("request")!(request, response);

      expect(response.writeHead).toHaveBeenCalledWith(200, expect.anything());
      const body = JSON.parse(response.end.mock.lastCall![0]);
      const validUntil = (Math.floor(Number(timestamp) / 1000) + lifetime) * 1000;
      expect(body.iceServers[0].username).toBe(`${validUntil / 1000}:${routeId}`);
      expect(new Date(body.expiresAt).getTime()).toBe(validUntil);
    },
  );

  it("keeps an idle authenticated host alive and retires it when a pong stops arriving", async () => {
    const { webSocket } = await connectAuthenticatedHost();

    vi.advanceTimersByTime(30_000);
    expect(webSocket.ping).toHaveBeenCalledOnce();
    webSocket.emit("pong");
    vi.advanceTimersByTime(30_000);
    expect(webSocket.ping).toHaveBeenCalledTimes(2);
    expect(webSocket.readyState).toBe(1);
    vi.advanceTimersByTime(30_000);
    expect(webSocket.terminate).toHaveBeenCalledOnce();
    expect(clients).not.toContain(webSocket);
  });

  it("isolates a socket error and restores the pending-host slot", () => {
    const route = "A".repeat(22);
    const { webSocket } = connectHost(route);
    const other = connectHost(route);
    expect(other.destroyed).not.toHaveBeenCalled();
    expect(connectHost(route).destroyed).toHaveBeenCalledOnce();

    expect(() => webSocket.emit("error", new Error("Connection lost"))).not.toThrow();

    expect(webSocket.terminate).toHaveBeenCalledOnce();
    expect(other.webSocket.readyState).toBe(1);
    expect(connectHost(route).destroyed).not.toHaveBeenCalled();
  });

  it("refuses a device when the authenticated host is completing its close handshake", async () => {
    const { webSocket, routeId } = await connectAuthenticatedHost();
    webSocket.readyState = 2;

    const device = upgrade("device", routeId);

    expect(device.destroyed).toHaveBeenCalledOnce();
    expect(clients).not.toContain(device.webSocket);
  });
});

function connectHost(routeId: string) {
  return upgrade("host", routeId);
}

function upgrade(role: "host" | "device", routeId: string) {
  const webSocket = new FakeWebSocket();
  const destroyed = vi.fn();
  serverEvents.get("upgrade")!(
    {
      url: `/v1/${role}/${routeId}`,
      headers: { origin: "https://voltura.se" },
      socket: { remoteAddress: "192.0.2.10" },
    },
    { webSocket, destroy: destroyed },
    Buffer.alloc(0),
  );
  return { webSocket, destroyed };
}

async function connectAuthenticatedHost() {
  const keys = await crypto.subtle.generateKey({ name: "ECDSA", namedCurve: "P-256" }, true, [
    "sign",
    "verify",
  ]);
  const publicKey = encodeBase64Url(
    new Uint8Array(await crypto.subtle.exportKey("raw", keys.publicKey)),
  );
  const routeId = await deriveRouteId(publicKey);
  const { webSocket, destroyed } = connectHost(routeId);
  expect(destroyed).not.toHaveBeenCalled();
  const challengeMessage = await exchange(webSocket, {
    type: "relay.host.hello",
    routeId,
    publicKey,
  });
  const signature = encodeBase64Url(
    new Uint8Array(
      await crypto.subtle.sign(
        { name: "ECDSA", hash: "SHA-256" },
        keys.privateKey,
        Uint8Array.from(createHostTranscript(routeId, challengeMessage.challenge)).buffer,
      ),
    ),
  );
  expect(await exchange(webSocket, { type: "relay.host.proof", signature })).toEqual({
    type: "relay.host.accepted",
    protocol: 1,
  });
  return { webSocket, routeId, keys };
}

async function exchange(socket: FakeWebSocket, message: object) {
  const outgoing = once(socket, "outgoing");
  socket.emit("message", Buffer.from(JSON.stringify(message)), false);
  const [text] = await outgoing;
  return JSON.parse(text);
}
