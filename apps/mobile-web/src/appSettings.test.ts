import { describe, expect, it } from "vitest";
import { defaultAppSettings, normalizeAppSettings } from "./appSettings";

describe("normalizeAppSettings", () => {
  it("defaults auto refresh on for old stored settings", () => {
    expect(normalizeAppSettings({})).toEqual(defaultAppSettings);
    expect(defaultAppSettings).toEqual({
      autoRefresh: true,
      clearTextAfterSending: true,
      fourthMode: "dictation"
    });
  });

  it("normalizes the configurable fourth mode", () => {
    expect(normalizeAppSettings({ fourthMode: "presentation" }).fourthMode).toBe("presentation");
    expect(normalizeAppSettings({ fourthMode: "text-transfer" }).fourthMode).toBe("text-transfer");
    expect(normalizeAppSettings({ fourthMode: "invalid" as never }).fourthMode).toBe("dictation");
  });

  it("preserves disabled auto refresh settings", () => {
    expect(normalizeAppSettings({ autoRefresh: false }).autoRefresh).toBe(false);
  });
});
