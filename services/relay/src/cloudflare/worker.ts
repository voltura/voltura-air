import { DurableObject } from "cloudflare:workers";
import {
  decodeEnvelope,
  encodeEnvelope,
  consumeRelayRate,
  canClaimHostCandidate,
  decodeBase64Url,
  maximumDevicesPerRoom,
  maximumBufferedBytes,
  maximumControlMessageBytes,
  maximumPendingDevicesPerRoom,
  maximumPendingDevicesPerSource,
  maximumPendingHostCandidates,
  maximumPendingHostCandidatesPerSource,
  maximumInnerMessageBytes,
  maximumRelayPayloadBytes,
  processHostAuthentication,
  deriveRelaySourceKey,
  encodeBase64Url,
  hostAuthenticationTimeoutMs,
  isHostAuthenticationExpired,
  nextHostAuthenticationDeadline,
  relayClose,
  relayEnvelopeKind,
  routeIdPattern,
  isTurnRequestTimestampFresh,
  turnRequestNonceRetentionMs,
  type RelayRateState,
} from "../core/index";
import { createTurnResponse, type TurnEnvironment } from "./turn";
import { parseSecureDescription } from "./secureDirectProtocol";
import { verifySignature } from "../core/routing";

interface Environment extends TurnEnvironment {
  RELAY_ROOMS: DurableObjectNamespace<RelayRoomObject>;
  SECURE_DIRECT_ROOMS: DurableObjectNamespace<SecureDirectRoomObject>;
  PUBLIC_SERVICE_ID: string;
  ALLOWED_DEVICE_ORIGIN?: string;
}

type SocketRole = "host" | "device";
interface SocketAttachment {
  role: SocketRole;
  routeId: string;
  authenticated?: boolean;
  source?: string;
  publicKey?: string;
  challenge?: string;
  authenticationExpiresAt?: number;
  sessionId?: number[];
  sourceKey?: number[];
  rate?: RelayRateState;
}

type SecureSocketRole = "secure-host" | "secure-device";
type SecureDevicePhase = "awaiting-offer" | "awaiting-answer";
interface SecureSocketAttachment {
  role: SecureSocketRole;
  routeId: string;
  authenticated?: boolean;
  publicKey?: string;
  challenge?: string;
  authenticationExpiresAt?: number;
  sessionId?: number[];
  sourceKey?: number[];
  phase?: SecureDevicePhase;
  negotiationExpiresAt?: number;
  rate?: RelayRateState;
}

const secureNegotiationTimeoutMs = 10_000;

