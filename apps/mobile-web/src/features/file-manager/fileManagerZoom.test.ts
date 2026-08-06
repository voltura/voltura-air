import { describe, expect, it } from "vitest";
import { clampFileManagerTransform, identityFileManagerTransform, updateFileManagerPinch } from "./fileManagerZoom";

describe("Files workspace magnification", () => {
  it("clamps scale from one to five and keeps panning inside the viewport", () => {
    expect(clampFileManagerTransform({ scale: 0.5, x: -20, y: -20 }, 400, 600)).toEqual(identityFileManagerTransform);
    expect(clampFileManagerTransform({ scale: 8, x: -5000, y: 100 }, 400, 600)).toEqual({ scale: 5, x: -1600, y: 0 });
  });

  it("keeps the pinch midpoint over the same workspace content", () => {
    const result = updateFileManagerPinch(
      { distance: 100, midpointX: 200, midpointY: 300, transform: identityFileManagerTransform },
      200,
      200,
      300,
      400,
      600
    );

    expect(result).toEqual({ scale: 2, x: -200, y: -300 });
  });

  it("reclamps existing panning after rotation without changing scale", () => {
    expect(clampFileManagerTransform({ scale: 3, x: -900, y: -1000 }, 600, 350)).toEqual({ scale: 3, x: -900, y: -700 });
  });
});
