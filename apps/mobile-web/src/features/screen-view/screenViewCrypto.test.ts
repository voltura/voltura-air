import { p256 } from "@noble/curves/nist.js";
import { describe, expect, it } from "vitest";
import { hashScreenSdp, verifyHostScreenSignature } from "./screenViewCrypto";

const encoder = new TextEncoder();

describe("screen WebRTC signaling identity", () => {
  it("hashes SDP deterministically and verifies the pinned PC signature", () => {
    const privateKey = new Uint8Array(32);
    privateKey[31] = 7;
    const publicKey = encodeBase64Url(p256.getPublicKey(privateKey, false));
    const offer = "v=0\r\na=group:BUNDLE 0 1\r\n";
    const hash = hashScreenSdp(offer);
    const transcript = `VolturaAir screen-view:offer:v2:client:operation:display:${hash}`;
    const signature = encodeBase64Url(p256.sign(encoder.encode(transcript), privateKey, { lowS: false }));

    expect(hash).toHaveLength(43);
    expect(verifyHostScreenSignature(publicKey, signature, transcript)).toBe(true);
    expect(verifyHostScreenSignature(publicKey, signature, `${transcript}-modified`)).toBe(false);
  });

  it("rejects malformed public keys and signatures", () => {
    expect(verifyHostScreenSignature("not-a-key", "not-a-signature", "transcript")).toBe(false);
  });
});

function encodeBase64Url(bytes: Uint8Array): string {
  let binary = "";
  for (const byte of bytes) {binary += String.fromCharCode(byte);}
  return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/g, "");
}
