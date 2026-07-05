import { describe, expect, it } from "vitest";
import { defaultAppSettings, normalizeAppSettings } from "./appSettings";

describe("normalizeAppSettings", () => {
  it("defaults auto refresh on for old stored settings", () => {
    expect(normalizeAppSettings({})).toEqual(defaultAppSettings);
    expect(defaultAppSettings).toEqual({
      autoRefresh: true
    });
  });

  it("preserves disabled auto refresh settings", () => {
    expect(normalizeAppSettings({ autoRefresh: false }).autoRefresh).toBe(false);
  });
});