export default {
  async fetch(request: Request, env: Environment): Promise<Response> {
    const url = new URL(request.url);
    if (request.method === "GET" && url.pathname === "/v1/health") {
      return Response.json({ service: env.PUBLIC_SERVICE_ID, protocol: 1, status: "ok" });
    }

    const turnMatch = /^\/v1\/turn\/([A-Za-z0-9_-]{22})$/u.exec(url.pathname);
    if (request.method === "POST" && turnMatch) {
      return env.RELAY_ROOMS.getByName(turnMatch[1]!).fetch(
        `https://relay.internal/turn?route=${turnMatch[1]}`,
        request,
      );
    }

    const secureMatch = /^\/v1\/secure\/(host|device)\/([A-Za-z0-9_-]{22})$/u.exec(url.pathname);
    if (secureMatch) {
      if (request.headers.get("Upgrade")?.toLowerCase() !== "websocket")
        return new Response("Not found", { status: 404 });
      const role = secureMatch[1]!;
      const routeId = secureMatch[2]!;
      if (role === "device") {
        const allowedOrigin = env.ALLOWED_DEVICE_ORIGIN ?? "https://voltura.se";
        if (request.headers.get("Origin") !== allowedOrigin)
          return new Response("Origin rejected", { status: 403 });
      }
      const source = request.headers.get("CF-Connecting-IP") ?? `unknown:${crypto.randomUUID()}`;
      const sourceKey =
        role === "device" ? encodeBase64Url(await deriveRelaySourceKey(routeId, source)) : null;
      return env.SECURE_DIRECT_ROOMS.getByName(routeId).fetch(
        `https://relay.internal/secure-connect?role=secure-${role}&route=${routeId}${sourceKey ? `&source=${sourceKey}` : ""}`,
        request,
      );
    }

    const match = /^\/v1\/(host|device)\/([A-Za-z0-9_-]{22})$/u.exec(url.pathname);
    if (!match || request.headers.get("Upgrade")?.toLowerCase() !== "websocket")
      return new Response("Not found", { status: 404 });
    const role = match[1] as SocketRole;
    const routeId = match[2]!;
    if (role === "device") {
      const allowedOrigin = env.ALLOWED_DEVICE_ORIGIN ?? "https://voltura.se";
      if (request.headers.get("Origin") !== allowedOrigin)
        return new Response("Origin rejected", { status: 403 });
    }

    const source = request.headers.get("CF-Connecting-IP") ?? `unknown:${crypto.randomUUID()}`;
    const sourceKey =
      role === "device" ? encodeBase64Url(await deriveRelaySourceKey(routeId, source)) : null;
    return env.RELAY_ROOMS.getByName(routeId).fetch(
      `https://relay.internal/connect?role=${role}&route=${routeId}${sourceKey ? `&source=${sourceKey}` : ""}`,
      request,
    );
  },
} satisfies ExportedHandler<Environment>;

export class RelayRoomObject extends DurableObject<Environment> {
  async fetch(request: Request): Promise<Response> {
    const url = new URL(request.url);
    if (url.pathname === "/turn") return this.issueTurn(request, url.searchParams.get("route"));
    const role = url.searchParams.get("role") as SocketRole | null;
    const routeId = url.searchParams.get("route");
    const source = url.searchParams.get("source");
    if ((role !== "host" && role !== "device") || !routeId || !routeIdPattern.test(routeId))
      return new Response("Invalid route", { status: 400 });

    const host = this.authenticatedHost();
    const hostSource = request.headers?.get("CF-Connecting-IP") ?? "unknown";
    if (
      role === "host" &&
      (this.pendingHostCount() >= maximumPendingHostCandidates ||
        this.pendingHostCount(hostSource) >= maximumPendingHostCandidatesPerSource)
    ) {
      return new Response("Host authentication is busy", { status: 409 });
    }
    if (role === "device" && !host) return new Response("Host unavailable", { status: 503 });
    if (role === "device" && (!source || !/^[A-Za-z0-9_-]{22}$/u.test(source)))
      return new Response("Invalid source", { status: 400 });

    let sourceBytes: Uint8Array | null = null;
    if (role === "device") {
      try {
        sourceBytes = decodeBase64Url(source!);
      } catch {
        return new Response("Invalid source", { status: 400 });
      }
      const devices = this.ctx.getWebSockets("device");
      const pendingDevices = this.pendingDeviceCount();
      if (
        devices.length - pendingDevices >= maximumDevicesPerRoom ||
        devices.length >= maximumDevicesPerRoom + maximumPendingDevicesPerRoom ||
        pendingDevices >= maximumPendingDevicesPerRoom ||
        this.pendingDeviceCount(Array.from(sourceBytes)) >= maximumPendingDevicesPerSource
      ) {
        return new Response("Room full", { status: 503 });
      }
    }

    const pair = new WebSocketPair();
    const client = pair[0];
    const server = pair[1];
    const attachment: SocketAttachment = { role, routeId };
    if (role === "host") attachment.source = hostSource;
    if (role === "host")
      attachment.authenticationExpiresAt = Date.now() + hostAuthenticationTimeoutMs;
    if (role === "device") {
      attachment.sessionId = Array.from(crypto.getRandomValues(new Uint8Array(16)));
      attachment.sourceKey = Array.from(sourceBytes!);
    }
    server.serializeAttachment(attachment);
    this.ctx.acceptWebSocket(server, [role]);
    if (role === "host") await this.scheduleHostAuthenticationAlarm();
    if (role === "device" && host && attachment.sessionId && attachment.sourceKey) {
      sendBounded(
        host,
        encodeEnvelope(
          Uint8Array.from(attachment.sessionId),
          Uint8Array.from(attachment.sourceKey),
          relayEnvelopeKind.connected,
        ),
      );
    }
    return new Response(null, { status: 101, webSocket: client });
  }

