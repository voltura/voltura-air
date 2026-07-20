import { type ReactNode, type RefObject } from "react";
import { createPortal } from "react-dom";
import { useAnchoredHintPosition } from "./useAnchoredHintPosition";
import type { AnchoredHintPlacement } from "./anchoredHintPosition";

interface AnchoredHintProps {
  anchorRef: RefObject<HTMLElement | null>;
  autoUpdate?: boolean | undefined;
  children: ReactNode;
  fallbackPlacements?: AnchoredHintPlacement[] | undefined;
  open: boolean;
  preferredPlacement?: AnchoredHintPlacement | undefined;
}

export function AnchoredHint({
  anchorRef,
  autoUpdate,
  children,
  fallbackPlacements,
  open,
  preferredPlacement
}: AnchoredHintProps) {
  const hintRef = useAnchoredHintPosition({
    anchorRef,
    autoUpdate,
    fallbackPlacements,
    open,
    preferredPlacement
  });

  if (!open) {
    return null;
  }

  return createPortal(
    <div
      ref={hintRef}
      className="anchored-hint"
      data-placement={preferredPlacement ?? "below-center"}
      role="status"
      aria-live="polite"
    >
      {children}
    </div>,
    document.body
  );
}
