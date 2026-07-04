import { describe, expect, it } from "vitest";
import { parsePairingLink } from "./pairingLink";

describe("parsePairingLink", () => {
  it("reads a token and host URL from a full pairing link", () => {
    expect(parsePairingLink("http://phone.local/?t=abc&h=http%3A%2F%2Fpc.local%3A51395%2Fpair", "http://fallback")).toEqual({
      pairToken: "abc",
      pcUrl: "http://pc.local:51395"
    });
  });

  it("uses the link origin when host URL is not present", () => {
    expect(parsePairingLink("http://pc.local:51395/pair?t=abc", "http://fallback")).toEqual({
      pairToken: "abc",
      pcUrl: "http://pc.local:51395"
    });
  });

  it("resolves a compact host port against the link origin", () => {
    expect(parsePairingLink("http://phone.local:5173/?t=abc&h=51395", "http://fallback")).toEqual({
      pairToken: "abc",
      pcUrl: "http://phone.local:51395"
    });
  });

  it("resolves a raw compact host port against the supplied fallback PC URL", () => {
    expect(parsePairingLink("t=abc&h=51395", "http://phone.local:5173/app")).toEqual({
      pairToken: "abc",
      pcUrl: "http://phone.local:51395"
    });
  });

  it("reads raw query parameters with the supplied fallback PC URL", () => {
    expect(parsePairingLink("t=abc", "http://fallback/app")).toEqual({
      pairToken: "abc",
      pcUrl: "http://fallback"
    });
  });

  it("rejects text without a pairing token", () => {
    expect(parsePairingLink("hello", "http://fallback")).toBeNull();
  });
});
