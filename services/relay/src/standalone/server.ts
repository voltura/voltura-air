import { createServer } from "node:http";
import { createHmac } from "node:crypto";
import { WebSocket as NodeWebSocket, WebSocketServer, type RawData, type WebSocket } from "ws";
import {
  decodeEnvelope,
  encodeEnvelope,
  consumeRelayRate,
  canClaimHostCandidate,
  deriveRelaySourceKey,
  hostAuthenticationTimeoutMs,
  maximumDevicesPerRoom,
  maximumBufferedBytes,
  maximumControlMessageBytes,
  maximumPendingDevicesPerRoom,
  maximumPendingDevicesPerSource,
  maximumPendingHostCandidates,
  maximumPendingHostCandidatesPerSource,
  maximumRelayPayloadBytes,
  isTurnRequestTimestampFresh,
  processHostAuthentication,
  relayClose,
  relayEnvelopeKind,
  routeIdPattern,
  turnRequestNonceRetentionMs,
  type RelayRateState,
} from "../core/index";
import { verifySignature } from "../core/routing";
import { parsePort, validateOptionalTurnPublicIp } from "./configuration";

interface HostState {
  socket: WebSocket;
  source: string;
  authenticated: boolean;
  authenticationExpiresAt: number;
  authenticationTimer?: NodeJS.Timeout;
  publicKey?: string;
  challenge?: string;
  rate?: RelayRateState;
}
interface DeviceState {
  socket: WebSocket;
  sessionId: Uint8Array;
  sourceKey: Uint8Array;
  authenticated: boolean;
  rate?: RelayRateState;
}
interface RoomState {
  host?: HostState;
  pendingHosts: Set<HostState>;
  devices: Map<string, DeviceState>;
}

const rooms = new Map<string, RoomState>();
const port = parsePort(process.env.RELAY_PORT);
const allowedOrigin = process.env.RELAY_ALLOWED_ORIGIN ?? "https://voltura.se";
const turnSharedSecret = process.env.TURN_SHARED_SECRET;
const turnUrls = (process.env.TURN_URLS ?? "")
  .split(",")
  .map((value) => value.trim())
  .filter(isTurnUrl);
validateOptionalTurnPublicIp(process.env.TURN_PUBLIC_IP);
const usedTurnNonces = new Map<string, number>();
const server = createServer(async (request, response) => {
  const url = URL.parse(request.url ?? "/", "http://relay.local");
  if (!url) {
    response.writeHead(400).end();
    return;
  }
  if (request.method === "GET" && url.pathname === "/v1/health") {
    response.writeHead(200, { "Content-Type": "application/json" });
    response.end(JSON.stringify({ service: "standalone", protocol: 1, status: "ok" }));
    return;
  }
  const turnMatch = /^\/v1\/turn\/([A-Za-z0-9_-]{22})$/u.exec(url.pathname);
  if (request.method === "POST" && turnMatch) {
    await issueTurn(request, response, turnMatch[1]!);
    return;
  }
  response.writeHead(404).end();
});
const sockets = new WebSocketServer({ noServer: true, maxPayload: maximumRelayPayloadBytes + 18 });
const awaitingPong = new WeakSet<WebSocket>();
const heartbeat = setInterval(() => {
  for (const socket of sockets.clients) {
    if (socket.readyState !== NodeWebSocket.OPEN) continue;
    if (awaitingPong.has(socket)) {
      socket.terminate();
      continue;
    }
    awaitingPong.add(socket);
    socket.ping();
  }
}, 30_000).unref();

server.on("upgrade", (request, socket, head) => {
  void handleUpgrade(request, socket, head).catch(() => socket.destroy());
});
server.on("close", () => clearInterval(heartbeat));

async function handleUpgrade(
  request: import("node:http").IncomingMessage,
  socket: import("node:stream").Duplex,
  head: Buffer,
): Promise<void> {
  const url = new URL(request.url ?? "/", "http://relay.local");
  const match = /^\/v1\/(host|device)\/([A-Za-z0-9_-]{22})$/u.exec(url.pathname);
  if (!match) {
    socket.destroy();
    return;
  }
  const role = match[1] as "host" | "device";
  const routeId = match[2]!;
  if (
    !routeIdPattern.test(routeId) ||
    (role === "device" && request.headers.origin !== allowedOrigin)
  ) {
    socket.destroy();
    return;
  }
  const source = request.socket.remoteAddress ?? "unknown";
  const room = rooms.get(routeId) ?? {
    pendingHosts: new Set<HostState>(),
    devices: new Map<string, DeviceState>(),
  };
  if (
    role === "host" &&
    (pendingHostCount(room) >= maximumPendingHostCandidates ||
      pendingHostCount(room, source) >= maximumPendingHostCandidatesPerSource)
  ) {
    socket.destroy();
    return;
  }
  if (role === "device" && !activeHost(room)) {
    socket.destroy();
    return;
  }
  const sourceKey = role === "device" ? await deriveRelaySourceKey(routeId, source) : undefined;
  const pendingDevices = role === "device" ? pendingDeviceCount(room) : 0;
  if (
    role === "device" &&
    (rooms.get(routeId) !== room ||
      !activeHost(room) ||
      room.devices.size - pendingDevices >= maximumDevicesPerRoom ||
      room.devices.size >= maximumDevicesPerRoom + maximumPendingDevicesPerRoom ||
      pendingDevices >= maximumPendingDevicesPerRoom ||
      pendingDeviceCount(room, sourceKey) >= maximumPendingDevicesPerSource)
  ) {
    socket.destroy();
    return;
  }
  sockets.handleUpgrade(request, socket, head, (webSocket) => {
    if (role === "host") rooms.set(routeId, room);
    attach(webSocket, role, routeId, room, source, sourceKey);
  });
}