  private async issueTurn(request: Request, routeId: string | null): Promise<Response> {
    const host = this.authenticatedHost();
    if (!host || !routeId || !routeIdPattern.test(routeId))
      return new Response("Host unavailable", { status: 503 });
    const attachment = host.deserializeAttachment() as SocketAttachment;
    let value: unknown;
    try {
      value = await request.json();
    } catch {
      return new Response("Invalid request", { status: 400 });
    }
    if (
      !isRecord(value) ||
      typeof value.timestamp !== "string" ||
      typeof value.nonce !== "string" ||
      typeof value.signature !== "string" ||
      !(
        (Object.keys(value).length === 3 && value.purpose === undefined) ||
        (Object.keys(value).length === 4 &&
          (value.purpose === "file-transfer" || value.purpose === "terminal"))
      ) ||
      !isTurnRequestTimestampFresh(value.timestamp) ||
      !/^[A-Za-z0-9_-]{43}$/u.test(value.nonce) ||
      !/^[A-Za-z0-9_-]{86}$/u.test(value.signature) ||
      !attachment.publicKey
    )
      return new Response("Unauthorized", { status: 401 });
    const purpose =
      value.purpose === "file-transfer" || value.purpose === "terminal" ? value.purpose : undefined;
    const transcript = new TextEncoder().encode(
      purpose
        ? `voltura-air-relay-turn-v2\n${routeId}\n${value.timestamp}\n${value.nonce}\n${purpose}`
        : `voltura-air-relay-turn-v1\n${routeId}\n${value.timestamp}\n${value.nonce}`,
    );
    if (!(await verifySignature(attachment.publicKey, transcript, value.signature, routeId)))
      return new Response("Unauthorized", { status: 401 });
    const replayKey = `turn-nonce:${value.nonce}`;
    if (await this.ctx.storage.get(replayKey)) return new Response("Unauthorized", { status: 401 });
    const now = Date.now();
    await this.ctx.storage.put(replayKey, now);
    const priorNonces = await this.ctx.storage.list<number>({ prefix: "turn-nonce:" });
    const expired = [...priorNonces]
      .filter(([, createdAt]) => createdAt < now - turnRequestNonceRetentionMs)
      .map(([key]) => key);
    if (expired.length > 0) await this.ctx.storage.delete(expired);
    return createTurnResponse(this.env, routeId, purpose);
  }

