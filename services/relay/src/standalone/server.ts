import { createServer } from "node:http";
import { createHmac } from "node:crypto";
import { WebSocket as NodeWebSocket, WebSocketServer, type RawData, type WebSocket } from "ws";
import {
  decodeEnvelope,
  encodeEnvelope,
  consumeRelayRate,
  canAcceptHostCandidate,
  canClaimHostCandidate,
  deriveRelaySourceKey,
  hostAuthenticationTimeoutMs,
  maximumDevicesPerRoom,
  maximumBufferedBytes,
  maximumRelayPayloadBytes,
  isTurnRequestTimestampFresh,
  processHostAuthentication,
  relayClose,
  relayEnvelopeKind,
  routeIdPattern,
  turnRequestNonceRetentionMs,
  type RelayRateState
} from "../core/index";
import { verifySignature } from "../core/routing";
import { validateOptionalTurnPublicIp } from "./configuration";

interface HostState {
  socket: WebSocket;
  authenticated: boolean;
  authenticationExpiresAt: number;
  authenticationTimer?: NodeJS.Timeout;
  publicKey?: string;
  challenge?: string;
  rate?: RelayRateState;
}
interface DeviceState { socket: WebSocket; sessionId: Uint8Array; sourceKey: Uint8Array; rate?: RelayRateState }
interface RoomState { host?: HostState; pendingHost?: HostState; devices: Map<string, DeviceState> }

const rooms = new Map<string, RoomState>();
const port = parsePort(process.env.RELAY_PORT);
const allowedOrigin = process.env.RELAY_ALLOWED_ORIGIN ?? "https://voltura.se";
const turnSharedSecret = process.env.TURN_SHARED_SECRET;
const turnUrls = (process.env.TURN_URLS ?? "").split(",").map((value) => value.trim()).filter(isTurnUrl);
validateOptionalTurnPublicIp(process.env.TURN_PUBLIC_IP);
const usedTurnNonces = new Map<string, number>();
const server = createServer(async (request, response) => {
  if (request.method === "GET" && request.url === "/v1/health") {
    response.writeHead(200, { "Content-Type": "application/json" });
    response.end(JSON.stringify({ service: "standalone", protocol: 1, status: "ok" }));
    return;
  }
  const turnMatch = /^\/v1\/turn\/([A-Za-z0-9_-]{22})$/u.exec(new URL(request.url ?? "/", "http://relay.local").pathname);
  if (request.method === "POST" && turnMatch) {
    await issueTurn(request, response, turnMatch[1]!);
    return;
  }
  response.writeHead(404).end();
});
const sockets = new WebSocketServer({ noServer: true, maxPayload: maximumRelayPayloadBytes + 18 });

server.on("upgrade", (request, socket, head) => {
  void handleUpgrade(request, socket, head).catch(() => socket.destroy());
});

async function handleUpgrade(
  request: import("node:http").IncomingMessage,
  socket: import("node:stream").Duplex,
  head: Buffer
): Promise<void> {
  const url = new URL(request.url ?? "/", "http://relay.local");
  const match = /^\/v1\/(host|device)\/([A-Za-z0-9_-]{22})$/u.exec(url.pathname);
  if (!match) { socket.destroy(); return; }
  const role = match[1] as "host" | "device";
  const routeId = match[2]!;
  if (!routeIdPattern.test(routeId) || (role === "device" && request.headers.origin !== allowedOrigin)) { socket.destroy(); return; }
  const room = rooms.get(routeId) ?? { devices: new Map<string, DeviceState>() };
  if ((role === "host" && !canAcceptHostCandidate(room.host?.authenticated === true)) ||
      (role === "device" && (!room.host?.authenticated || room.devices.size >= maximumDevicesPerRoom))) { socket.destroy(); return; }
  const forwardedSource = request.headers["x-forwarded-for"];
  const source = typeof forwardedSource === "string" && forwardedSource.length <= 128
    ? forwardedSource
    : `unknown:${crypto.randomUUID()}`;
  const sourceKey = role === "device" ? await deriveRelaySourceKey(routeId, source) : undefined;
  rooms.set(routeId, room);
  sockets.handleUpgrade(request, socket, head, (webSocket) => attach(webSocket, role, routeId, room, sourceKey));
}

server.listen(port, "0.0.0.0", () => process.stdout.write(`Voltura Air relay listening on ${port}\n`));

