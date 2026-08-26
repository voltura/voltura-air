import { p256 } from "@noble/curves/nist.js";
import { hmac } from "@noble/hashes/hmac.js";
import { sha256 } from "@noble/hashes/sha2.js";
import type { PairAcceptedMessage, PairBootstrapChallengeMessage } from "../protocol/messages";
import {
  readLocalStorage,
  removeLocalStorage,
  writeLocalStorage,
} from "../platform/browserStorage";

const reconnectSigningPrefix = "VolturaAir reconnect:v1";
const pairingHostProofPrefix = "VolturaAir pairing host:v1";
const pairingClientProofPrefix = "VolturaAir pairing client:v1";

export interface PairingKeyMaterial {
  privateKey: string;
  reconnectPublicKey: string;
}

export interface PairingBootstrapMaterial {
  clientNonce: string;
  pairTokenId: string;
  token: string;
}

export interface VerifiedPairingBootstrap {
  clientProof: string;
  hostIdentity: { publicKey: string; fingerprint: string };
}

export function createPairingKeyMaterial(): PairingKeyMaterial | null {
  if (!crypto.getRandomValues) {
    return null;
  }

  const { secretKey } = p256.keygen();
  return {
    privateKey: base64Url(secretKey),
    reconnectPublicKey: base64Url(p256.getPublicKey(secretKey, false)),
  };
}

export function createPairingBootstrapMaterial(token: string): PairingBootstrapMaterial | null {
  if (!crypto.getRandomValues) {
    return null;
  }

  const nonce = crypto.getRandomValues(new Uint8Array(32));
  return {
    clientNonce: base64Url(nonce),
    pairTokenId: base64Url(sha256(new TextEncoder().encode(token))),
    token,
  };
}

export function verifyPairingBootstrapChallenge(
  challenge: PairBootstrapChallengeMessage,
  material: PairingBootstrapMaterial,
  clientId: string,
  reconnectPublicKey: string,
): VerifiedPairingBootstrap | null {
  if (
    challenge.clientId !== clientId ||
    challenge.clientNonce !== material.clientNonce ||
    !isValidHostIdentity(challenge.hostIdentity)
  ) {
    return null;
  }

  const expectedHostProof = createPairingProof(
    pairingHostProofPrefix,
    material.token,
    clientId,
    material.clientNonce,
    challenge.serverNonce,
    reconnectPublicKey,
    challenge.hostIdentity.publicKey,
    challenge.hostIdentity.fingerprint,
  );
  if (!constantTimeEqual(expectedHostProof, challenge.proof)) {
    return null;
  }

  return {
    clientProof: createPairingProof(
      pairingClientProofPrefix,
      material.token,
      clientId,
      material.clientNonce,
      challenge.serverNonce,
      reconnectPublicKey,
      challenge.hostIdentity.publicKey,
      challenge.hostIdentity.fingerprint,
    ),
    hostIdentity: challenge.hostIdentity,
  };
}

export function handlePairAccepted(
  message: PairAcceptedMessage,
  pcId: string,
  pendingKey: string | null,
): void {
  if (!pendingKey) {
    return;
  }

  writeLocalStorage(privateKeyStoreKey(message.clientId, pcId), pendingKey);
}

export function isExpectedHostIdentity(
  message: PairAcceptedMessage,
  expectedFingerprint: string | undefined,
): boolean {
  if (!expectedFingerprint) {
    return true;
  }

  const identity = message.hostIdentity;
  if (identity?.fingerprint !== expectedFingerprint) {
    return false;
  }

  try {
    const encoded = decodeBase64Url(identity.publicKey);
    return (
      encoded.length === 65 &&
      encoded[0] === 0x04 &&
      base64Url(sha256(encoded).slice(0, 16)) === expectedFingerprint
    );
  } catch {
    return false;
  }
}

function isValidHostIdentity(identity: { publicKey: string; fingerprint: string }): boolean {
  try {
    const encoded = decodeBase64Url(identity.publicKey);
    return (
      encoded.length === 65 &&
      encoded[0] === 0x04 &&
      base64Url(sha256(encoded).slice(0, 16)) === identity.fingerprint
    );
  } catch {
    return false;
  }
}

function createPairingProof(
  prefix: string,
  token: string,
  clientId: string,
  clientNonce: string,
  serverNonce: string,
  reconnectPublicKey: string,
  hostPublicKey: string,
  hostFingerprint: string,
): string {
  const encodedClientId = base64Url(new TextEncoder().encode(clientId));
  const transcript = [
    prefix,
    encodedClientId,
    clientNonce,
    serverNonce,
    reconnectPublicKey,
    hostPublicKey,
    hostFingerprint,
  ].join("\n");
  return base64Url(
    hmac(sha256, new TextEncoder().encode(token), new TextEncoder().encode(transcript)),
  );
}

function constantTimeEqual(left: string, right: string): boolean {
  if (left.length !== right.length) {
    return false;
  }

  let difference = 0;
  for (let index = 0; index < left.length; index += 1) {
    difference |= left.charCodeAt(index) ^ right.charCodeAt(index);
  }
  return difference === 0;
}

export function hasStoredReconnectKey(clientId: string, pcId: string): boolean {
  return readLocalStorage(privateKeyStoreKey(clientId, pcId)) !== null;
}

export function signReconnectChallenge(
  clientId: string,
  pcId: string,
  challenge: string,
): string | null {
  const privateKey = getStoredPrivateKey(clientId, pcId);
  if (!privateKey) {
    return null;
  }

  const signature = p256.sign(
    new TextEncoder().encode(`${reconnectSigningPrefix}:${clientId}:${challenge}`),
    privateKey,
    { lowS: false },
  );
  return base64Url(signature);
}

export function signClientPayload(clientId: string, pcId: string, payload: string): string | null {
  const privateKey = getStoredPrivateKey(clientId, pcId);
  if (!privateKey) {
    return null;
  }
  return base64Url(p256.sign(new TextEncoder().encode(payload), privateKey, { lowS: false }));
}

export function signPrivateKeyPayload(privateKey: string, payload: Uint8Array): string {
  return base64Url(p256.sign(payload, decodeBase64Url(privateKey), { lowS: false }));
}

export { base64Url, decodeBase64Url };

export function clearStoredReconnectKey(clientId: string, pcId: string): void {
  removeLocalStorage(privateKeyStoreKey(clientId, pcId));
}

export function shouldClearStoredReconnectKeyForRejection(reason: string): boolean {
  return reason === "device-revoked" || reason === "invalid-proof";
}

function getStoredPrivateKey(clientId: string, pcId: string): Uint8Array | null {
  const raw = readLocalStorage(privateKeyStoreKey(clientId, pcId));
  if (!raw) {
    return null;
  }

  try {
    return decodeBase64Url(raw);
  } catch {
    return null;
  }
}

function privateKeyStoreKey(clientId: string, pcId: string): string {
  return `voltura-air.reconnect-key.${clientId}.${pcId}`;
}

function base64Url(bytes: Uint8Array): string {
  let binary = "";
  for (const byte of bytes) {
    binary += String.fromCharCode(byte);
  }

  return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/g, "");
}

function decodeBase64Url(value: string): Uint8Array {
  const padded = value
    .replace(/-/g, "+")
    .replace(/_/g, "/")
    .padEnd(value.length + ((4 - (value.length % 4)) % 4), "=");
  const binary = atob(padded);
  const bytes = new Uint8Array(binary.length);
  for (let index = 0; index < binary.length; index += 1) {
    bytes[index] = binary.charCodeAt(index);
  }

  return bytes;
}