server.listen(port, "0.0.0.0", () =>
  process.stdout.write(`Voltura Air relay listening on ${port}\n`),
);

function attach(
  socket: WebSocket,
  role: "host" | "device",
  routeId: string,
  room: RoomState,
  source: string,
  sourceKey?: Uint8Array,
): void {
  socket.on("error", () => socket.terminate());
  socket.on("pong", () => awaitingPong.delete(socket));
  if (role === "host") {
    const host: HostState = {
      socket,
      source,
      authenticated: false,
      authenticationExpiresAt: Date.now() + hostAuthenticationTimeoutMs,
    };
    room.pendingHosts.add(host);
    host.authenticationTimer = setTimeout(() => {
      if (room.pendingHosts.has(host) && !host.authenticated)
        socket.close(relayClose.unauthorized, "Host authentication timed out");
    }, hostAuthenticationTimeoutMs);
    host.authenticationTimer.unref();
    socket.on("message", (data, binary) => {
      if (socket.readyState !== NodeWebSocket.OPEN) return;
      if (!host.authenticated) {
        void authenticateHost(data, binary, routeId, host, room).catch(() =>
          socket.close(relayClose.unauthorized, "Host authentication failed"),
        );
        return;
      }
      if (room.host !== host) return;
      const bytes = toBytes(data);
      const rate = consumeRelayRate(host.rate, "host", bytes.length);
      host.rate = rate.state;
      if (!rate.allowed) return socket.close(relayClose.overloaded, "Relay rate limit exceeded");
      const envelope = decodeEnvelope(bytes);
      if (
        !envelope ||
        (envelope.kind !== relayEnvelopeKind.text &&
          envelope.kind !== relayEnvelopeKind.binary &&
          envelope.kind !== relayEnvelopeKind.closeDevice &&
          envelope.kind !== relayEnvelopeKind.authenticated)
      )
        return socket.close(relayClose.invalid, "Invalid relay envelope");
      if (
        (envelope.kind === relayEnvelopeKind.closeDevice ||
          envelope.kind === relayEnvelopeKind.authenticated) &&
        envelope.payload.length !== 0
      )
        return socket.close(relayClose.invalid, "Invalid relay control envelope");
      if (envelope.kind === relayEnvelopeKind.authenticated) {
        const device = room.devices.get(key(envelope.sessionId));
        if (device) device.authenticated = true;
        return;
      }
      const device = room.devices.get(key(envelope.sessionId));
      if (device && device.socket.readyState === NodeWebSocket.OPEN) {
        if (envelope.kind === relayEnvelopeKind.closeDevice) {
          device.socket.close(1000, "Host closed session");
        } else if (envelope.kind === relayEnvelopeKind.text) {
          try {
            sendBounded(
              device.socket,
              new TextDecoder("utf-8", { fatal: true }).decode(envelope.payload),
            );
          } catch {
            socket.close(relayClose.invalid, "Invalid relay text");
          }
        } else sendBounded(device.socket, envelope.payload);
      }
    });
    socket.on("close", () => {
      if (host.authenticationTimer) clearTimeout(host.authenticationTimer);
      room.pendingHosts.delete(host);
      if (room.host === host) {
        delete room.host;
        for (const device of room.devices.values())
          device.socket.close(relayClose.unavailable, "Host disconnected");
        room.devices.clear();
      }
      removeEmptyRoom(routeId, room);
    });
    return;
  }

  if (!sourceKey || sourceKey.length !== 16)
    return socket.close(relayClose.invalid, "Relay source identity unavailable");
  const sessionId = crypto.getRandomValues(new Uint8Array(16));
  const sessionKey = key(sessionId);
  const device: DeviceState = { socket, sessionId, sourceKey, authenticated: false };
  room.devices.set(sessionKey, device);
  socket.on("message", (data, binary) => {
    if (socket.readyState !== NodeWebSocket.OPEN || room.devices.get(sessionKey) !== device) return;
    const payload = toBytes(data);
    if (payload.length > maximumRelayPayloadBytes)
      return socket.close(relayClose.tooLarge, "Message is too large");
    const rate = consumeRelayRate(device.rate, "device", payload.length);
    device.rate = rate.state;
    if (!rate.allowed) return socket.close(relayClose.overloaded, "Relay rate limit exceeded");
    const host = activeHost(room);
    if (!host) return socket.close(relayClose.unavailable, "Host unavailable");
    sendBounded(
      host.socket,
      encodeEnvelope(
        sessionId,
        payload,
        binary ? relayEnvelopeKind.binary : relayEnvelopeKind.text,
      ),
    );
  });
  socket.on("close", () => {
    if (room.devices.get(sessionKey) !== device) return;
    room.devices.delete(sessionKey);
    const host = activeHost(room);
    if (host)
      sendBounded(
        host.socket,
        encodeEnvelope(sessionId, new Uint8Array(), relayEnvelopeKind.disconnected),
      );
  });
  const host = activeHost(room);
  if (
    !host ||
    !sendBounded(host.socket, encodeEnvelope(sessionId, sourceKey, relayEnvelopeKind.connected))
  )
    socket.close(relayClose.unavailable, "Host unavailable");
}