function attach(
  socket: WebSocket,
  role: "host" | "device",
  routeId: string,
  room: RoomState,
  sourceKey?: Uint8Array
): void {
  if (role === "host") {
    if (room.pendingHost) room.pendingHost.socket.close(relayClose.conflict, "Superseded host authentication");
    const host: HostState = {
      socket,
      authenticated: false,
      authenticationExpiresAt: Date.now() + hostAuthenticationTimeoutMs
    };
    room.pendingHost = host;
    host.authenticationTimer = setTimeout(() => {
      if (room.pendingHost === host && !host.authenticated) socket.close(relayClose.unauthorized, "Host authentication timed out");
    }, hostAuthenticationTimeoutMs);
    host.authenticationTimer.unref();
    socket.on("message", (data, binary) => {
      if (!host.authenticated) {
        void authenticateHost(data, binary, routeId, host, room).catch(() =>
          socket.close(relayClose.unauthorized, "Host authentication failed"));
        return;
      }
      const bytes = toBytes(data);
      const rate = consumeRelayRate(host.rate, "host", bytes.length);
      host.rate = rate.state;
      if (!rate.allowed) return socket.close(relayClose.overloaded, "Relay rate limit exceeded");
      const envelope = decodeEnvelope(bytes);
      if (!envelope || (envelope.kind !== relayEnvelopeKind.text && envelope.kind !== relayEnvelopeKind.binary && envelope.kind !== relayEnvelopeKind.closeDevice)) return socket.close(relayClose.invalid, "Invalid relay envelope");
      if (envelope.kind === relayEnvelopeKind.closeDevice && envelope.payload.length !== 0) return socket.close(relayClose.invalid, "Invalid device close envelope");
      const device = room.devices.get(key(envelope.sessionId));
      if (device && device.socket.readyState === NodeWebSocket.OPEN) {
        if (envelope.kind === relayEnvelopeKind.closeDevice) {
          device.socket.close(1000, "Host closed session");
        } else if (envelope.kind === relayEnvelopeKind.text) sendBoundedText(device.socket, new TextDecoder().decode(envelope.payload));
        else sendBounded(device.socket, envelope.payload);
      }
    });
    socket.on("close", () => {
      if (host.authenticationTimer) clearTimeout(host.authenticationTimer);
      if (room.pendingHost === host) delete room.pendingHost;
      if (room.host === host) {
        delete room.host;
        for (const device of room.devices.values()) device.socket.close(relayClose.unavailable, "Host disconnected");
        room.devices.clear();
      }
      removeEmptyRoom(routeId, room);
    });
    return;
  }

  if (!sourceKey || sourceKey.length !== 16) return socket.close(relayClose.invalid, "Relay source identity unavailable");
  const sessionId = crypto.getRandomValues(new Uint8Array(16));
  const device: DeviceState = { socket, sessionId, sourceKey };
  room.devices.set(key(sessionId), device);
  sendBounded(room.host!.socket, encodeEnvelope(sessionId, sourceKey, relayEnvelopeKind.connected));
  socket.on("message", (data, binary) => {
    const payload = toBytes(data);
    if (payload.length > maximumRelayPayloadBytes) return socket.close(relayClose.tooLarge, "Message is too large");
    const rate = consumeRelayRate(device.rate, "device", payload.length);
    device.rate = rate.state;
    if (!rate.allowed) return socket.close(relayClose.overloaded, "Relay rate limit exceeded");
    if (room.host?.authenticated) sendBounded(room.host.socket, encodeEnvelope(
      sessionId,
      payload,
      binary ? relayEnvelopeKind.binary : relayEnvelopeKind.text));
  });
  socket.on("close", () => {
    room.devices.delete(key(sessionId));
    if (room.host?.authenticated) sendBounded(room.host.socket, encodeEnvelope(sessionId, new Uint8Array(), relayEnvelopeKind.disconnected));
  });
}

async function authenticateHost(
  data: RawData,
  binary: boolean,
  routeId: string,
  host: HostState,
  room: RoomState
): Promise<void> {
  if (!canUseHostCandidate(room, host)) return host.socket.close(relayClose.conflict, "Host authentication superseded or expired");
  if (binary) return host.socket.close(relayClose.invalid, "Host authentication requires text");
  const result = await processHostAuthentication(routeId, data.toString(), host.publicKey && host.challenge ? { publicKey: host.publicKey, challenge: host.challenge } : undefined);
  if (result.kind === "rejected") return host.socket.close(relayClose.unauthorized, "Host authentication failed");
  if (!canUseHostCandidate(room, host)) return host.socket.close(relayClose.conflict, "Host authentication superseded or expired");
  if (result.kind === "challenge") {
    host.publicKey = result.hello.publicKey;
    host.challenge = result.challenge;
  } else {
    host.authenticated = true;
    room.host = host;
    delete room.pendingHost;
    delete host.challenge;
    if (host.authenticationTimer) clearTimeout(host.authenticationTimer);
    delete host.authenticationTimer;
  }
  host.socket.send(result.response);
}

