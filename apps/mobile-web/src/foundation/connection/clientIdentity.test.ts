import { afterEach, describe, expect, it } from "vitest";
import { clearPairTokenFromAddress, ensureClientMetadataInAddress } from "./clientIdentity";

describe("clearPairTokenFromAddress", () => {
  afterEach(() => window.history.replaceState(null, "", "/"));

  it("removes a relay fragment without removing non-secret route metadata", () => {
    window.history.replaceState(null, "", `/air/app/?r=${"r".repeat(22)}&v=0.8.5#${"t".repeat(32)}`);

    clearPairTokenFromAddress();

    expect(window.location.hash).toBe("");
    expect(window.location.search).toBe(`?r=${"r".repeat(22)}&v=0.8.5`);
  });
});

describe("ensureClientMetadataInAddress", () => {
  afterEach(() => window.history.replaceState(null, "", "/"));

  it("does not place device identity in a hosted relay URL", () => {
    window.history.replaceState(null, "", `/air/app/?r=${"r".repeat(22)}&v=0.8.5#${"t".repeat(32)}`);

    ensureClientMetadataInAddress("client-private", "Private phone");

    const address = new URL(window.location.href);
    expect(address.searchParams.has("d")).toBe(false);
    expect(address.searchParams.has("n")).toBe(false);
  });
});
