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
  const pointerRef = useRef<{ id: number; x: number; y: number } | null>(null);
  const triggeredRef = useRef(false);

  const cancel = useCallback(() => {
    if (timerRef.current !== null) {
      window.clearTimeout(timerRef.current);
      timerRef.current = null;
    }
    pointerRef.current = null;
  }, []);

  const onPointerDown = useCallback(
    (event: PointerEvent<HTMLDivElement>) => {
      if (!enabled || event.button !== 0) {
        return;
      }

      cancel();
      triggeredRef.current = false;
      pointerRef.current = { id: event.pointerId, x: event.clientX, y: event.clientY };
      if (typeof event.currentTarget.setPointerCapture === "function") {
        event.currentTarget.setPointerCapture(event.pointerId);
      }
      timerRef.current = window.setTimeout(() => {
        triggeredRef.current = true;
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
      cancel();
      if (
        typeof event.currentTarget.hasPointerCapture === "function" &&
        event.currentTarget.hasPointerCapture(event.pointerId) &&
        typeof event.currentTarget.releasePointerCapture === "function"
      ) {
        event.currentTarget.releasePointerCapture(event.pointerId);
      }
      triggeredRef.current = false;
    },
    [cancel],
  );

  const onContextMenu = useCallback((event: MouseEvent<HTMLDivElement>) => {
    if (triggeredRef.current) {
      event.preventDefault();
    }
  }, []);

  useEffect(() => cancel, [cancel, enabled]);

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