function canUseHostCandidate(room: RoomState, host: HostState): boolean {
  return canClaimHostCandidate(
    room.host?.authenticated === true,
    room.pendingHost === host,
    host.socket.readyState === NodeWebSocket.OPEN,
    host.authenticationExpiresAt);
}

function removeEmptyRoom(routeId: string, room: RoomState): void {
  if (!room.host && !room.pendingHost && room.devices.size === 0) rooms.delete(routeId);
}

function toBytes(data: RawData): Uint8Array {
  if (data instanceof ArrayBuffer) return new Uint8Array(data);
  if (Array.isArray(data)) return Uint8Array.from(Buffer.concat(data));
  return Uint8Array.from(data);
}
function key(value: Uint8Array): string { return Buffer.from(value).toString("hex"); }
function parsePort(value: string | undefined): number {
  const parsed = Number.parseInt(value ?? "8787", 10);
  if (!Number.isInteger(parsed) || parsed < 1 || parsed > 65535) throw new Error("RELAY_PORT must be a valid port.");
  return parsed;
}

async function issueTurn(request: import("node:http").IncomingMessage, response: import("node:http").ServerResponse, routeId: string): Promise<void> {
  const host = rooms.get(routeId)?.host;
  if (!host?.authenticated || !host.publicKey || !turnSharedSecret || turnUrls.length === 0) return json(response, 503, { code: "turn-unavailable" });
  let payload: unknown;
  try { payload = JSON.parse(await readBody(request)); } catch { return json(response, 400, { code: "invalid-request" }); }
  if (!isRecord(payload) || typeof payload.timestamp !== "string" || typeof payload.nonce !== "string" || typeof payload.signature !== "string" ||
      Object.keys(payload).length !== 3 || !isTurnRequestTimestampFresh(payload.timestamp) || !/^[A-Za-z0-9_-]{43}$/u.test(payload.nonce) ||
      !/^[A-Za-z0-9_-]{86}$/u.test(payload.signature)) return json(response, 401, { code: "unauthorized" });
  const replayKey = `${routeId}:${payload.nonce}`;
  const now = Date.now();
  for (const [nonce, createdAt] of usedTurnNonces) if (createdAt < now - turnRequestNonceRetentionMs) usedTurnNonces.delete(nonce);
  if (usedTurnNonces.has(replayKey)) return json(response, 401, { code: "unauthorized" });
  const transcript = new TextEncoder().encode(`voltura-air-relay-turn-v1\n${routeId}\n${payload.timestamp}\n${payload.nonce}`);
  if (!await verifySignature(host.publicKey, transcript, payload.signature, routeId)) return json(response, 401, { code: "unauthorized" });
  usedTurnNonces.set(replayKey, now);
  const expiresAt = new Date(now + 15 * 60_000);
  const username = `${Math.floor(expiresAt.getTime() / 1000)}:${routeId}`;
  const credential = createHmac("sha1", turnSharedSecret).update(username).digest("base64");
  json(response, 200, {
    allowed: true,
    forcedQuality: null,
    usageBytes: 0,
    checkedAt: new Date(now).toISOString(),
    usageWarningBytes: null,
    usageCutoffBytes: null,
    expiresAt: expiresAt.toISOString(),
    iceServers: [{ urls: turnUrls, username, credential }]
  });
}

async function readBody(request: import("node:http").IncomingMessage): Promise<string> {
  const chunks: Buffer[] = [];
  let length = 0;
  for await (const chunk of request) {
    const bytes = Buffer.from(chunk);
    length += bytes.length;
    if (length > 4096) throw new Error("Request too large.");
    chunks.push(bytes);
  }
  return Buffer.concat(chunks).toString("utf8");
}

function json(response: import("node:http").ServerResponse, status: number, payload: object): void {
  response.writeHead(status, { "Content-Type": "application/json", "Cache-Control": "no-store" });
  response.end(JSON.stringify(payload));
}

function isRecord(value: unknown): value is Record<string, unknown> { return typeof value === "object" && value !== null && !Array.isArray(value); }
function isTurnUrl(value: string): boolean { return /^turns?:[^\s,]{1,500}$/u.test(value); }
function sendBounded(socket: WebSocket, value: Uint8Array): boolean {
  if (socket.bufferedAmount > maximumBufferedBytes) {
    socket.close(relayClose.overloaded, "Relay backpressure limit exceeded");
    return false;
  }
  socket.send(value, { binary: true });
  return true;
}
function sendBoundedText(socket: WebSocket, value: string): boolean {
  if (socket.bufferedAmount > maximumBufferedBytes) {
    socket.close(relayClose.overloaded, "Relay backpressure limit exceeded");
    return false;
  }
  socket.send(value);
  return true;
}