async function authenticateHost(
  data: RawData,
  binary: boolean,
  routeId: string,
  host: HostState,
  room: RoomState,
): Promise<void> {
  if (!canUseHostCandidate(room, host))
    return host.socket.close(relayClose.conflict, "Host authentication superseded or expired");
  if (binary) return host.socket.close(relayClose.invalid, "Host authentication requires text");
  if (toBytes(data).length > maximumControlMessageBytes) {
    return host.socket.close(relayClose.tooLarge, "Host authentication message is too large");
  }
  const result = await processHostAuthentication(
    routeId,
    data.toString(),
    host.publicKey && host.challenge
      ? { publicKey: host.publicKey, challenge: host.challenge }
      : undefined,
  );
  if (result.kind === "rejected")
    return host.socket.close(relayClose.unauthorized, "Host authentication failed");
  if (!canUseHostCandidate(room, host))
    return host.socket.close(relayClose.conflict, "Host authentication superseded or expired");
  if (result.kind === "challenge") {
    host.publicKey = result.hello.publicKey;
    host.challenge = result.challenge;
  } else {
    const supersededHost = room.host;
    for (const device of room.devices.values())
      device.socket.close(relayClose.unavailable, "Host replaced");
    room.devices.clear();
    host.authenticated = true;
    room.host = host;
    room.pendingHosts.delete(host);
    if (supersededHost && supersededHost !== host)
      supersededHost.socket.close(relayClose.conflict, "Host authentication superseded");
    for (const pending of room.pendingHosts)
      pending.socket.close(relayClose.conflict, "Host authentication superseded");
    room.pendingHosts.clear();
    delete host.challenge;
    if (host.authenticationTimer) clearTimeout(host.authenticationTimer);
    delete host.authenticationTimer;
  }
  host.socket.send(result.response);
}

function activeHost(room: RoomState | undefined): HostState | undefined {
  const host = room?.host;
  return host?.authenticated && host.socket.readyState === NodeWebSocket.OPEN ? host : undefined;
}

function canUseHostCandidate(room: RoomState, host: HostState): boolean {
  return canClaimHostCandidate(
    room.pendingHosts.has(host),
    host.socket.readyState === NodeWebSocket.OPEN,
    host.authenticationExpiresAt,
  );
}

function pendingDeviceCount(room: RoomState, sourceKey?: Uint8Array): number {
  return [...room.devices.values()].filter((device) => {
    if (device.authenticated) return false;
    if (!sourceKey) return true;
    return (
      device.sourceKey.length === sourceKey.length &&
      device.sourceKey.every((value, index) => value === sourceKey[index])
    );
  }).length;
}

function pendingHostCount(room: RoomState, source?: string): number {
  return [...room.pendingHosts].filter((host) => source === undefined || host.source === source)
    .length;
}

function removeEmptyRoom(routeId: string, room: RoomState): void {
  if (
    rooms.get(routeId) === room &&
    !room.host &&
    room.pendingHosts.size === 0 &&
    room.devices.size === 0
  )
    rooms.delete(routeId);
}

