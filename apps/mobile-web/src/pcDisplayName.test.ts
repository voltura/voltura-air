import { describe, expect, it } from "vitest";
import { getPcDisplayName } from "./pcDisplayName";

describe("getPcDisplayName", () => {
  it("uses a custom PC name when one is set", () => {
    expect(getPcDisplayName({ customName: true, name: "Office", url: "http://192.168.1.20:51395" })).toBe("Office");
  });

  it("hides generated host and IP-style names behind a friendly default", () => {
    expect(getPcDisplayName({ customName: false, name: "192.168.1.20", url: "http://192.168.1.20:51395" })).toBe("PC");
    expect(getPcDisplayName({ customName: false, name: "192.168.1.20:51395", url: "http://192.168.1.20:51395" })).toBe("PC");
  });

  it("uses host-provided names when they are descriptive", () => {
    expect(getPcDisplayName({ customName: false, name: "JOAKIM-PC", url: "http://192.168.1.20:51395" })).toBe("JOAKIM-PC");
  });
});
