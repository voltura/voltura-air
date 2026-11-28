import { useCallback, useEffect, useRef, type MouseEvent, type PointerEvent } from "react";
import "./developer-refresh-long-press.css";

const developerRefreshLongPressMs = 700;
const developerRefreshMovementThreshold = 10;

interface DeveloperRefreshLongPressHandlers {
  className: string;
  onContextMenu: (event: MouseEvent<HTMLDivElement>) => void;
  onPointerCancel: (event: PointerEvent<HTMLDivElement>) => void;
  onLostPointerCapture: (event: PointerEvent<HTMLDivElement>) => void;
  onPointerDown: (event: PointerEvent<HTMLDivElement>) => void;
  onPointerMove: (event: PointerEvent<HTMLDivElement>) => void;
  onPointerUp: (event: PointerEvent<HTMLDivElement>) => void;
}

export function useDeveloperRefreshLongPress(
  enabled: boolean,
  refreshApp: () => void | Promise<void>,
): DeveloperRefreshLongPressHandlers {
  const timerRef = useRef<number | null>(null);
  const pointerRef = useRef<{ id: number; x: number; y: number; target: HTMLDivElement } | null>(
    null,
  );

  const cancel = useCallback(() => {
    if (timerRef.current !== null) {
      window.clearTimeout(timerRef.current);
      timerRef.current = null;
    }
    const pointer = pointerRef.current;
    pointerRef.current = null;
    if (pointer?.target.hasPointerCapture?.(pointer.id)) {
      try {
        pointer.target.releasePointerCapture?.(pointer.id);
      } catch {
        // Capture may already have ended with the pointer.
      }
    }
  }, []);

  const onPointerDown = useCallback(
    (event: PointerEvent<HTMLDivElement>) => {
      if (!enabled || event.button !== 0 || pointerRef.current) {
        return;
      }

      pointerRef.current = {
        id: event.pointerId,
        x: event.clientX,
        y: event.clientY,
        target: event.currentTarget,
      };
      try {
        event.currentTarget.setPointerCapture?.(event.pointerId);
      } catch {
        cancel();
        return;
      }
      timerRef.current = window.setTimeout(() => {
        timerRef.current = null;
        void refreshApp();
      }, developerRefreshLongPressMs);
    },
    [cancel, enabled, refreshApp],
  );

  const onPointerMove = useCallback(
    (event: PointerEvent<HTMLDivElement>) => {
      const pointer = pointerRef.current;
      if (
        pointer?.id === event.pointerId &&
        Math.hypot(event.clientX - pointer.x, event.clientY - pointer.y) >
          developerRefreshMovementThreshold
      ) {
        cancel();
      }
    },
    [cancel],
  );

  const onPointerEnd = useCallback(
    (event: PointerEvent<HTMLDivElement>) => {
      if (pointerRef.current?.id === event.pointerId) {
        cancel();
      }
    },
    [cancel],
  );

  const onContextMenu = useCallback((event: MouseEvent<HTMLDivElement>) => {
    if (pointerRef.current) {
      event.preventDefault();
    }
  }, []);

  useEffect(() => cancel, [cancel, enabled, refreshApp]);

  useEffect(() => {
    const onVisibility = () => {
      if (document.visibilityState === "hidden") {
        cancel();
      }
    };
    window.addEventListener("blur", cancel);
    window.addEventListener("pagehide", cancel);
    document.addEventListener("visibilitychange", onVisibility);
    return () => {
      window.removeEventListener("blur", cancel);
      window.removeEventListener("pagehide", cancel);
      document.removeEventListener("visibilitychange", onVisibility);
    };
  }, [cancel]);

  return {
    className: enabled ? "developer-long-press-target" : "",
    onContextMenu,
    onPointerCancel: onPointerEnd,
    onLostPointerCapture: onPointerEnd,
    onPointerDown,
    onPointerMove,
    onPointerUp: onPointerEnd,
  };
}
