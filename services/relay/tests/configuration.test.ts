import { describe, expect, it } from "vitest";
import { validateOptionalTurnPublicIp } from "../src/standalone/configuration";

describe("standalone configuration", () => {
  it("accepts a public TURN IPv4 address", () => {
    expect(() => validateOptionalTurnPublicIp("1.1.1.1")).not.toThrow();
  });

  it.each(["", "example.net", "10.0.0.1", "100.64.0.1", "127.0.0.1", "169.254.1.1", "172.16.0.1", "192.168.1.1", "198.51.100.1", "224.0.0.1"])(
    "rejects non-public TURN address %s",
    (value) => expect(() => validateOptionalTurnPublicIp(value)).toThrow("public IPv4")
  );
});
