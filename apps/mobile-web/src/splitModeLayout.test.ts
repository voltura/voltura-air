import { describe, expect, it } from "vitest";
import { splitModeMinimumWidth, supportsSplitModeLayout } from "./splitModeLayout";

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
});