  async webSocketMessage(socket: WebSocket, message: ArrayBuffer | string): Promise<void> {
    const attachment = socket.deserializeAttachment() as SocketAttachment;
    if (attachment.role === "host" && !attachment.authenticated) {
      if (!this.canUseHostCandidate(socket, attachment))
        return socket.close(relayClose.conflict, "Host authentication superseded or expired");
      if (typeof message !== "string")
        return socket.close(relayClose.invalid, "Host authentication requires text");
      if (new TextEncoder().encode(message).length > maximumControlMessageBytes) {
        return socket.close(relayClose.tooLarge, "Host authentication message is too large");
      }
      const result = await processHostAuthentication(
        attachment.routeId,
        message,
        attachment.publicKey && attachment.challenge
          ? { publicKey: attachment.publicKey, challenge: attachment.challenge }
          : undefined,
      );
      if (result.kind === "rejected")
        return socket.close(relayClose.unauthorized, "Host authentication failed");
      if (!this.canUseHostCandidate(socket, attachment))
        return socket.close(relayClose.conflict, "Host authentication superseded or expired");
      if (result.kind === "challenge") {
        attachment.publicKey = result.hello.publicKey;
        attachment.challenge = result.challenge;
      } else {
        attachment.authenticated = true;
        delete attachment.challenge;
        delete attachment.authenticationExpiresAt;
      }
      socket.serializeAttachment(attachment);
      if (result.kind === "accepted") {
        for (const device of this.ctx.getWebSockets("device"))
          device.close(relayClose.unavailable, "Host replaced");
        for (const host of this.ctx.getWebSockets("host"))
          if (host !== socket) host.close(relayClose.conflict, "Host authentication superseded");
      }
      if (result.kind === "accepted") await this.ctx.storage.deleteAlarm();
      socket.send(result.response);
      return;
    }

    const receivedText = typeof message === "string";
    const bytes = receivedText ? new TextEncoder().encode(message) : new Uint8Array(message);
    const rate = consumeRelayRate(attachment.rate, attachment.role, bytes.length);
    attachment.rate = rate.state;
    socket.serializeAttachment(attachment);
    if (!rate.allowed) return socket.close(relayClose.overloaded, "Relay rate limit exceeded");
    if (attachment.role === "device") {
      if (bytes.length > maximumRelayPayloadBytes || !attachment.sessionId)
        return socket.close(relayClose.tooLarge, "Message is too large");
      const host = this.authenticatedHost();
      if (!host) return socket.close(relayClose.unavailable, "Host unavailable");
      sendBounded(
        host,
        encodeEnvelope(
          Uint8Array.from(attachment.sessionId),
          bytes,
          receivedText ? relayEnvelopeKind.text : relayEnvelopeKind.binary,
        ),
      );
      return;
    }

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
      const device = this.findDevice(envelope.sessionId);
      if (device) {
        const deviceAttachment = device.deserializeAttachment() as SocketAttachment;
        deviceAttachment.authenticated = true;
        device.serializeAttachment(deviceAttachment);
      }
      return;
    }
    const device = this.findDevice(envelope.sessionId);
    if (device) {
      if (envelope.kind === relayEnvelopeKind.closeDevice) {
        device.close(1000, "Host closed session");
      } else if (envelope.kind === relayEnvelopeKind.text) {
        try {
          sendBounded(device, new TextDecoder("utf-8", { fatal: true }).decode(envelope.payload));
        } catch {
          socket.close(relayClose.invalid, "Invalid relay text");
        }
      } else {
        sendBounded(device, envelope.payload);
      }
    }
  }

  async webSocketClose(socket: WebSocket): Promise<void> {
    const attachment = socket.deserializeAttachment() as SocketAttachment;
    if (attachment.role === "host") {
      if (attachment.authenticated !== true) {
        await this.scheduleHostAuthenticationAlarm();
        return;
      }
      const replacement = this.authenticatedHost();
      if (replacement && replacement !== socket) return;
      for (const device of this.ctx.getWebSockets("device"))
        device.close(relayClose.unavailable, "Host disconnected");
      return;
    }
    const host = this.authenticatedHost();
    if (host && attachment.sessionId)
      sendBounded(
        host,
        encodeEnvelope(
          Uint8Array.from(attachment.sessionId),
          new Uint8Array(),
          relayEnvelopeKind.disconnected,
        ),
      );
  }

  async webSocketError(socket: WebSocket): Promise<void> {
    await this.webSocketClose(socket);
  }

  async alarm(): Promise<void> {
    const now = Date.now();
    for (const socket of this.ctx.getWebSockets("host")) {
      const attachment = socket.deserializeAttachment() as SocketAttachment;
      if (
        attachment.authenticated !== true &&
        isHostAuthenticationExpired(attachment.authenticationExpiresAt ?? 0, now)
      ) {
        socket.close(relayClose.unauthorized, "Host authentication timed out");
      }
    }
    await this.scheduleHostAuthenticationAlarm(now);
  }

  private authenticatedHost(): WebSocket | null {
    return (
      this.ctx
        .getWebSockets("host")
        .find(
          (socket) =>
            socket.readyState === WebSocket.OPEN &&
            (socket.deserializeAttachment() as SocketAttachment).authenticated === true,
        ) ?? null
    );
  }

  private pendingHostCount(source?: string): number {
    return this.ctx.getWebSockets("host").filter((socket) => {
      const attachment = socket.deserializeAttachment() as SocketAttachment;
      return (
        socket.readyState === WebSocket.OPEN &&
        attachment.authenticated !== true &&
        (source === undefined || attachment.source === source)
      );
    }).length;
  }

  private pendingDeviceCount(sourceKey?: number[]): number {
    return this.ctx.getWebSockets("device").filter((socket) => {
      const attachment = socket.deserializeAttachment() as SocketAttachment;
      if (attachment.authenticated === true) return false;
      if (!sourceKey) return true;
      return (
        (attachment.sourceKey ?? []).length === sourceKey.length &&
        (attachment.sourceKey ?? []).every((value, index) => value === sourceKey[index])
      );
    }).length;
  }

  private canUseHostCandidate(socket: WebSocket, attachment: SocketAttachment): boolean {
    const openCandidates = this.ctx.getWebSockets("host").filter((candidate) => {
      const candidateAttachment = candidate.deserializeAttachment() as SocketAttachment;
      return candidate.readyState === WebSocket.OPEN && candidateAttachment.authenticated !== true;
    });
    return canClaimHostCandidate(
      openCandidates.includes(socket),
      socket.readyState === WebSocket.OPEN,
      attachment.authenticationExpiresAt ?? 0,
    );
  }

  private async scheduleHostAuthenticationAlarm(now: number = Date.now()): Promise<void> {
    const deadline = nextHostAuthenticationDeadline(
      this.ctx
        .getWebSockets("host")
        .filter((socket) => socket.readyState === WebSocket.OPEN)
        .map((socket) => socket.deserializeAttachment() as SocketAttachment)
        .filter((attachment) => attachment.authenticated !== true)
        .map((attachment) => attachment.authenticationExpiresAt ?? 0),
      now,
    );
    if (deadline === null) await this.ctx.storage.deleteAlarm();
    else await this.ctx.storage.setAlarm(deadline);
  }

  private findDevice(sessionId: Uint8Array): WebSocket | null {
    const key = Array.from(sessionId).join(",");
    return (
      this.ctx
        .getWebSockets("device")
        .find(
          (socket) =>
            ((socket.deserializeAttachment() as SocketAttachment).sessionId ?? []).join(",") ===
            key,
        ) ?? null
    );
  }
}

