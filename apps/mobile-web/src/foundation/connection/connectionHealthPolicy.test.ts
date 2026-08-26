import { describe, expect, it } from "vitest";
import {
  getNextHealthCheckDelay,
  getNextInputAckCheckDelay,
  hasExpiredInputAck,
} from "./connectionHealthPolicy";

describe("connection health policy", () => {
  it("only expires acknowledged input when the capability is active", () => {
    expect(hasExpiredInputAck([1000], false, 5000)).toBe(false);
    expect(hasExpiredInputAck([1000], true, 5000)).toBe(true);
    expect(hasExpiredInputAck([2000], true, 5000)).toBe(false);
  });

  it("uses interactive and passive health intervals", () => {
    expect(getNextHealthCheckDelay(1, 0, 1000, 2000)).toBe(9000);
    expect(getNextHealthCheckDelay(0, 0, 1000, 20000)).toBe(41000);
    expect(getNextHealthCheckDelay(0, 20000, 1000, 20000)).toBe(10000);
  });

  it("checks pending input acknowledgements at their own deadline", () => {
    expect(getNextInputAckCheckDelay([1000, 2000], 3000)).toBe(1501);
    expect(getNextInputAckCheckDelay([], 3000)).toBe(Number.POSITIVE_INFINITY);
  });
});
