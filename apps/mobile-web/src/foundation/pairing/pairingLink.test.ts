import { describe, expect, it } from "vitest";
import { parsePairingLink, parsePcUrl, validateManualConnectionInput } from "./pairingLink";

const pairToken = "a".repeat(32);
const version = "0.6.1";

describe("parsePairingLink", () => {
  it("reads the generated token, version, and full host hint", () => {
    const hostHint = encodeURIComponent("http://pc.local:51395");

    expect(parsePairingLink(`http://phone.local:5173/pair?t=${pairToken}&v=${version}&h=${hostHint}`)).toEqual({
      pairToken,
      pcUrl: "http://pc.local:51395"
    });
  });

  it("uses the link origin when the generated host hint is absent", () => {
    expect(parsePairingLink(`http://pc.local:51395/pair?t=${pairToken}&v=${version}`)).toEqual({
      pairToken,
      pcUrl: "http://pc.local:51395"
    });
  });

  it("resolves a compact generated host port against the link origin", () => {
    expect(parsePairingLink(`http://phone.local:5173/pair?t=${pairToken}&v=${version}&h=51395`)).toEqual({
      pairToken,
      pcUrl: "http://phone.local:51395"
    });
  });

  it.each([
    [`http://pc.local:51395/pair?t=${pairToken}`, "a missing version"],
    [`http://pc.local:51395/pair?t=short&v=${version}`, "an invalid token"],
    [`http://pc.local:51395/pair?t=${pairToken}&t=${pairToken}&v=${version}`, "duplicate tokens"],
    [`http://pc.local:51395/pair?t=${pairToken}&v=${version}&v=${version}`, "duplicate versions"],
    [`http://pc.local:51395/pair?t=${pairToken}&v=${version}&h=51395&h=51396`, "duplicate host hints"],
    [`http://pc.local:51395/pair?t=${pairToken}&v=preview`, "an invalid version"],
    [`http://user:password@pc.local:51395/pair?t=${pairToken}&v=${version}`, "credentials"],
    [`http://pc.local:51395/pair?t=${pairToken}&v=${version}#fragment`, "a fragment"],
    [`http://phone.local:5173/pair?t=${pairToken}&v=${version}&h=${encodeURIComponent("http://pc.local:51395/path")}`, "a host-hint path"],
    [`http://phone.local:5173/pair?t=${pairToken}&v=${version}&h=99999`, "an invalid host-hint port"],
    [`http://pc.local:51395/?t=${pairToken}&v=${version}`, "the ordinary app path"],
    [`t=${pairToken}&v=${version}&h=51395`, "raw query text"]
  ])("rejects a link containing %s (%s)", (source) => {
    expect(parsePairingLink(source)).toBeNull();
  });
});

describe("validateManualConnectionInput", () => {
  const fallbackUrl = "http://192.168.1.20:5173/app";

  it("normalizes a port against the current page host", () => {
    expect(validateManualConnectionInput("51395", fallbackUrl)).toEqual({
      valid: true,
      target: { kind: "host", pcUrl: "http://192.168.1.20:51395" }
    });
  });

  it("normalizes a host and explicit port", () => {
    expect(validateManualConnectionInput("192.168.1.50:51395", fallbackUrl)).toEqual({
      valid: true,
      target: { kind: "host", pcUrl: "http://192.168.1.50:51395" }
    });
  });

  it("returns a pairing target for a complete Voltura Air pairing link", () => {
    const hostHint = encodeURIComponent("http://pc.local:51395");

    expect(validateManualConnectionInput(
      `http://phone.local:5173/pair?t=${pairToken}&v=${version}&h=${hostHint}`,
      fallbackUrl
    )).toEqual({
      valid: true,
      target: { kind: "pairing", pairToken, pcUrl: "http://pc.local:51395" }
    });
  });

  it.each([
    ["", "Enter a host and port, port number, or Voltura Air pairing link."],
    ["99999", "Enter a host with a valid port, for example 192.168.1.50:51395."],
    ["pc.local", "Enter a host with a valid port, for example 192.168.1.50:51395."],
    ["https://pc.local:51395/path", "Host addresses cannot include a path, query, or fragment."],
    ["https://pc.local:51395/?other=value", "Host addresses cannot include a path, query, or fragment."],
    ["https://user:password@pc.local:51395", "Host addresses cannot include a user name or password."],
    ["ftp://pc.local:51395", "Only HTTP and HTTPS host addresses are supported."],
    [`http://pc.local:51395/pair?t=short&v=${version}`, "Enter the complete pairing link shown by Voltura Air on the PC."],
    [`http://pc.local:51395/?t=${pairToken}&v=${version}`, "Enter the complete pairing link shown by Voltura Air on the PC."]
  ])("rejects %s with a specific message", (value, message) => {
    expect(validateManualConnectionInput(value, fallbackUrl)).toEqual({ valid: false, message });
  });
});

describe("parsePcUrl", () => {
  const fallbackUrl = "http://fallback.local:51395/path";
  const addressWithHint = (hint: string) =>
    `http://client.local:5173/app?h=${encodeURIComponent(hint)}`;

  it.each([
    ["http://pc.local:51395", "http://pc.local:51395"],
    [" https://pc.local:51395/path?ignored=true ", "https://pc.local:51395"],
    ["http://192.168.1.50:51395", "http://192.168.1.50:51395"],
    ["http://[2001:db8::1]:51395", "http://[2001:db8::1]:51395"],
    ["http://workstation.local:51395", "http://workstation.local:51395"],
    ["https://workstation.local:8443", "https://workstation.local:8443"]
  ])("normalizes valid host hint %s", (hint, expected) => {
    expect(parsePcUrl(addressWithHint(hint), fallbackUrl)).toBe(expected);
  });

  it("resolves the supported port-only development hint", () => {
    expect(parsePcUrl("?h=51396", fallbackUrl)).toBe("http://fallback.local:51396");
  });

  it.each([
    "javascript:alert(1)",
    "data:text/plain,hello",
    "file:///C:/Windows/System32",
    "ftp://pc.local:51395",
    "http://user:password@pc.local:51395",
    "http://:51395",
    "http://pc.local:99999",
    "   "
  ])("falls back safely for invalid host hint %s", (hint) => {
    expect(() => parsePcUrl(addressWithHint(hint), fallbackUrl)).not.toThrow();
    expect(parsePcUrl(addressWithHint(hint), fallbackUrl)).toBe("http://client.local:5173");
  });

  it("falls back for empty and malformed encoded hints", () => {
    expect(parsePcUrl("http://client.local:5173/?h=", fallbackUrl)).toBe("http://client.local:5173");
    expect(parsePcUrl("http://client.local:5173/?h=%E0%A4%A", fallbackUrl)).toBe("http://client.local:5173");
  });

  it("handles non-string input without throwing", () => {
    expect(() => parsePcUrl({ host: "javascript:alert(1)" } as unknown, fallbackUrl)).not.toThrow();
    expect(parsePcUrl({ host: "javascript:alert(1)" } as unknown, fallbackUrl)).toBe("http://fallback.local:51395");
  });
});
