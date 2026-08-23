import { describe, expect, it } from "vitest";
import {
  canAcceptHostCandidate,
  canClaimHostCandidate,
  createHostTranscript,
  decodeEnvelope,
  deriveRelaySourceKey,
  deriveRouteId,
  encodeBase64Url,
  encodeEnvelope,
  maximumRelayPayloadBytes,
  processHostAuthentication,
  isHostAuthenticationExpired,
  nextHostAuthenticationDeadline,
  relayEnvelopeKind
} from "../src/core/index";

describe("relay route identity", () => {
  it("derives a stable 128-bit route identifier from an uncompressed P-256 key", async () => {
    const key = await crypto.subtle.generateKey({ name: "ECDSA", namedCurve: "P-256" }, true, ["sign", "verify"]);
    const publicKey = encodeBase64Url(new Uint8Array(await crypto.subtle.exportKey("raw", key.publicKey)));
    const first = await deriveRouteId(publicKey);
    expect(first).toMatch(/^[A-Za-z0-9_-]{22}$/);
    await expect(deriveRouteId(publicKey)).resolves.toBe(first);
  });

  it("authenticates the persistent host using the shared transcript", async () => {
    const key = await crypto.subtle.generateKey({ name: "ECDSA", namedCurve: "P-256" }, true, ["sign", "verify"]);
    const publicKey = encodeBase64Url(new Uint8Array(await crypto.subtle.exportKey("raw", key.publicKey)));
    const routeId = await deriveRouteId(publicKey);
    const challenged = await processHostAuthentication(routeId, JSON.stringify({ type: "relay.host.hello", routeId, publicKey }));
    expect(challenged.kind).toBe("challenge");
    if (challenged.kind !== "challenge") throw new Error("Expected host challenge.");
    const signature = encodeBase64Url(new Uint8Array(await crypto.subtle.sign(
      { name: "ECDSA", hash: "SHA-256" },
      key.privateKey,
      Uint8Array.from(createHostTranscript(routeId, challenged.challenge)).buffer)));
    const accepted = await processHostAuthentication(
      routeId,
      JSON.stringify({ type: "relay.host.proof", signature }),
      { publicKey, challenge: challenged.challenge });
    expect(accepted.kind).toBe("accepted");
  });

  it("does not let an unauthenticated host candidate reserve the route", () => {
    expect(canAcceptHostCandidate(false)).toBe(true);
    expect(canAcceptHostCandidate(true)).toBe(false);
    expect(canClaimHostCandidate(false, true, true, 11_000, 1_000)).toBe(true);
    expect(canClaimHostCandidate(false, false, true, 11_000, 1_000)).toBe(false);
    expect(canClaimHostCandidate(false, true, true, 999, 1_000)).toBe(false);
    expect(canClaimHostCandidate(false, true, true, 1_000, 1_000)).toBe(false);
  });

  it("expires idle host authentication and schedules only the next live deadline", () => {
    expect(isHostAuthenticationExpired(999, 1_000)).toBe(true);
    expect(isHostAuthenticationExpired(1_000, 1_000)).toBe(true);
    expect(isHostAuthenticationExpired(1_001, 1_000)).toBe(false);
    expect(nextHostAuthenticationDeadline([900, 1_500, 1_200], 1_000)).toBe(1_200);
    expect(nextHostAuthenticationDeadline([900, 1_000], 1_000)).toBeNull();
  });

  it("derives stable route-scoped opaque relay source keys", async () => {
    const first = await deriveRelaySourceKey("A".repeat(22), "203.0.113.10");
    const same = await deriveRelaySourceKey("A".repeat(22), "203.0.113.10");
    const otherRoute = await deriveRelaySourceKey("B".repeat(22), "203.0.113.10");

    expect(first).toHaveLength(16);
    expect(same).toEqual(first);
    expect(otherRoute).not.toEqual(first);
    await expect(deriveRelaySourceKey("A".repeat(22), "")).rejects.toThrow();
  });
});

describe("relay envelopes", () => {
  it("preserves text and binary inner-frame kinds", () => {
    const session = new Uint8Array(16);
    const text = decodeEnvelope(encodeEnvelope(session, new TextEncoder().encode("{}"), relayEnvelopeKind.text));
    const binary = decodeEnvelope(encodeEnvelope(session, new Uint8Array([1, 2]), relayEnvelopeKind.binary));
    const closeDevice = decodeEnvelope(encodeEnvelope(session, new Uint8Array(), relayEnvelopeKind.closeDevice));

    expect(text?.kind).toBe(relayEnvelopeKind.text);
    expect(binary?.kind).toBe(relayEnvelopeKind.binary);
    expect(closeDevice?.kind).toBe(relayEnvelopeKind.closeDevice);
    expect(closeDevice?.payload).toHaveLength(0);
    expect(decodeEnvelope(new Uint8Array([...encodeEnvelope(session, new Uint8Array(), relayEnvelopeKind.closeDevice), 1]))).toBeNull();
  });

  it("allows only the fixed encryption overhead above a 64 KiB application frame", () => {
    const session = new Uint8Array(16);
    const maximumPayload = new Uint8Array(maximumRelayPayloadBytes);

    expect(decodeEnvelope(encodeEnvelope(session, maximumPayload))?.payload).toHaveLength(maximumRelayPayloadBytes);
    expect(() => encodeEnvelope(session, new Uint8Array(maximumRelayPayloadBytes + 1))).toThrow(RangeError);
  });
});
