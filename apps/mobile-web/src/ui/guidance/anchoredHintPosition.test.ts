import { describe, expect, it } from "vitest";
import { computeAnchoredHintLayout, type VisibleViewportBounds } from "./anchoredHintPosition";

const viewport: VisibleViewportBounds = {
  bottom: 320,
  height: 320,
  left: 0,
  right: 320,
  top: 0,
  width: 320
};

function rect(left: number, top: number, width: number, height: number): DOMRectReadOnly {
  return {
    bottom: top + height,
    height,
    left,
    right: left + width,
    top,
    width,
    x: left,
    y: top,
    toJSON: () => ({})
  };
}

describe("computeAnchoredHintLayout", () => {
  it("uses the preferred placement when it fits", () => {
    const layout = computeAnchoredHintLayout({
      anchorRect: rect(140, 40, 40, 44),
      hintSize: { width: 120, height: 40 },
      preferredPlacement: "below-center",
      viewport
    });

    expect(layout.placement).toBe("below-center");
    expect(layout.clamped).toBe(false);
    expect(layout.style.top).toBe("92px");
  });

  it("falls back above when below no longer fits the visible viewport", () => {
    const layout = computeAnchoredHintLayout({
      anchorRect: rect(140, 270, 40, 44),
      hintSize: { width: 120, height: 40 },
      preferredPlacement: "below-center",
      viewport
    });

    expect(layout.placement).toBe("above-center");
    expect(layout.clamped).toBe(false);
  });

  it("clamps inside the visible viewport when no placement fully fits", () => {
    const layout = computeAnchoredHintLayout({
      anchorRect: rect(4, 140, 24, 24),
      fallbackPlacements: ["below-start"],
      hintSize: { width: 360, height: 360 },
      preferredPlacement: "below-start",
      viewport
    });

    expect(layout.placement).toBe("below-start");
    expect(layout.clamped).toBe(true);
    expect(layout.style.left).toBe("12px");
    expect(layout.style.top).toBe("12px");
  });
});
