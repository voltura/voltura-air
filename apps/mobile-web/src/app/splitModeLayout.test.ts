import { describe, expect, it } from "vitest";
import { getStableScreenOrientation, splitModeMinimumWidth, supportsSplitModeLayout } from "./splitModeLayout";

describe("supportsSplitModeLayout", () => {
  it.each([
    ["portrait phone", 390, 844, false],
    ["landscape phone", 667, 375, true],
    ["portrait tablet", 768, 1024, false],
    ["landscape tablet", 1024, 768, true]
  ])("handles the %s breakpoint", (_label, width, height, expected) => {
    expect(supportsSplitModeLayout(width, height)).toBe(expected);
  });

  it("activates at the minimum landscape width", () => {
    expect(supportsSplitModeLayout(splitModeMinimumWidth - 1, 360)).toBe(false);
    expect(supportsSplitModeLayout(splitModeMinimumWidth, 360)).toBe(true);
  });

  it.each([
    ["portrait screen", 800, 1200, "portrait" as const, true, false],
    ["portrait keyboard viewport", 800, 500, "portrait" as const, true, false],
    ["landscape touch screen", 1200, 800, "landscape" as const, true, true],
    ["narrow landscape touch screen", 639, 360, "landscape" as const, true, false],
    ["desktop landscape window", 900, 600, "portrait" as const, false, true],
    ["desktop portrait window", 600, 900, "landscape" as const, false, false]
  ])("handles stable orientation for a %s", (_label, width, height, orientation, touch, expected) => {
    expect(supportsSplitModeLayout(width, height, orientation, touch)).toBe(expected);
  });

  it("falls back to stable screen dimensions without an Orientation API value", () => {
    expect(getStableScreenOrientation({ width: 800, height: 1200 })).toBe("portrait");
    expect(getStableScreenOrientation({ width: 1200, height: 800 })).toBe("landscape");
  });
});