function toBytes(data: RawData): Uint8Array {
  if (data instanceof ArrayBuffer) return new Uint8Array(data);
  if (Array.isArray(data)) return Uint8Array.from(Buffer.concat(data));
  return Uint8Array.from(data);
}
function key(value: Uint8Array): string {
  return Buffer.from(value).toString("hex");
}
async function issueTurn(
  request: import("node:http").IncomingMessage,
  response: import("node:http").ServerResponse,
  routeId: string,
): Promise<void> {
  const host = activeHost(rooms.get(routeId));
  if (!host?.publicKey || !turnSharedSecret || turnUrls.length === 0)
    return json(response, 503, { code: "turn-unavailable" });
  let payload: unknown;
  try {
    payload = JSON.parse(await readBody(request));
  } catch {
    return json(response, 400, { code: "invalid-request" });
  }
  if (
    !isRecord(payload) ||
    typeof payload.timestamp !== "string" ||
    typeof payload.nonce !== "string" ||
    typeof payload.signature !== "string" ||
    !(
      (Object.keys(payload).length === 3 && payload.purpose === undefined) ||
      (Object.keys(payload).length === 4 &&
        (payload.purpose === "file-transfer" || payload.purpose === "terminal"))
    ) ||
    !isTurnRequestTimestampFresh(payload.timestamp) ||
    !/^[A-Za-z0-9_-]{43}$/u.test(payload.nonce) ||
    !/^[A-Za-z0-9_-]{86}$/u.test(payload.signature)
  )
    return json(response, 401, { code: "unauthorized" });
  const replayKey = `${routeId}:${payload.nonce}`;
  const now = Date.now();
  for (const [nonce, createdAt] of usedTurnNonces)
    if (createdAt < now - turnRequestNonceRetentionMs) usedTurnNonces.delete(nonce);
  if (usedTurnNonces.has(replayKey)) return json(response, 401, { code: "unauthorized" });
  const purpose =
    payload.purpose === "file-transfer" || payload.purpose === "terminal"
      ? payload.purpose
      : undefined;
  const transcript = new TextEncoder().encode(
    purpose
      ? `voltura-air-relay-turn-v2\n${routeId}\n${payload.timestamp}\n${payload.nonce}\n${purpose}`
      : `voltura-air-relay-turn-v1\n${routeId}\n${payload.timestamp}\n${payload.nonce}`,
  );
  if (
    !(await verifySignature(host.publicKey, transcript, payload.signature, routeId)) ||
    usedTurnNonces.has(replayKey)
  )
    return json(response, 401, { code: "unauthorized" });
  if (activeHost(rooms.get(routeId)) !== host)
    return json(response, 503, { code: "turn-unavailable" });
  usedTurnNonces.set(replayKey, now);
  const credentialTtlSeconds =
    (purpose === "file-transfer" || purpose === "terminal" ? 60 : 15) * 60;
  const expirySeconds = Math.floor(now / 1000) + credentialTtlSeconds;
  const expiresAt = new Date(expirySeconds * 1000);
  const username = `${expirySeconds}:${routeId}`;
  // Coturn's shared-secret TURN REST credentials require this exact HMAC-SHA1 derivation.
  // This is protocol interoperability, not a general-purpose hash or signature choice.
  // codeql[js/weak-cryptographic-algorithm]
  const credential = createHmac("sha1", turnSharedSecret).update(username).digest("base64");
  json(response, 200, {
    allowed: true,
    forcedQuality: null,
    usageBytes: 0,
    checkedAt: new Date(now).toISOString(),
    usageWarningBytes: null,
    usageCutoffBytes: null,
    expiresAt: expiresAt.toISOString(),
    iceServers: [{ urls: turnUrls, username, credential }],
  });
}

async function readBody(request: import("node:http").IncomingMessage): Promise<string> {
  const chunks: Buffer[] = [];
  let length = 0;
  for await (const chunk of request) {
    const bytes = Buffer.from(chunk);
    length += bytes.length;
    if (length > maximumControlMessageBytes) throw new Error("Request too large.");
    chunks.push(bytes);
  }
  return Buffer.concat(chunks).toString("utf8");
}

function json(response: import("node:http").ServerResponse, status: number, payload: object): void {
  response.writeHead(status, { "Content-Type": "application/json", "Cache-Control": "no-store" });
  response.end(JSON.stringify(payload));
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}
function isTurnUrl(value: string): boolean {
  return /^turns?:[^\s,]{1,500}$/u.test(value);
}
function sendBounded(socket: WebSocket, value: Uint8Array | string): boolean {
  if (socket.readyState !== NodeWebSocket.OPEN) return false;
  const binary = typeof value !== "string";
  const byteLength = binary ? value.byteLength : Buffer.byteLength(value, "utf8");
  if (socket.bufferedAmount + byteLength > maximumBufferedBytes) {
    socket.close(relayClose.overloaded, "Relay backpressure limit exceeded");
    return false;
  }
  try {
    socket.send(value, { binary });
  } catch {
    socket.terminate();
    return false;
  }
  return true;
}
