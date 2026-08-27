import { describe, expect, it } from "vitest";
import { getAvailableToolModeIds, getEffectiveFourthMode, getModeTabs } from "./appModeTabs";

describe("capability-aware app modes", () => {
  it("removes Presentation and uses Dictation for a stale choice when its capability is unavailable", () => {
    expect(getAvailableToolModeIds(false)).toEqual([
      "dictation",
      "text-transfer",
      "clipboard-read",
    ]);
    expect(getEffectiveFourthMode("presentation", false)).toBe("dictation");
    expect(getModeTabs("presentation", false).at(-1)?.id).toBe("dictation");
  });

  it("restores Presentation choices when the host advertises the capability", () => {
    expect(getAvailableToolModeIds(true)).toContain("presentation");
    expect(getEffectiveFourthMode("presentation", true)).toBe("presentation");
    expect(getModeTabs("presentation", true).at(-1)?.id).toBe("presentation");
  });

  it("keeps Presentation as the default and falls back from Files to Presentation then Dictation", () => {
    expect(getModeTabs("files", true, true).at(-1)?.id).toBe("files");
    expect(getEffectiveFourthMode("files", true, false)).toBe("presentation");
    expect(getModeTabs("files", true, false).at(-1)?.id).toBe("presentation");
    expect(getEffectiveFourthMode("files", false, false)).toBe("dictation");
    expect(getModeTabs("files", false, false).at(-1)?.id).toBe("dictation");
  });

  it("offers Files under Tools only when the host supports it", () => {
    expect(getAvailableToolModeIds(true, false)).not.toContain("files");
    expect(getAvailableToolModeIds(true, true)).toContain("files");
  });

  it("offers Terminal only when the host advertises it and falls back safely", () => {
    expect(getAvailableToolModeIds(true, true, false)).not.toContain("terminal");
    expect(getAvailableToolModeIds(true, true, true)).toContain("terminal");
    expect(getEffectiveFourthMode("terminal", true, true, false)).toBe("presentation");
    expect(getModeTabs("terminal", false, true, true).at(-1)?.id).toBe("terminal");
  });
});