export class SecureDirectRoomObject extends DurableObject<Environment> {
  async fetch(request: Request): Promise<Response> {
    const url = new URL(request.url);
    const role = url.searchParams.get("role") as SecureSocketRole | null;
    const routeId = url.searchParams.get("route");
    const source = url.searchParams.get("source");
    if (
      url.pathname !== "/secure-connect" ||
      (role !== "secure-host" && role !== "secure-device") ||
      !routeId ||
      !routeIdPattern.test(routeId)
    )
      return new Response("Invalid route", { status: 400 });

    const host = this.authenticatedHost();
    if (role === "secure-host" && this.pendingSecureHostCount() >= maximumPendingHostCandidates)
      return new Response("Host authentication is busy", { status: 409 });
    if (role === "secure-device" && !host) return new Response("Host unavailable", { status: 503 });
    if (role === "secure-device" && (!source || !/^[A-Za-z0-9_-]{22}$/u.test(source)))
      return new Response("Invalid source", { status: 400 });

    let sourceBytes: Uint8Array | null = null;
    if (role === "secure-device") {
      try {
        sourceBytes = decodeBase64Url(source!);
      } catch {
        return new Response("Invalid source", { status: 400 });
      }
      const devices = this.ctx.getWebSockets("secure-device");
      const pendingDevices = this.pendingSecureDeviceCount();
      if (
        devices.length - pendingDevices >= maximumDevicesPerRoom ||
        devices.length >= maximumDevicesPerRoom + maximumPendingDevicesPerRoom ||
        pendingDevices >= maximumPendingDevicesPerRoom ||
        this.pendingSecureDeviceCount(Array.from(sourceBytes)) >= maximumPendingDevicesPerSource
      ) {
        return new Response("Room full", { status: 503 });
      }
    }

    const pair = new WebSocketPair();
    const client = pair[0];
    const server = pair[1];
    const attachment: SecureSocketAttachment = { role, routeId };
    if (role === "secure-host")
      attachment.authenticationExpiresAt = Date.now() + hostAuthenticationTimeoutMs;
    else {
      attachment.sessionId = Array.from(crypto.getRandomValues(new Uint8Array(16)));
      attachment.sourceKey = Array.from(sourceBytes!);
      attachment.phase = "awaiting-offer";
      attachment.negotiationExpiresAt = Date.now() + secureNegotiationTimeoutMs;
    }
    server.serializeAttachment(attachment);
    this.ctx.acceptWebSocket(server, [role]);
    await this.scheduleAlarm();
    if (role === "secure-device" && host && attachment.sessionId && attachment.sourceKey) {
      sendBounded(
        host,
        encodeEnvelope(
          Uint8Array.from(attachment.sessionId),
          Uint8Array.from(attachment.sourceKey),
          relayEnvelopeKind.connected,
        ),
      );
    }
    return new Response(null, { status: 101, webSocket: client });
  }

