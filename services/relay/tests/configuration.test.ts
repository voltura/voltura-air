import { describe, expect, it } from "vitest";
import { parsePort, validateOptionalTurnPublicIp } from "../src/standalone/configuration";

describe("standalone configuration", () => {
  it.each([
    [undefined, 8787],
    ["1", 1],
    ["8787", 8787],
    ["65535", 65535]
  ])("accepts RELAY_PORT %s", (value, expected) => {
    expect(parsePort(value)).toBe(expected);
  });

  it.each(["", "0", "65536", "8787junk", "8787.5", " 8787", "8787 ", "+8787"])(
    "rejects malformed RELAY_PORT %s",
    (value) => expect(() => parsePort(value)).toThrow("RELAY_PORT must be a valid port.")
  );

  it("accepts a public TURN IPv4 address", () => {
    expect(() => validateOptionalTurnPublicIp("1.1.1.1")).not.toThrow();
  });

  it.each(["", "example.net", "10.0.0.1", "100.64.0.1", "127.0.0.1", "169.254.1.1", "172.16.0.1", "192.168.1.1", "198.51.100.1", "224.0.0.1"])(
    "rejects non-public TURN address %s",
    (value) => expect(() => validateOptionalTurnPublicIp(value)).toThrow("public IPv4")
  );
});
