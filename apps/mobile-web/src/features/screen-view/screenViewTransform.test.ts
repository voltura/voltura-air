import { describe, expect, it } from "vitest";
import { identityScreenViewTransform, updateScreenViewPinch } from "./screenViewTransform";

describe("screen view pinch transform", () => {
  it("zooms around the midpoint and keeps the view inside its viewport", () => {
    const transformed = updateScreenViewPinch({
      distance: 100,
      midpointX: 200,
      midpointY: 150,
      transform: identityScreenViewTransform
    }, 200, 200, 150, 400, 300);

    expect(transformed).toEqual({ scale: 2, x: -200, y: -150 });
  });

  it("uses midpoint movement to pan a magnified view", () => {
    const transformed = updateScreenViewPinch({
      distance: 100,
      midpointX: 200,
      midpointY: 150,
      transform: { scale: 2, x: -200, y: -150 }
    }, 100, 240, 180, 400, 300);

    expect(transformed).toEqual({ scale: 2, x: -160, y: -120 });
  });

  it("returns exactly to the fitted view when pinched below one times", () => {
    const transformed = updateScreenViewPinch({
      distance: 100,
      midpointX: 100,
      midpointY: 100,
      transform: { scale: 1.2, x: -20, y: -20 }
    }, 50, 100, 100, 400, 300);

    expect(transformed).toEqual(identityScreenViewTransform);
  });
});
