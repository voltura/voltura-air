import {
  useCallback,
  useEffect,
  useRef,
  type MouseEvent,
  type PointerEvent,
  type TouchEventHandler,
} from "react";
import { ArrowDown, ArrowLeft, ArrowRight, ArrowUp } from "lucide-react";
import "./custom-screen-navigation-ring.css";

const repeatDelayMs = 400;
const repeatMs = 55;
type NavigationKey = "ArrowUp" | "ArrowLeft" | "ArrowRight" | "ArrowDown";

interface CustomScreenNavigationRingProps {
  enabled: boolean;
  name: string;
  reason?: string | null | undefined;
  onCenterKey: () => void;
  onTouchCancel: TouchEventHandler<HTMLDivElement>;
  onTouchEnd: TouchEventHandler<HTMLDivElement>;
  onTouchMove: TouchEventHandler<HTMLDivElement>;
  onTouchStart: TouchEventHandler<HTMLDivElement>;
  sendSpecial: (key: string) => void;
}

export function CustomScreenNavigationRing({
  enabled,
  name,
  reason,
  onCenterKey,
  onTouchCancel,
  onTouchEnd,
  onTouchMove,
  onTouchStart,
  sendSpecial,
}: CustomScreenNavigationRingProps) {
  const repeatTimeoutRef = useRef<number | null>(null);
  const repeatIntervalRef = useRef<number | null>(null);

  const stopRepeat = useCallback(() => {
    window.clearTimeout(repeatTimeoutRef.current ?? undefined);
    window.clearInterval(repeatIntervalRef.current ?? undefined);
    repeatTimeoutRef.current = null;
    repeatIntervalRef.current = null;
  }, []);

  useEffect(() => {
    const stopWhenBlurred = () => {
      stopRepeat();
    };
    const stopWhenHidden = () => {
      if (document.visibilityState === "hidden") {
        stopRepeat();
      }
    };
    window.addEventListener("blur", stopWhenBlurred);
    document.addEventListener("visibilitychange", stopWhenHidden);
    return () => {
      window.removeEventListener("blur", stopWhenBlurred);
      document.removeEventListener("visibilitychange", stopWhenHidden);
      stopRepeat();
    };
  }, [stopRepeat]);

  useEffect(() => {
    if (!enabled) {
      stopRepeat();
    }
  }, [enabled, stopRepeat]);

  const startRepeat = (event: PointerEvent<HTMLButtonElement>) => {
    if (!enabled || event.button !== 0) {
      return;
    }
    const key = event.currentTarget.dataset.key as NavigationKey;
    event.preventDefault();
    event.currentTarget.setPointerCapture?.(event.pointerId);
    stopRepeat();
    sendSpecial(key);
    repeatTimeoutRef.current = window.setTimeout(() => {
      sendSpecial(key);
      repeatIntervalRef.current = window.setInterval(() => {
        sendSpecial(key);
      }, repeatMs);
    }, repeatDelayMs);
  };

  const click = (event: MouseEvent<HTMLButtonElement>) => {
    if (enabled && event.detail === 0) {
      const key = event.currentTarget.dataset.key as NavigationKey;
      sendSpecial(key);
    }
  };

  const stopTouchPropagation: TouchEventHandler<HTMLElement> = (event) => {
    event.stopPropagation();
  };

  const forwardCenterTouch =
    (handler: TouchEventHandler<HTMLDivElement>): TouchEventHandler<HTMLDivElement> =>
    (event) => {
      event.stopPropagation();
      handler(event);
    };

  return (
    <div
      aria-disabled={!enabled}
      aria-label={`${name} trackpad`}
      className="trackpad-surface custom-screen-navigation-surface"
      role="application"
      title={enabled ? name : (reason ?? "Remote input is unavailable.")}
      onTouchCancel={enabled ? onTouchCancel : undefined}
      onTouchEnd={enabled ? onTouchEnd : undefined}
      onTouchMove={enabled ? onTouchMove : undefined}
      onTouchStart={enabled ? onTouchStart : undefined}
    >
      <div aria-label={name} className="custom-screen-navigation-ring" role="group">
        <button
          type="button"
          className="custom-screen-ring-zone custom-screen-ring-up"
          data-key="ArrowUp"
          disabled={!enabled}
          aria-label="D-pad up"
          onClick={click}
          onLostPointerCapture={stopRepeat}
          onPointerCancel={stopRepeat}
          onPointerDown={startRepeat}
          onPointerUp={stopRepeat}
          onTouchCancel={stopTouchPropagation}
          onTouchEnd={stopTouchPropagation}
          onTouchMove={stopTouchPropagation}
          onTouchStart={stopTouchPropagation}
        >
          <ArrowUp aria-hidden="true" />
        </button>
        <button
          type="button"
          className="custom-screen-ring-zone custom-screen-ring-left"
          data-key="ArrowLeft"
          disabled={!enabled}
          aria-label="D-pad left"
          onClick={click}
          onLostPointerCapture={stopRepeat}
          onPointerCancel={stopRepeat}
          onPointerDown={startRepeat}
          onPointerUp={stopRepeat}
          onTouchCancel={stopTouchPropagation}
          onTouchEnd={stopTouchPropagation}
          onTouchMove={stopTouchPropagation}
          onTouchStart={stopTouchPropagation}
        >
          <ArrowLeft aria-hidden="true" />
        </button>
        <button
          type="button"
          className="custom-screen-ring-zone custom-screen-ring-right"
          data-key="ArrowRight"
          disabled={!enabled}
          aria-label="D-pad right"
          onClick={click}
          onLostPointerCapture={stopRepeat}
          onPointerCancel={stopRepeat}
          onPointerDown={startRepeat}
          onPointerUp={stopRepeat}
          onTouchCancel={stopTouchPropagation}
          onTouchEnd={stopTouchPropagation}
          onTouchMove={stopTouchPropagation}
          onTouchStart={stopTouchPropagation}
        >
          <ArrowRight aria-hidden="true" />
        </button>
        <button
          type="button"
          className="custom-screen-ring-zone custom-screen-ring-down"
          data-key="ArrowDown"
          disabled={!enabled}
          aria-label="D-pad down"
          onClick={click}
          onLostPointerCapture={stopRepeat}
          onPointerCancel={stopRepeat}
          onPointerDown={startRepeat}
          onPointerUp={stopRepeat}
          onTouchCancel={stopTouchPropagation}
          onTouchEnd={stopTouchPropagation}
          onTouchMove={stopTouchPropagation}
          onTouchStart={stopTouchPropagation}
        >
          <ArrowDown aria-hidden="true" />
        </button>
        <div
          aria-disabled={!enabled}
          aria-label="Mini trackpad"
          className="custom-screen-mini-trackpad"
          onKeyDown={(event) => {
            if (!enabled || (event.key !== "Enter" && event.key !== " ")) {
              return;
            }
            event.preventDefault();
            onCenterKey();
          }}
          onTouchCancel={enabled ? forwardCenterTouch(onTouchCancel) : undefined}
          onTouchEnd={enabled ? forwardCenterTouch(onTouchEnd) : undefined}
          onTouchMove={enabled ? forwardCenterTouch(onTouchMove) : undefined}
          onTouchStart={enabled ? forwardCenterTouch(onTouchStart) : undefined}
          role="button"
          tabIndex={enabled ? 0 : -1}
        >
          <span aria-hidden="true" />
        </div>
      </div>
    </div>
  );
}
