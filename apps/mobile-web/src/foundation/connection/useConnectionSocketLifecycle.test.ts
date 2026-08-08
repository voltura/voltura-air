import { describe, expect, it } from "vitest";
import { getConnectionTimeoutMs } from "./useConnectionSocketLifecycle";

describe("getConnectionTimeoutMs", () => {
  it("keeps direct connections at three seconds", () => {
    expect(getConnectionTimeoutMs(undefined)).toBe(3000);
  });

  it("allows relay connections ten seconds", () => {
    expect(getConnectionTimeoutMs("relay")).toBe(10000);
  });
});
