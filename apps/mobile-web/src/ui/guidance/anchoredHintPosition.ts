import type { CSSProperties } from "react";

export type AnchoredHintPlacement =
  | "below-center"
  | "below-start"
  | "below-end"
  | "above-center"
  | "above-start"
  | "above-end"
  | "right"
  | "left";

export interface VisibleViewportBounds {
  bottom: number;
  height: number;
  left: number;
  right: number;
  top: number;
  width: number;
}

interface Size {
  height: number;
  width: number;
}

interface Point {
  left: number;
  top: number;
}

export interface AnchoredHintLayout {
  clamped: boolean;
  placement: AnchoredHintPlacement;
  style: CSSProperties;
}

interface ComputeAnchoredHintLayoutOptions {
  anchorRect: DOMRectReadOnly;
  fallbackPlacements?: AnchoredHintPlacement[] | undefined;
  gap?: number | undefined;
  hintSize: Size;
  preferredPlacement?: AnchoredHintPlacement | undefined;
  viewport: VisibleViewportBounds;
  viewportPadding?: number | undefined;
}

const defaultPlacementOrder: AnchoredHintPlacement[] = [
  "below-center",
  "above-center",
  "below-start",
  "below-end",
  "above-start",
  "above-end",
  "right",
  "left"
];

const minimumPointerInset = 16;

export function getVisibleViewportBounds(): VisibleViewportBounds {
  const visualViewport = window.visualViewport;
  const left = visualViewport?.offsetLeft ?? 0;
  const top = visualViewport?.offsetTop ?? 0;
  const width = visualViewport?.width ?? window.innerWidth;
  const height = visualViewport?.height ?? window.innerHeight;
  return {
    bottom: top + height,
    height,
    left,
    right: left + width,
    top,
    width
  };
}

export function computeAnchoredHintLayout({
  anchorRect,
  fallbackPlacements,
  gap = 8,
  hintSize,
  preferredPlacement = "below-center",
  viewport,
  viewportPadding = 12
}: ComputeAnchoredHintLayoutOptions): AnchoredHintLayout {
  const placements = getPlacementOrder(preferredPlacement, fallbackPlacements);
  const fitBounds = {
    bottom: viewport.bottom - viewportPadding,
    left: viewport.left + viewportPadding,
    right: viewport.right - viewportPadding,
    top: viewport.top + viewportPadding
  };

  for (const placement of placements) {
    const point = getPlacementPoint(placement, anchorRect, hintSize, gap);
    if (fits(point, hintSize, fitBounds)) {
      return createLayout(placement, point, anchorRect, hintSize, false);
    }
  }

  const placement = placements[0] ?? preferredPlacement;
  const point = getPlacementPoint(placement, anchorRect, hintSize, gap);
  const clampedPoint = {
    left: clamp(point.left, fitBounds.left, Math.max(fitBounds.left, fitBounds.right - hintSize.width)),
    top: clamp(point.top, fitBounds.top, Math.max(fitBounds.top, fitBounds.bottom - hintSize.height))
  };
  return createLayout(placement, clampedPoint, anchorRect, hintSize, true);
}

function getPlacementOrder(
  preferredPlacement: AnchoredHintPlacement,
  fallbackPlacements: AnchoredHintPlacement[] | undefined
): AnchoredHintPlacement[] {
  const ordered = [
    preferredPlacement,
    ...(fallbackPlacements ?? defaultPlacementOrder)
  ];
  return ordered.filter((placement, index) => ordered.indexOf(placement) === index);
}

function getPlacementPoint(
  placement: AnchoredHintPlacement,
  anchorRect: DOMRectReadOnly,
  hintSize: Size,
  gap: number
): Point {
  const anchorCenterX = anchorRect.left + anchorRect.width / 2;
  const anchorCenterY = anchorRect.top + anchorRect.height / 2;

  switch (placement) {
    case "below-start":
      return { left: anchorRect.left, top: anchorRect.bottom + gap };
    case "below-end":
      return { left: anchorRect.right - hintSize.width, top: anchorRect.bottom + gap };
    case "above-center":
      return { left: anchorCenterX - hintSize.width / 2, top: anchorRect.top - hintSize.height - gap };
    case "above-start":
      return { left: anchorRect.left, top: anchorRect.top - hintSize.height - gap };
    case "above-end":
      return { left: anchorRect.right - hintSize.width, top: anchorRect.top - hintSize.height - gap };
    case "right":
      return { left: anchorRect.right + gap, top: anchorCenterY - hintSize.height / 2 };
    case "left":
      return { left: anchorRect.left - hintSize.width - gap, top: anchorCenterY - hintSize.height / 2 };
    case "below-center":
    default:
      return { left: anchorCenterX - hintSize.width / 2, top: anchorRect.bottom + gap };
  }
}

function fits(point: Point, hintSize: Size, bounds: Omit<VisibleViewportBounds, "height" | "width">): boolean {
  return point.left >= bounds.left
    && point.top >= bounds.top
    && point.left + hintSize.width <= bounds.right
    && point.top + hintSize.height <= bounds.bottom;
}

function createLayout(
  placement: AnchoredHintPlacement,
  point: Point,
  anchorRect: DOMRectReadOnly,
  hintSize: Size,
  clamped: boolean
): AnchoredHintLayout {
  const anchorCenterX = anchorRect.left + anchorRect.width / 2;
  const anchorCenterY = anchorRect.top + anchorRect.height / 2;
  const arrowX = clamp(anchorCenterX - point.left, minimumPointerInset, Math.max(minimumPointerInset, hintSize.width - minimumPointerInset));
  const arrowY = clamp(anchorCenterY - point.top, minimumPointerInset, Math.max(minimumPointerInset, hintSize.height - minimumPointerInset));

  return {
    clamped,
    placement,
    style: {
      "--anchored-hint-arrow-x": `${arrowX}px`,
      "--anchored-hint-arrow-y": `${arrowY}px`,
      left: `${point.left}px`,
      top: `${point.top}px`
    } as CSSProperties
  };
}

function clamp(value: number, min: number, max: number): number {
  return Math.min(max, Math.max(min, value));
}
