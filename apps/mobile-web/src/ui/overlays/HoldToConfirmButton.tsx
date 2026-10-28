import {
  forwardRef,
  useEffect,
  useRef,
  useState,
  type CSSProperties,
  type KeyboardEvent,
  type PointerEvent,
} from "react";
import "./hold-to-confirm.css";

const holdDurationMs = 1600;
const holdTickMs = 40;

export const HoldToConfirmButton = forwardRef<
  HTMLButtonElement,
  { disabled: boolean; label: string; onConfirm: () => void }
>(function HoldToConfirmButton({ disabled, label, onConfirm }, ref) {
  const [progress, setProgress] = useState(0);
  const intervalRef = useRef<number | null>(null);
  const timeoutRef = useRef<number | null>(null);
  const startedAtRef = useRef(0);
  const completedRef = useRef(false);
  const pointerRef = useRef<number | null>(null);
  const keyRef = useRef<string | null>(null);
  const confirmRef = useRef(onConfirm);
  useEffect(() => {
    confirmRef.current = onConfirm;
  }, [onConfirm]);

  const clearHold = (reset = true) => {
    pointerRef.current = null;
    keyRef.current = null;
    if (intervalRef.current !== null) {
      window.clearInterval(intervalRef.current);
      intervalRef.current = null;
    }
    if (timeoutRef.current !== null) {
      window.clearTimeout(timeoutRef.current);
      timeoutRef.current = null;
    }
    if (reset && !completedRef.current) {
      setProgress(0);
    }
  };

  useEffect(() => {
    const cancelOnWindowBlur = () => {
      clearHold();
    };
    const cancelWhenHidden = () => {
      if (document.visibilityState === "hidden") {
        clearHold();
      }
    };
    window.addEventListener("blur", cancelOnWindowBlur);
    window.addEventListener("pagehide", cancelOnWindowBlur);
    document.addEventListener("visibilitychange", cancelWhenHidden);
    return () => {
      clearHold(false);
      window.removeEventListener("blur", cancelOnWindowBlur);
      window.removeEventListener("pagehide", cancelOnWindowBlur);
      document.removeEventListener("visibilitychange", cancelWhenHidden);
    };
  }, []);

  useEffect(() => {
    clearHold();
    if (disabled) {
      return;
    }
    completedRef.current = false;
    setProgress(0);
  }, [disabled, label]);

  const beginHold = () => {
    if (disabled || timeoutRef.current !== null || completedRef.current) {
      return;
    }
    startedAtRef.current = performance.now();
    setProgress(0.01);
    intervalRef.current = window.setInterval(() => {
      setProgress(Math.min(1, (performance.now() - startedAtRef.current) / holdDurationMs));
    }, holdTickMs);
    timeoutRef.current = window.setTimeout(() => {
      completedRef.current = true;
      setProgress(1);
      clearHold(false);
      confirmRef.current();
    }, holdDurationMs);
  };

  const onPointerDown = (event: PointerEvent<HTMLButtonElement>) => {
    if (event.button !== 0 || timeoutRef.current !== null || completedRef.current) {
      return;
    }
    event.preventDefault();
    try {
      event.currentTarget.setPointerCapture?.(event.pointerId);
    } catch {
      /* Capture is optional; leaving the button cancels. */
    }
    pointerRef.current = event.pointerId;
    beginHold();
  };

  const onKeyDown = (event: KeyboardEvent<HTMLButtonElement>) => {
    if ((event.key === " " || event.key === "Enter") && !event.repeat) {
      event.preventDefault();
      if (timeoutRef.current !== null) {
        return;
      }
      keyRef.current = event.key;
      beginHold();
    }
  };

  const cancelHold = () => {
    clearHold();
  };
  const finishPointer = (event: PointerEvent<HTMLButtonElement>) => {
    if (pointerRef.current === null || pointerRef.current === event.pointerId) {
      clearHold();
    }
  };
  const style = { "--hold-progress": `${Math.round(progress * 100)}%` } as CSSProperties;

  return (
    <button
      className="hold-confirm-button"
      ref={ref}
      type="button"
      disabled={disabled}
      style={style}
      aria-label={`Hold to ${label.toLocaleLowerCase()}`}
      onClick={(event) => {
        event.preventDefault();
      }}
      onKeyDown={onKeyDown}
      onKeyUp={(event) => {
        if (event.key === keyRef.current) {
          clearHold();
        }
      }}
      onBlur={cancelHold}
      onPointerCancel={finishPointer}
      onLostPointerCapture={finishPointer}
      onPointerDown={onPointerDown}
      onPointerLeave={finishPointer}
      onPointerUp={finishPointer}
    >
      <span>
        {disabled
          ? "Wait for the current power request to finish."
          : progress > 0
            ? "Keep holding…"
            : `Hold to ${label.toLocaleLowerCase()}`}
      </span>
    </button>
  );
});
