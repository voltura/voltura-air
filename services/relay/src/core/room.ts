import { encodeEnvelope, sessionIdKey } from "./envelope";
import { maximumBufferedBytes, maximumDevicesPerRoom, maximumRelayPayloadBytes, relayClose } from "./constants";
import { consumeRelayRate, type RelayRateRole, type RelayRateState } from "./rateLimit";

export interface RelaySocket {
  readonly bufferedAmount: number;
  send(value: Uint8Array): void;
  close(code: number, reason: string): void;
}

export class RelayRoom {
  private host: RelaySocket | null = null;
  private readonly devices = new Map<string, { socket: RelaySocket; sessionId: Uint8Array }>();
  private readonly rateStates = new WeakMap<RelaySocket, RelayRateState>();

  get deviceCount(): number { return this.devices.size; }
  get hasHost(): boolean { return this.host !== null; }

  attachHost(socket: RelaySocket): boolean {
    if (this.host) {
      socket.close(relayClose.conflict, "A host is already connected");
      return false;
    }
    this.host = socket;
    return true;
  }

  detachHost(socket: RelaySocket): void {
    if (this.host !== socket) return;
    this.host = null;
    for (const device of this.devices.values()) device.socket.close(relayClose.unavailable, "Host disconnected");
    this.devices.clear();
  }

  attachDevice(socket: RelaySocket, sessionId: Uint8Array): boolean {
    const key = sessionIdKey(sessionId);
    if (!this.host || this.devices.size >= maximumDevicesPerRoom || this.devices.has(key)) {
      socket.close(this.host ? relayClose.overloaded : relayClose.unavailable, this.host ? "Room is full" : "Host unavailable");
      return false;
    }
    this.devices.set(key, { socket, sessionId: sessionId.slice() });
    return true;
  }

  detachDevice(socket: RelaySocket): void {
    for (const [key, device] of this.devices) {
      if (device.socket === socket) this.devices.delete(key);
    }
  }

  forwardDevicePayload(socket: RelaySocket, payload: Uint8Array): boolean {
    if (payload.length > maximumRelayPayloadBytes) {
      socket.close(relayClose.tooLarge, "Message is too large");
      return false;
    }
    const device = Array.from(this.devices.values()).find((candidate) => candidate.socket === socket);
    if (!device || !this.host || !this.consumeRate(socket, "device", payload.length)) return false;
    const envelope = encodeEnvelope(device.sessionId, payload, 3);
    if (!this.canSend(this.host, envelope.length)) return false;
    this.host.send(envelope);
    return true;
  }

  forwardHostPayload(sessionId: Uint8Array, payload: Uint8Array): boolean {
    if (payload.length > maximumRelayPayloadBytes) return false;
    const device = this.devices.get(sessionIdKey(sessionId));
    if (!device || !this.host || !this.consumeRate(this.host, "host", payload.length) || !this.canSend(device.socket, payload.length)) return false;
    device.socket.send(payload);
    return true;
  }

  private canSend(socket: RelaySocket, outgoingBytes: number): boolean {
    if (socket.bufferedAmount + outgoingBytes <= maximumBufferedBytes) return true;
    socket.close(relayClose.overloaded, "Relay backpressure limit exceeded");
    this.detachDevice(socket);
    if (this.host === socket) this.detachHost(socket);
    return false;
  }

  private consumeRate(socket: RelaySocket, role: RelayRateRole, byteLength: number): boolean {
    const result = consumeRelayRate(this.rateStates.get(socket), role, byteLength);
    this.rateStates.set(socket, result.state);
    if (result.allowed) return true;
    socket.close(relayClose.overloaded, "Relay rate limit exceeded");
    this.detachDevice(socket);
    if (this.host === socket) this.detachHost(socket);
    return false;
  }
}
