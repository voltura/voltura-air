import { describe, expect, it } from "vitest";
import { parseSecureDescription } from "../src/cloudflare/secureDirectProtocol";

const bytes = (value: string) => new TextEncoder().encode(value);

describe("Secure Direct signaling descriptions", () => {
  it.each(["secure.offer", "secure.answer"] as const)("accepts one bounded %s", (type) => {
    const value = JSON.stringify({ type, sdp: "v=0\r\n" });
    expect(parseSecureDescription(bytes(value), type)).toBe(value);
  });

  it.each([
    "{}",
    JSON.stringify({ type: "secure.offer", sdp: "" }),
    JSON.stringify({ type: "secure.offer", sdp: "v=0", extra: true }),
    JSON.stringify({ type: "secure.answer", sdp: "v=0" })
  ])("rejects malformed or wrong-phase offers", (value) => {
    expect(parseSecureDescription(bytes(value), "secure.offer")).toBeNull();
  });

  it("rejects oversized SDP", () => {
    expect(parseSecureDescription(bytes(JSON.stringify({ type: "secure.offer", sdp: "a".repeat(32 * 1024 + 1) })), "secure.offer")).toBeNull();
  });
});
