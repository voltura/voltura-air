import { describe, expect, it } from "vitest";
import { getNextHealthCheckDelay, hasExpiredInputAck } from "./connectionHealthPolicy";

describe("connection health policy", () => {
  it("only expires acknowledged input when the capability is active", () => {
    expect(hasExpiredInputAck([1000], false, 5000)).toBe(false);
    expect(hasExpiredInputAck([1000], true, 5000)).toBe(true);
    expect(hasExpiredInputAck([2000], true, 5000)).toBe(false);
  });

  it("uses interactive and passive health intervals", () => {
    expect(getNextHealthCheckDelay(1, 0, 1000, 2000)).toBe(9000);
    expect(getNextHealthCheckDelay(0, 0, 1000, 20000)).toBe(41000);
  });
});