  async webSocketMessage(socket: WebSocket, message: ArrayBuffer | string): Promise<void> {
    const attachment = socket.deserializeAttachment() as SecureSocketAttachment;
    if (attachment.role === "secure-host" && !attachment.authenticated) {
      if (
        typeof message !== "string" ||
        new TextEncoder().encode(message).length > maximumControlMessageBytes
      ) {
        return socket.close(relayClose.tooLarge, "Host authentication message is too large");
      }
      if (!this.canUseHostCandidate(socket, attachment)) {
        return socket.close(relayClose.unauthorized, "Host authentication failed");
      }
      const result = await processHostAuthentication(
        attachment.routeId,
        message,
        attachment.publicKey && attachment.challenge
          ? { publicKey: attachment.publicKey, challenge: attachment.challenge }
          : undefined,
      );
      if (result.kind === "rejected" || !this.canUseHostCandidate(socket, attachment)) {
        return socket.close(relayClose.unauthorized, "Host authentication failed");
      }
      if (result.kind === "challenge") {
        attachment.publicKey = result.hello.publicKey;
        attachment.challenge = result.challenge;
      } else {
        attachment.authenticated = true;
        delete attachment.challenge;
        delete attachment.authenticationExpiresAt;
      }
      socket.serializeAttachment(attachment);
      if (result.kind === "accepted") {
        for (const device of this.ctx.getWebSockets("secure-device"))
          device.close(relayClose.unavailable, "Host replaced");
        for (const host of this.ctx.getWebSockets("secure-host"))
          if (host !== socket) host.close(relayClose.conflict, "Host authentication superseded");
      }
      socket.send(result.response);
      await this.scheduleAlarm();
      return;
    }

    const bytes =
      typeof message === "string" ? new TextEncoder().encode(message) : new Uint8Array(message);
    if (attachment.role === "secure-device" && bytes.length > maximumInnerMessageBytes) {
      return socket.close(relayClose.tooLarge, "Secure answer is too large");
    }
    const rate = consumeRelayRate(
      attachment.rate,
      attachment.role === "secure-host" ? "host" : "device",
      bytes.length,
    );
    attachment.rate = rate.state;
    socket.serializeAttachment(attachment);
    if (!rate.allowed) return socket.close(relayClose.overloaded, "Signaling rate limit exceeded");

    if (attachment.role === "secure-host") {
      if (typeof message === "string")
        return socket.close(relayClose.invalid, "Signaling host envelopes must be binary");
      const envelope = decodeEnvelope(bytes);
      if (
        !envelope ||
        (envelope.kind !== relayEnvelopeKind.text &&
          envelope.kind !== relayEnvelopeKind.closeDevice &&
          envelope.kind !== relayEnvelopeKind.authenticated)
      ) {
        return socket.close(relayClose.invalid, "Invalid signaling envelope");
      }
      if (
        (envelope.kind === relayEnvelopeKind.closeDevice ||
          envelope.kind === relayEnvelopeKind.authenticated) &&
        envelope.payload.length !== 0
      ) {
        return socket.close(relayClose.invalid, "Invalid signaling control envelope");
      }
      const device = this.findDevice(envelope.sessionId);
      if (!device) return;
      if (envelope.kind === relayEnvelopeKind.authenticated) {
        const deviceAttachment = device.deserializeAttachment() as SecureSocketAttachment;
        deviceAttachment.authenticated = true;
        device.serializeAttachment(deviceAttachment);
        return;
      }
      if (envelope.kind === relayEnvelopeKind.closeDevice) {
        if (envelope.payload.length !== 0)
          return socket.close(relayClose.invalid, "Invalid close envelope");
        device.close(1000, "Host closed signaling session");
        return;
      }
      const deviceAttachment = device.deserializeAttachment() as SecureSocketAttachment;
      const offer = parseSecureDescription(envelope.payload, "secure.offer");
      if (deviceAttachment.phase !== "awaiting-offer" || !offer) {
        device.close(relayClose.invalid, "Invalid secure offer");
        return;
      }
      deviceAttachment.phase = "awaiting-answer";
      device.serializeAttachment(deviceAttachment);
      sendBounded(device, new TextDecoder().decode(envelope.payload));
      return;
    }

    if (
      typeof message !== "string" ||
      attachment.phase !== "awaiting-answer" ||
      !parseSecureDescription(bytes, "secure.answer") ||
      !attachment.sessionId
    ) {
      return socket.close(relayClose.invalid, "Invalid secure answer");
    }
    const host = this.authenticatedHost();
    if (!host) return socket.close(relayClose.unavailable, "Host unavailable");
    sendBounded(
      host,
      encodeEnvelope(Uint8Array.from(attachment.sessionId), bytes, relayEnvelopeKind.text),
    );
    socket.close(1000, "Signaling complete");
  }

