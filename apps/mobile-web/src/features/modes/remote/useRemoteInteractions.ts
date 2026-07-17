import { useEffect, useRef, type HTMLAttributes, type KeyboardEvent, type PointerEvent } from "react";
import { isInteractiveRemoteTarget, roundRemoteDelta } from "./remotePointerMath";
import type { RepeatablePressProps } from "./RemoteButton";

const repeatDelayMs = 400;
const repeatMs = 55;
const trackpadScale = 1.35;
const tapDistance = 8;
const doubleTapMs = 280;
const doubleTapDistance = 24;

interface PointerState {
  id: number;
  startX: number;
  startY: number;
  lastX: number;
  lastY: number;
  maxDistance: number;
}

interface PendingTap {
  timeout: number;
  x: number;
  y: number;
  time: number;
}

interface RemoteInteractionOptions {
  isKodiMode: boolean;
  navigationRing: boolean;
  onPointerButtonClick: (button: "left" | "right") => void;
  onPointerMove: (dx: number, dy: number) => void;
  sendSpecial: (key: string, modifiers?: string[]) => void;
}

export function useRemoteInteractions({
  isKodiMode,
  navigationRing,
  onPointerButtonClick,
  onPointerMove,
  sendSpecial
}: RemoteInteractionOptions) {
  const repeatTimeoutRef = useRef<number | null>(null);
  const repeatIntervalRef = useRef<number | null>(null);
  const ignoreNextClickRef = useRef(false);
  const miniTrackpadPointerRef = useRef<PointerState | null>(null);
  const navigationPanelPointerRef = useRef<PointerState | null>(null);
  const pendingTapRef = useRef<PendingTap | null>(null);

  const stopRepeatingPress = () => {
    if (repeatTimeoutRef.current !== null) {
      window.clearTimeout(repeatTimeoutRef.current);
      repeatTimeoutRef.current = null;
    }

    if (repeatIntervalRef.current !== null) {
      window.clearInterval(repeatIntervalRef.current);
      repeatIntervalRef.current = null;
    }
  };

  const clearPendingTap = () => {
    if (pendingTapRef.current !== null) {
      window.clearTimeout(pendingTapRef.current.timeout);
      pendingTapRef.current = null;
    }
  };

  useEffect(
    () => () => {
      stopRepeatingPress();
      clearPendingTap();
    },
    []
  );

  const getRepeatablePressProps = (action: () => void): RepeatablePressProps => ({
    onPointerDown: (event) => {
      if (event.button !== 0) {
        return;
      }

      event.preventDefault();
      ignoreNextClickRef.current = true;
      event.currentTarget.setPointerCapture?.(event.pointerId);
      stopRepeatingPress();
      action();
      repeatTimeoutRef.current = window.setTimeout(() => {
        action();
        repeatIntervalRef.current = window.setInterval(action, repeatMs);
      }, repeatDelayMs);
    },
    onPointerUp: stopRepeatingPress,
    onPointerCancel: stopRepeatingPress,
    onPointerLeave: stopRepeatingPress,
    onClick: () => {
      if (ignoreNextClickRef.current) {
        ignoreNextClickRef.current = false;
        return;
      }

      action();
    }
  });

  const queuePointerTap = (x: number, y: number, time: number) => {
    const previousTap = pendingTapRef.current;
    if (previousTap && time - previousTap.time <= doubleTapMs && Math.hypot(x - previousTap.x, y - previousTap.y) <= doubleTapDistance) {
      clearPendingTap();
      onPointerButtonClick("right");
      return;
    }

    clearPendingTap();
    pendingTapRef.current = {
      x,
      y,
      time,
      timeout: window.setTimeout(() => {
        pendingTapRef.current = null;
        onPointerButtonClick("left");
      }, doubleTapMs)
    };
  };

  const startPointer = (event: PointerEvent<HTMLDivElement>): PointerState => ({
    id: event.pointerId,
    startX: event.clientX,
    startY: event.clientY,
    lastX: event.clientX,
    lastY: event.clientY,
    maxDistance: 0
  });

  const movePointer = (event: PointerEvent<HTMLDivElement>, pointer: PointerState) => {
    event.preventDefault();
    const dx = event.clientX - pointer.lastX;
    const dy = event.clientY - pointer.lastY;
    pointer.lastX = event.clientX;
    pointer.lastY = event.clientY;
    pointer.maxDistance = Math.max(pointer.maxDistance, Math.hypot(event.clientX - pointer.startX, event.clientY - pointer.startY));
    if (Math.abs(dx) >= 0.01 || Math.abs(dy) >= 0.01) {
      onPointerMove(roundRemoteDelta(dx * trackpadScale), roundRemoteDelta(dy * trackpadScale));
    }
  };

  const releaseCapture = (event: PointerEvent<HTMLDivElement>) => {
    if (event.currentTarget.hasPointerCapture?.(event.pointerId)) {
      event.currentTarget.releasePointerCapture?.(event.pointerId);
    }
  };

  const miniTrackpadProps: HTMLAttributes<HTMLDivElement> = {
    onClick: (event) => { event.preventDefault(); },
    onKeyDown: (event: KeyboardEvent<HTMLDivElement>) => {
      if (event.key !== "Enter" && event.key !== " ") {
        return;
      }

      event.preventDefault();
      if (isKodiMode) {
        clearPendingTap();
        sendSpecial("Enter");
      } else {
        onPointerButtonClick("left");
      }
    },
    onPointerDown: (event) => {
      if (event.button !== 0) {
        return;
      }

      event.preventDefault();
      event.stopPropagation();
      event.currentTarget.setPointerCapture?.(event.pointerId);
      miniTrackpadPointerRef.current = startPointer(event);
    },
    onPointerMove: (event) => {
      const pointer = miniTrackpadPointerRef.current;
      if (pointer?.id === event.pointerId) {
        event.stopPropagation();
        movePointer(event, pointer);
      }
    },
    onPointerUp: (event) => {
      const pointer = miniTrackpadPointerRef.current;
      if (pointer?.id !== event.pointerId) {
        return;
      }

      event.preventDefault();
      event.stopPropagation();
      miniTrackpadPointerRef.current = null;
      releaseCapture(event);
      if (pointer.maxDistance <= tapDistance) {
        if (isKodiMode) {
          clearPendingTap();
          sendSpecial("Enter");
        } else {
          queuePointerTap(event.clientX, event.clientY, event.timeStamp);
        }
      }
    },
    onPointerCancel: (event) => {
      if (miniTrackpadPointerRef.current?.id === event.pointerId) {
        event.stopPropagation();
        miniTrackpadPointerRef.current = null;
      }
    },
    onPointerLeave: (event) => {
      if (miniTrackpadPointerRef.current?.id === event.pointerId) {
        event.stopPropagation();
        miniTrackpadPointerRef.current = null;
      }
    }
  };

  const navigationPanelProps: HTMLAttributes<HTMLDivElement> = {
    onPointerDown: (event) => {
      if (!navigationRing || event.button !== 0 || isInteractiveRemoteTarget(event.target)) {
        return;
      }

      event.preventDefault();
      event.currentTarget.setPointerCapture?.(event.pointerId);
      navigationPanelPointerRef.current = startPointer(event);
    },
    onPointerMove: (event) => {
      const pointer = navigationPanelPointerRef.current;
      if (pointer?.id === event.pointerId) {
        movePointer(event, pointer);
      }
    },
    onPointerUp: (event) => {
      const pointer = navigationPanelPointerRef.current;
      if (pointer?.id !== event.pointerId) {
        return;
      }

      event.preventDefault();
      navigationPanelPointerRef.current = null;
      releaseCapture(event);
      if (pointer.maxDistance <= tapDistance) {
        queuePointerTap(event.clientX, event.clientY, event.timeStamp);
      }
    },
    onPointerCancel: (event) => {
      if (navigationPanelPointerRef.current?.id === event.pointerId) {
        navigationPanelPointerRef.current = null;
      }
    },
    onPointerLeave: (event) => {
      if (navigationPanelPointerRef.current?.id === event.pointerId) {
        navigationPanelPointerRef.current = null;
      }
    }
  };

  return { getRepeatablePressProps, miniTrackpadProps, navigationPanelProps };
}
