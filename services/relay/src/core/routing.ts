import { decodeBase64Url, encodeBase64Url } from "./base64url";
import { relayHostTranscriptPrefix, routeIdPattern } from "./constants";

export interface RelayHostHello {
  type: "relay.host.hello";
  routeId: string;
  publicKey: string;
}

export interface RelayHostProof {
  type: "relay.host.proof";
  signature: string;
}

export function parseHostHello(value: unknown, expectedRouteId: string): RelayHostHello | null {
  if (
    !isRecord(value) ||
    value.type !== "relay.host.hello" ||
    value.routeId !== expectedRouteId ||
    typeof value.publicKey !== "string" ||
    Object.keys(value).length !== 3
  ) {
    return null;
  }

  return routeIdPattern.test(value.routeId) && /^[A-Za-z0-9_-]{87}$/.test(value.publicKey)
    ? { type: value.type, routeId: value.routeId, publicKey: value.publicKey }
    : null;
}

export function parseHostProof(value: unknown): RelayHostProof | null {
  return isRecord(value) &&
    value.type === "relay.host.proof" &&
    typeof value.signature === "string" &&
    /^[A-Za-z0-9_-]{86}$/.test(value.signature) &&
    Object.keys(value).length === 2
    ? { type: value.type, signature: value.signature }
    : null;
}

export async function deriveRouteId(publicKey: string): Promise<string> {
  const encoded = decodeBase64Url(publicKey);
  if (encoded.length !== 65 || encoded[0] !== 4)
    throw new TypeError("Relay key must be uncompressed P-256.");
  const digest = new Uint8Array(await crypto.subtle.digest("SHA-256", copyBuffer(encoded)));
  return encodeBase64Url(digest.subarray(0, 16));
}

export async function deriveRelaySourceKey(routeId: string, source: string): Promise<Uint8Array> {
  if (!routeIdPattern.test(routeId) || source.length === 0 || source.length > 128) {
    throw new TypeError("Relay source identity is invalid.");
  }
  const transcript = new TextEncoder().encode(`VolturaAir relay source:v1\n${routeId}\n${source}`);
  const digest = new Uint8Array(await crypto.subtle.digest("SHA-256", copyBuffer(transcript)));
  return digest.slice(0, 16);
}

export function createHostTranscript(routeId: string, challenge: string): Uint8Array {
  return new TextEncoder().encode(`${relayHostTranscriptPrefix}\n${routeId}\n${challenge}`);
}

export async function verifyHostProof(
  publicKey: string,
  routeId: string,
  challenge: string,
  signature: string,
): Promise<boolean> {
  return verifySignature(publicKey, createHostTranscript(routeId, challenge), signature, routeId);
}

export async function verifySignature(
  publicKey: string,
  payload: Uint8Array,
  signature: string,
  expectedRouteId?: string,
): Promise<boolean> {
  try {
    if (expectedRouteId && (await deriveRouteId(publicKey)) !== expectedRouteId) return false;
    const encoded = decodeBase64Url(publicKey);
    const key = await crypto.subtle.importKey(
      "raw",
      copyBuffer(encoded),
      { name: "ECDSA", namedCurve: "P-256" },
      false,
      ["verify"],
    );
    return await crypto.subtle.verify(
      { name: "ECDSA", hash: "SHA-256" },
      key,
      copyBuffer(decodeBase64Url(signature)),
      copyBuffer(payload),
    );
  } catch {
    return false;
  }
}

function copyBuffer(value: Uint8Array): ArrayBuffer {
  return Uint8Array.from(value).buffer;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}