  async webSocketClose(socket: WebSocket): Promise<void> {
    const attachment = socket.deserializeAttachment() as SecureSocketAttachment;
    if (attachment.role === "secure-host") {
      if (attachment.authenticated === true) {
        const replacement = this.authenticatedHost();
        if (replacement && replacement !== socket) {
          await this.scheduleAlarm();
          return;
        }
        for (const device of this.ctx.getWebSockets("secure-device"))
          device.close(relayClose.unavailable, "Host unavailable");
      }
    } else if (attachment.sessionId) {
      const host = this.authenticatedHost();
      if (host)
        sendBounded(
          host,
          encodeEnvelope(
            Uint8Array.from(attachment.sessionId),
            new Uint8Array(),
            relayEnvelopeKind.disconnected,
          ),
        );
    }
    await this.scheduleAlarm();
  }

  async webSocketError(socket: WebSocket): Promise<void> {
    await this.webSocketClose(socket);
  }

  async alarm(): Promise<void> {
    const now = Date.now();
    for (const host of this.ctx.getWebSockets("secure-host")) {
      const attachment = host.deserializeAttachment() as SecureSocketAttachment;
      if (
        attachment.authenticated !== true &&
        isHostAuthenticationExpired(attachment.authenticationExpiresAt ?? 0, now)
      ) {
        host.close(relayClose.unauthorized, "Host authentication timed out");
      }
    }
    for (const device of this.ctx.getWebSockets("secure-device")) {
      const attachment = device.deserializeAttachment() as SecureSocketAttachment;
      if ((attachment.negotiationExpiresAt ?? 0) <= now)
        device.close(relayClose.unavailable, "Signaling timed out");
    }
    await this.scheduleAlarm(now);
  }

