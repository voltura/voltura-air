import { describe, expect, it } from "vitest";
import {
  identityScreenViewTransform,
  normalizedScreenPoint,
  screenCursorImagePosition,
  updateScreenViewPinch,
} from "./screenViewTransform";

describe("screen view pinch transform", () => {
  it("normalizes only points inside the rendered screen and clamps edge releases", () => {
    const bounds = { left: 100, top: 50, width: 800, height: 600 };

    expect(normalizedScreenPoint(500, 350, bounds)).toEqual({ x: 0.5, y: 0.5 });
    expect(normalizedScreenPoint(99, 350, bounds)).toBeNull();
    expect(normalizedScreenPoint(950, 20, bounds, true)).toEqual({ x: 1, y: 0 });
  });

  it("draws cursor shapes from the Desktop Duplication top-left position", () => {
    expect(screenCursorImagePosition(320, 240, 100, 50, 0.5)).toEqual({ left: 260, top: 170 });
  });

  it("zooms around the midpoint and keeps the view inside its viewport", () => {
    const transformed = updateScreenViewPinch(
      {
        distance: 100,
        midpointX: 200,
        midpointY: 150,
        transform: identityScreenViewTransform,
      },
      200,
      200,
      150,
      400,
      300,
    );

    expect(transformed).toEqual({ scale: 2, x: -200, y: -150 });
  });

  it("clamps excessive pinch zoom at ten times", () => {
    const transformed = updateScreenViewPinch(
      {
        distance: 100,
        midpointX: 200,
        midpointY: 150,
        transform: identityScreenViewTransform,
      },
      2_000,
      200,
      150,
      400,
      300,
    );

    expect(transformed).toEqual({ scale: 10, x: -1_800, y: -1_350 });
  });

  it("uses midpoint movement to pan a magnified view", () => {
    const transformed = updateScreenViewPinch(
      {
        distance: 100,
        midpointX: 200,
        midpointY: 150,
        transform: { scale: 2, x: -200, y: -150 },
      },
      100,
      240,
      180,
      400,
      300,
    );

    expect(transformed).toEqual({ scale: 2, x: -160, y: -120 });
  });

  it("returns exactly to the fitted view when pinched below one times", () => {
    const transformed = updateScreenViewPinch(
      {
        distance: 100,
        midpointX: 100,
        midpointY: 100,
        transform: { scale: 1.2, x: -20, y: -20 },
      },
      50,
      100,
      100,
      400,
      300,
    );

    expect(transformed).toEqual(identityScreenViewTransform);
  });
});
