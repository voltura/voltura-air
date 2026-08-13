import { hashSessionDescription, verifyHostSessionSignature } from "../../foundation/webrtc/sessionCrypto";

export function hashScreenSdp(sdp: string): string {
  return hashSessionDescription(sdp);
}

export function verifyHostScreenSignature(publicKey: string, signature: string, transcript: string): boolean {
  return verifyHostSessionSignature(publicKey, signature, transcript);
}
