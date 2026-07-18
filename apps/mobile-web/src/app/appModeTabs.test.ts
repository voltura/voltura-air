import { describe, expect, it } from "vitest";
import { getAvailableToolModeIds, getEffectiveFourthMode, getModeTabs } from "./appModeTabs";

describe("alpha-aware app modes", () => {
  it("removes Presentation and uses Dictation for a stale Presentation preference when alpha is unavailable", () => {
    expect(getAvailableToolModeIds(false)).toEqual(["dictation", "text-transfer", "clipboard-read"]);
    expect(getEffectiveFourthMode("presentation", false)).toBe("dictation");
    expect(getModeTabs("presentation", false).at(-1)?.id).toBe("dictation");
  });

  it("restores Presentation choices when the host advertises the alpha feature", () => {
    expect(getAvailableToolModeIds(true)).toContain("presentation");
    expect(getEffectiveFourthMode("presentation", true)).toBe("presentation");
    expect(getModeTabs("presentation", true).at(-1)?.id).toBe("presentation");
  });
});
