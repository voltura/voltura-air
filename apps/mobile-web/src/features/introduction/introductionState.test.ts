import { beforeEach, describe, expect, it, vi } from "vitest";
import { initializeIntroduction, introductionKey, saveIntroduction } from "./introductionState";

beforeEach(() => {
  localStorage.clear();
});

describe("browser introduction state", () => {
  it("establishes eligibility before pairing writes PCs and preserves it across reload", () => {
    expect(initializeIntroduction()).toBe("eligible");
    localStorage.setItem("voltura-air.pcProfiles", '[{"id":"new-pc"}]');
    expect(initializeIntroduction()).toBe("eligible");
    saveIntroduction("suppressed");
    localStorage.removeItem("voltura-air.pcProfiles");
    expect(initializeIntroduction()).toBe("suppressed");
  });
  it("suppresses existing clients even after forgetting every PC", () => {
    localStorage.setItem("voltura-air.pcProfiles", '[{"id":"existing"}]');
    expect(initializeIntroduction()).toBe("suppressed");
    localStorage.removeItem("voltura-air.pcProfiles");
    expect(initializeIntroduction()).toBe("suppressed");
  });
  it.each([
    "invalid",
    "{}",
    '{"version":2,"state":"eligible"}',
    '{"version":1,"state":"eligible","extra":true}',
    '{"version":1,"state":"pending"}',
  ])("suppresses malformed or unknown records: %s", (value) => {
    localStorage.setItem(introductionKey, value);
    expect(initializeIntroduction()).toBe("suppressed");
  });
  it("retains session suppression when writes fail without retrying reads", () => {
    const write = vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => {
      throw new Error("denied");
    });
    saveIntroduction("suppressed");
    expect(initializeIntroduction()).toBe("suppressed");
    expect(write).toHaveBeenCalledTimes(1);
    write.mockRestore();
    // Clear the helper's volatile override for subsequent tests.
    saveIntroduction("suppressed");
  });
});
