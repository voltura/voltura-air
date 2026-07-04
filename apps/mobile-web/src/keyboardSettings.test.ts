import { describe, expect, it } from "vitest";
import { defaultKeyboardSettings, normalizeKeyboardSettings } from "./keyboardSettings";

describe("normalizeKeyboardSettings", () => {
  it("defaults function keys and split mode off and control and arrow keys on for old stored settings", () => {
    expect(normalizeKeyboardSettings({})).toEqual(defaultKeyboardSettings);
    expect(defaultKeyboardSettings).toEqual({
      showFunctionKeys: false,
      showControlKeys: true,
      showArrowKeys: true,
      showSleepButton: true,
      enableSplitMode: false
    });
  });

  it("preserves enabled function keys setting", () => {
    expect(normalizeKeyboardSettings({ showFunctionKeys: true }).showFunctionKeys).toBe(true);
  });

  it("preserves disabled control and arrow key settings", () => {
    expect(normalizeKeyboardSettings({ showControlKeys: false, showArrowKeys: false })).toMatchObject({
      showControlKeys: false,
      showArrowKeys: false
    });
  });

  it("preserves enabled split mode setting", () => {
    expect(normalizeKeyboardSettings({ enableSplitMode: true }).enableSplitMode).toBe(true);
  });
});
