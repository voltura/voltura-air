import { useEffect, useLayoutEffect, useState, type RefObject } from "react";
import {
  computeAnchoredHintLayout,
  getVisibleViewportBounds,
  type AnchoredHintPlacement,
} from "./anchoredHintPosition";

interface UseAnchoredHintPositionOptions {
  anchorRef: RefObject<HTMLElement | null>;
  autoUpdate?: boolean | undefined;
  fallbackPlacements?: AnchoredHintPlacement[] | undefined;
  open: boolean;
  preferredPlacement?: AnchoredHintPlacement | undefined;
}

const viewportPadding = 12;

export function useAnchoredHintPosition({
  anchorRef,
  autoUpdate = true,
  fallbackPlacements,
  open,
  preferredPlacement,
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
    const resizeObserver =
      typeof ResizeObserver === "function" ? new ResizeObserver(scheduleLayoutUpdate) : null;

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
  fallbackPlacements: AnchoredHintPlacement[] | undefined,
) {
  const anchorRect = anchorElement.getBoundingClientRect();
  const viewport = getVisibleViewportBounds();
  hintElement.style.maxWidth = `${Math.max(0, viewport.width - viewportPadding * 2)}px`;
  const { clamped, placement, style } = computeAnchoredHintLayout({
    anchorRect,
    fallbackPlacements,
    hintSize: hintElement.getBoundingClientRect(),
    preferredPlacement,
    viewport,
    viewportPadding,
  });
  hintElement.dataset.placement = placement;
  hintElement.toggleAttribute("data-clamped", clamped);
  hintElement.style.setProperty("--anchored-hint-arrow-x", style["--anchored-hint-arrow-x"]);
  hintElement.style.setProperty("--anchored-hint-arrow-y", style["--anchored-hint-arrow-y"]);
  hintElement.style.left = style.left;
  hintElement.style.top = style.top;
}
