import { maximumRelayPayloadBytes, relayProtocolVersion } from "./constants";

const headerBytes = 18;

export const relayEnvelopeKind = {
  text: 0,
  connected: 1,
  disconnected: 2,
  binary: 3,
  closeDevice: 4
} as const;

export interface RelayEnvelope {
  kind: number;
  sessionId: Uint8Array;
  payload: Uint8Array;
}

export function encodeEnvelope(sessionId: Uint8Array, payload: Uint8Array, kind: number = relayEnvelopeKind.binary): Uint8Array {
  if (sessionId.length !== 16) throw new TypeError("Relay session IDs are 16 bytes.");
  if (payload.length > maximumRelayPayloadBytes) throw new RangeError("Relay payload is too large.");
  const result = new Uint8Array(headerBytes + payload.length);
  result[0] = relayProtocolVersion;
  if (!Object.values(relayEnvelopeKind).includes(kind as 0 | 1 | 2 | 3 | 4)) throw new TypeError("Invalid relay envelope kind.");
  result[1] = kind;
  result.set(sessionId, 2);
  result.set(payload, headerBytes);
  return result;
}

export function decodeEnvelope(value: ArrayBuffer | Uint8Array): RelayEnvelope | null {
  const bytes = value instanceof Uint8Array ? value : new Uint8Array(value);
  if (bytes.length < headerBytes || bytes.length > headerBytes + maximumRelayPayloadBytes ||
      bytes[0] !== relayProtocolVersion || !Object.values(relayEnvelopeKind).includes(bytes[1] as 0 | 1 | 2 | 3 | 4)) return null;
  return { kind: bytes[1]!, sessionId: bytes.slice(2, headerBytes), payload: bytes.slice(headerBytes) };
}

export function sessionIdKey(sessionId: Uint8Array): string {
  if (sessionId.length !== 16) throw new TypeError("Relay session IDs are 16 bytes.");
  return Array.from(sessionId, (byte) => byte.toString(16).padStart(2, "0")).join("");
}
