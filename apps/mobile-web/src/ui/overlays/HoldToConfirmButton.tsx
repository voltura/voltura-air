import {
  forwardRef,
  useLayoutEffect,
  useRef,
  useState,
  type CSSProperties,
  type KeyboardEvent,
  type PointerEvent,
} from "react";
import "./hold-to-confirm.css";

const holdDurationMs = 1600;
const holdTickMs = 40;

interface HeldPointer {
  id: number;
  target: HTMLButtonElement;
  x: number;
  y: number;
}

function isPointerInside({ target, x, y }: HeldPointer) {
  const { left, top, right, bottom } = target.getBoundingClientRect();
  return right > left && bottom > top && x >= left && x <= right && y >= top && y <= bottom;
}

export const HoldToConfirmButton = forwardRef<
  HTMLButtonElement,
  { disabled: boolean; label: string; onConfirm: () => void }
>(function HoldToConfirmButton({ disabled, label, onConfirm }, ref) {
  const [progress, setProgress] = useState(0);
  const intervalRef = useRef<number | null>(null);
  const timeoutRef = useRef<number | null>(null);
  const startedAtRef = useRef(0);
  const completedRef = useRef(false);
  const pointerRef = useRef<HeldPointer | null>(null);
  const keyRef = useRef<string | null>(null);
  const confirmRef = useRef(onConfirm);
  useLayoutEffect(() => {
    confirmRef.current = onConfirm;
  }, [onConfirm]);

  const clearHold = (reset = true) => {
    const pointer = pointerRef.current;
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
    if (pointer?.target.hasPointerCapture?.(pointer.id)) {
      try {
        pointer.target.releasePointerCapture?.(pointer.id);
      } catch {
        // Capture may already have ended with the pointer.
      }
    }
    if (reset && !completedRef.current) {
      setProgress(0);
    }
  };

  useLayoutEffect(() => {
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

  useLayoutEffect(() => {
    clearHold();
    if (disabled) {
      return;
    }
    completedRef.current = false;
    setProgress(0);
  }, [disabled, label]);

  const beginHold = (target: HTMLButtonElement) => {
    if (disabled || timeoutRef.current !== null || completedRef.current) {
      return;
    }
    const remainsValid = () =>
      target.isConnected &&
      !target.disabled &&
      document.visibilityState !== "hidden" &&
      (pointerRef.current === null || isPointerInside(pointerRef.current));
    startedAtRef.current = performance.now();
    setProgress(0.01);
    intervalRef.current = window.setInterval(() => {
      if (!remainsValid()) {
        clearHold();
        return;
      }
      setProgress(Math.min(1, (performance.now() - startedAtRef.current) / holdDurationMs));
    }, holdTickMs);
    timeoutRef.current = window.setTimeout(() => {
      if (!remainsValid()) {
        clearHold();
        return;
      }
      completedRef.current = true;
      setProgress(1);
      clearHold(false);
      confirmRef.current();
    }, holdDurationMs);
  };

  const onPointerDown = (event: PointerEvent<HTMLButtonElement>) => {
    if (disabled || event.button !== 0 || timeoutRef.current !== null || completedRef.current) {
      return;
    }
    event.preventDefault();
    try {
      event.currentTarget.setPointerCapture?.(event.pointerId);
    } catch {
      /* Capture is optional; leaving the button cancels. */
    }
    pointerRef.current = {
      id: event.pointerId,
      target: event.currentTarget,
      x: event.clientX,
      y: event.clientY,
    };
    beginHold(event.currentTarget);
  };

  const onPointerMove = (event: PointerEvent<HTMLButtonElement>) => {
    const pointer = pointerRef.current;
    if (pointer?.id !== event.pointerId) {
      return;
    }
    // Captured moves still target the button when the pointer is outside it.
    pointer.x = event.clientX;
    pointer.y = event.clientY;
    if (!isPointerInside(pointer)) {
      clearHold();
    }
  };

  const onKeyDown = (event: KeyboardEvent<HTMLButtonElement>) => {
    if ((event.key === " " || event.key === "Enter") && !event.repeat) {
      event.preventDefault();
      if (timeoutRef.current !== null) {
        return;
      }
      keyRef.current = event.key;
      beginHold(event.currentTarget);
    }
  };

  const cancelHold = () => {
    clearHold();
  };
  const finishPointer = (event: PointerEvent<HTMLButtonElement>) => {
    if (pointerRef.current === null || pointerRef.current.id === event.pointerId) {
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
      onPointerMove={onPointerMove}
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
