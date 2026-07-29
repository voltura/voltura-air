import { describe, expect, it } from "vitest";
import { getAvailableToolModeIds, getEffectiveFourthMode, getModeTabs } from "./appModeTabs";

describe("capability-aware app modes", () => {
  it("removes Presentation and uses Dictation for a stale choice when its capability is unavailable", () => {
    expect(getAvailableToolModeIds(false)).toEqual(["dictation", "text-transfer", "clipboard-read"]);
    expect(getEffectiveFourthMode("presentation", false)).toBe("dictation");
    expect(getModeTabs("presentation", false).at(-1)?.id).toBe("dictation");
  });

  it("restores Presentation choices when the host advertises the capability", () => {
    expect(getAvailableToolModeIds(true)).toContain("presentation");
    expect(getEffectiveFourthMode("presentation", true)).toBe("presentation");
    expect(getModeTabs("presentation", true).at(-1)?.id).toBe("presentation");
  });
});
