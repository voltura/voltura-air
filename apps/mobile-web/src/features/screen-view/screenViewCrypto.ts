import { p256 } from "@noble/curves/nist.js";
import { sha256 } from "@noble/hashes/sha2.js";

const textEncoder = new TextEncoder();

export function hashScreenSdp(sdp: string): string {
  return encodeBase64Url(sha256(textEncoder.encode(sdp)));
}

export function verifyHostScreenSignature(publicKey: string, signature: string, transcript: string): boolean {
  try {
    return p256.verify(decodeBase64Url(signature), textEncoder.encode(transcript), decodeBase64Url(publicKey), { lowS: false });
  } catch {
    return false;
  }
}

function encodeBase64Url(bytes: Uint8Array): string {
  let binary = "";
  for (const byte of bytes) {binary += String.fromCharCode(byte);}
  return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/g, "");
}

function decodeBase64Url(value: string): Uint8Array {
  const binary = atob(value.replace(/-/g, "+").replace(/_/g, "/").padEnd(value.length + ((4 - value.length % 4) % 4), "="));
  return Uint8Array.from(binary, (character) => character.charCodeAt(0));
}