  private authenticatedHost(): WebSocket | null {
    return (
      this.ctx
        .getWebSockets("secure-host")
        .find(
          (socket) =>
            socket.readyState === WebSocket.OPEN &&
            (socket.deserializeAttachment() as SecureSocketAttachment).authenticated === true,
        ) ?? null
    );
  }

  private pendingSecureHostCount(): number {
    return this.ctx
      .getWebSockets("secure-host")
      .filter(
        (socket) =>
          socket.readyState === WebSocket.OPEN &&
          (socket.deserializeAttachment() as SecureSocketAttachment).authenticated !== true,
      ).length;
  }

  private pendingSecureDeviceCount(sourceKey?: number[]): number {
    return this.ctx.getWebSockets("secure-device").filter((socket) => {
      const attachment = socket.deserializeAttachment() as SecureSocketAttachment;
      if (attachment.authenticated === true) return false;
      if (!sourceKey) return true;
      return (
        (attachment.sourceKey ?? []).length === sourceKey.length &&
        (attachment.sourceKey ?? []).every((value, index) => value === sourceKey[index])
      );
    }).length;
  }

  private canUseHostCandidate(socket: WebSocket, attachment: SecureSocketAttachment): boolean {
    const candidates = this.ctx.getWebSockets("secure-host").filter((candidate) => {
      const value = candidate.deserializeAttachment() as SecureSocketAttachment;
      return candidate.readyState === WebSocket.OPEN && value.authenticated !== true;
    });
    return canClaimHostCandidate(
      candidates.includes(socket),
      socket.readyState === WebSocket.OPEN,
      attachment.authenticationExpiresAt ?? 0,
    );
  }

  private findDevice(sessionId: Uint8Array): WebSocket | null {
    const key = Array.from(sessionId).join(",");
    return (
      this.ctx
        .getWebSockets("secure-device")
        .find(
          (socket) =>
            ((socket.deserializeAttachment() as SecureSocketAttachment).sessionId ?? []).join(
              ",",
            ) === key,
        ) ?? null
    );
  }

  private async scheduleAlarm(now: number = Date.now()): Promise<void> {
    const deadlines = [
      ...this.ctx
        .getWebSockets("secure-host")
        .map((socket) => socket.deserializeAttachment() as SecureSocketAttachment)
        .filter((attachment) => attachment.authenticated !== true)
        .map((attachment) => attachment.authenticationExpiresAt ?? 0),
      ...this.ctx
        .getWebSockets("secure-device")
        .map(
          (socket) =>
            (socket.deserializeAttachment() as SecureSocketAttachment).negotiationExpiresAt ?? 0,
        ),
    ].filter((deadline) => deadline > now);
    if (deadlines.length === 0) await this.ctx.storage.deleteAlarm();
    else await this.ctx.storage.setAlarm(Math.min(...deadlines));
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function sendBounded(socket: WebSocket, value: string | ArrayBuffer | Uint8Array): boolean {
  const byteLength =
    typeof value === "string" ? new TextEncoder().encode(value).length : value.byteLength;
  if (socket.bufferedAmount + byteLength > maximumBufferedBytes) {
    socket.close(relayClose.overloaded, "Relay backpressure limit exceeded");
    return false;
  }
  socket.send(value);
  return true;
}
