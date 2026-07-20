import { useEffect, useLayoutEffect, useState, type RefObject } from "react";
import type { AnchoredHintPlacement } from "./anchoredHintPosition";

interface UseAnchoredHintPositionOptions {
  anchorRef: RefObject<HTMLElement | null>;
  autoUpdate?: boolean | undefined;
  fallbackPlacements?: AnchoredHintPlacement[] | undefined;
  open: boolean;
  preferredPlacement?: AnchoredHintPlacement | undefined;
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

const viewportPadding = 12;

export function useAnchoredHintPosition({
  anchorRef,
  autoUpdate = true,
  fallbackPlacements,
  open,
  preferredPlacement
}: UseAnchoredHintPositionOptions) {
  const [hintElement, setHintElement] = useState<HTMLDivElement | null>(null);

  useLayoutEffect(() => {
    if (!open || !hintElement || !anchorRef.current) {
      return;
    }

    applyCurrentLayout(anchorRef.current, hintElement, preferredPlacement, fallbackPlacements);
  }, [anchorRef, fallbackPlacements, hintElement, open, preferredPlacement]);

  useEffect(() => {
    if (!open || !autoUpdate || !hintElement || !anchorRef.current) {
      return;
    }

    const anchorElement = anchorRef.current;
    const visualViewport = window.visualViewport;
    let animationFrame = 0;
    const updateLayout = () => {
      animationFrame = 0;
      applyCurrentLayout(anchorElement, hintElement, preferredPlacement, fallbackPlacements);
    };
    const scheduleLayoutUpdate = () => {
      if (animationFrame === 0) {
        animationFrame = window.requestAnimationFrame(updateLayout);
      }
    };
    const resizeObserver = typeof ResizeObserver === "function"
      ? new ResizeObserver(scheduleLayoutUpdate)
      : null;

    visualViewport?.addEventListener("resize", scheduleLayoutUpdate);
    visualViewport?.addEventListener("scroll", scheduleLayoutUpdate);
    window.addEventListener("resize", scheduleLayoutUpdate);
    window.addEventListener("scroll", scheduleLayoutUpdate, true);
    resizeObserver?.observe(anchorElement);
    resizeObserver?.observe(hintElement);
    scheduleLayoutUpdate();

    return () => {
      visualViewport?.removeEventListener("resize", scheduleLayoutUpdate);
      visualViewport?.removeEventListener("scroll", scheduleLayoutUpdate);
      window.removeEventListener("resize", scheduleLayoutUpdate);
      window.removeEventListener("scroll", scheduleLayoutUpdate, true);
      resizeObserver?.disconnect();
      if (animationFrame !== 0) {
        window.cancelAnimationFrame(animationFrame);
      }
    };
  }, [anchorRef, autoUpdate, fallbackPlacements, hintElement, open, preferredPlacement]);

  return setHintElement;
}

function applyCurrentLayout(
  anchorElement: HTMLElement,
  hintElement: HTMLElement,
  preferredPlacement: AnchoredHintPlacement | undefined,
  fallbackPlacements: AnchoredHintPlacement[] | undefined
) {
  const anchorRect = anchorElement.getBoundingClientRect();
  const visualViewport = window.visualViewport;
  const viewportLeft = visualViewport?.offsetLeft ?? 0;
  const viewportTop = visualViewport?.offsetTop ?? 0;
  const viewportWidth = visualViewport?.width ?? window.innerWidth;
  const viewportHeight = visualViewport?.height ?? window.innerHeight;
  hintElement.style.maxWidth = `${Math.max(0, viewportWidth - viewportPadding * 2)}px`;
  const hintRect = hintElement.getBoundingClientRect();
  const bounds = {
    bottom: viewportTop + viewportHeight - viewportPadding,
    left: viewportLeft + viewportPadding,
    right: viewportLeft + viewportWidth - viewportPadding,
    top: viewportTop + viewportPadding
  };
  const placements = [
    preferredPlacement ?? "below-center",
    ...(fallbackPlacements ?? defaultPlacementOrder)
  ].filter((placement, index, ordered) => ordered.indexOf(placement) === index);

  for (const placement of placements) {
    const point = getPlacementPoint(placement, anchorRect, hintRect, 8);
    if (
      point.left >= bounds.left
      && point.top >= bounds.top
      && point.left + hintRect.width <= bounds.right
      && point.top + hintRect.height <= bounds.bottom
    ) {
      applyLayout(hintElement, placement, point.left, point.top, anchorRect, hintRect, false);
      return;
    }
  }

  const placement = placements[0] ?? "below-center";
  const point = getPlacementPoint(placement, anchorRect, hintRect, 8);
  applyLayout(
    hintElement,
    placement,
    clamp(point.left, bounds.left, Math.max(bounds.left, bounds.right - hintRect.width)),
    clamp(point.top, bounds.top, Math.max(bounds.top, bounds.bottom - hintRect.height)),
    anchorRect,
    hintRect,
    true
  );
}

function getPlacementPoint(
  placement: AnchoredHintPlacement,
  anchorRect: DOMRect,
  hintRect: DOMRect,
  gap: number
) {
  const anchorCenterX = anchorRect.left + anchorRect.width / 2;
  const anchorCenterY = anchorRect.top + anchorRect.height / 2;

  if (placement === "below-start") {
    return { left: anchorRect.left, top: anchorRect.bottom + gap };
  }
  if (placement === "below-end") {
    return { left: anchorRect.right - hintRect.width, top: anchorRect.bottom + gap };
  }
  if (placement === "above-center") {
    return { left: anchorCenterX - hintRect.width / 2, top: anchorRect.top - hintRect.height - gap };
  }
  if (placement === "above-start") {
    return { left: anchorRect.left, top: anchorRect.top - hintRect.height - gap };
  }
  if (placement === "above-end") {
    return { left: anchorRect.right - hintRect.width, top: anchorRect.top - hintRect.height - gap };
  }
  if (placement === "right") {
    return { left: anchorRect.right + gap, top: anchorCenterY - hintRect.height / 2 };
  }
  if (placement === "left") {
    return { left: anchorRect.left - hintRect.width - gap, top: anchorCenterY - hintRect.height / 2 };
  }
  return { left: anchorCenterX - hintRect.width / 2, top: anchorRect.bottom + gap };
}

function applyLayout(
  hintElement: HTMLElement,
  placement: AnchoredHintPlacement,
  left: number,
  top: number,
  anchorRect: DOMRect,
  hintRect: DOMRect,
  clamped: boolean
) {
  const arrowX = clamp(anchorRect.left + anchorRect.width / 2 - left, 16, Math.max(16, hintRect.width - 16));
  const arrowY = clamp(anchorRect.top + anchorRect.height / 2 - top, 16, Math.max(16, hintRect.height - 16));
  hintElement.dataset.placement = placement;
  hintElement.toggleAttribute("data-clamped", clamped);
  hintElement.style.setProperty("--anchored-hint-arrow-x", `${arrowX}px`);
  hintElement.style.setProperty("--anchored-hint-arrow-y", `${arrowY}px`);
  hintElement.style.left = `${left}px`;
  hintElement.style.top = `${top}px`;
}

function clamp(value: number, min: number, max: number): number {
  return Math.min(max, Math.max(min, value));
}
