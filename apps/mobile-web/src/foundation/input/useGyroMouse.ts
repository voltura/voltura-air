import { useCallback, useEffect, useEffectEvent, useRef, useState } from "react";
import {
  getGyroInitialAvailability,
  GyroMotionProcessor,
  requestGyroPermission,
  type GyroActivationRequest,
  type GyroAvailability,
} from "./gyroMouse";

interface UseGyroMouseOptions {
  activationRequest?: GyroActivationRequest | null;
  connected: boolean;
  enabledSurface: boolean;
  onMove: (dx: number, dy: number) => void;
  onSelectedChange: (selected: boolean) => void;
  onStop: () => void;
  sessionKey: number;
  sensitivity: number;
}

export function useGyroMouse({
  activationRequest,
  connected,
  enabledSurface,
  onMove,
  onSelectedChange,
  onStop,
  sensitivity,
  sessionKey,
}: UseGyroMouseOptions) {
  const [selected, setSelectedState] = useState(false);
  const [availability, setAvailability] = useState<GyroAvailability>(getGyroInitialAvailability);
  const [engaged, setEngagedState] = useState(false);
  const [listenerGeneration, setListenerGeneration] = useState(0);
  const processorRef = useRef(new GyroMotionProcessor());
  const engagedRef = useRef(false);
  const lastValidMotionAtRef = useRef(Number.NEGATIVE_INFINITY);
  const dataTimerRef = useRef<number | null>(null);
  const wakeLockRef = useRef<{ release: () => Promise<void> } | null>(null);
  const lastActivationIdRef = useRef(0);
  const permissionAttemptRef = useRef(0);
  const onSelectedChangeRef = useRef(onSelectedChange);
  const onStopRef = useRef(onStop);
  const emitMove = useEffectEvent(onMove);

  useEffect(() => {
    onSelectedChangeRef.current = onSelectedChange;
    onStopRef.current = onStop;
  }, [onSelectedChange, onStop]);

  const setSelected = useCallback((next: boolean) => {
    setSelectedState(next);
    onSelectedChangeRef.current(next);
    if (!next) {
      permissionAttemptRef.current += 1;
      engagedRef.current = false;
      setEngagedState(false);
      processorRef.current.resetMapping();
      onStopRef.current();
    }
  }, []);

  const finishPermission = useCallback(
    async (permission: Promise<boolean>) => {
      const attempt = ++permissionAttemptRef.current;
      const granted = await permission;
      if (attempt !== permissionAttemptRef.current) {
        return;
      }
      if (!granted) {
        const initial = getGyroInitialAvailability();
        setAvailability(initial === "ready" ? "denied" : initial);
        setSelected(true);
        return;
      }
      processorRef.current.resetAll();
      setAvailability("ready");
      setListenerGeneration((current) => current + 1);
      setSelected(true);
    },
    [setSelected],
  );

  const enableFromUserGesture = useCallback(() => {
    void finishPermission(requestGyroPermission());
  }, [finishPermission]);

  useEffect(() => {
    if (!activationRequest || activationRequest.id === lastActivationIdRef.current) {
      return;
    }
    lastActivationIdRef.current = activationRequest.id;
    void finishPermission(activationRequest.permission);
  }, [activationRequest, finishPermission]);

  useEffect(() => {
    if (!selected || !connected || !enabledSurface) {
      return;
    }
    lastValidMotionAtRef.current = Number.NEGATIVE_INFINITY;
    processorRef.current.resetMapping();
    const processor = processorRef.current;
    const armNoDataTimer = () => {
      if (dataTimerRef.current !== null) {
        window.clearTimeout(dataTimerRef.current);
      }
      dataTimerRef.current = window.setTimeout(() => {
        setAvailability("no-data");
      }, 1800);
    };
    const markValid = () => {
      setAvailability("ready");
      armNoDataTimer();
    };
    armNoDataTimer();
    const onMotion = (event: DeviceMotionEvent) => {
      const delta = processor.motion(event, 0, sensitivity / 100, engagedRef.current);
      if (!delta) {
        return;
      }
      lastValidMotionAtRef.current = performance.now();
      markValid();
      if (engagedRef.current && (delta.dx !== 0 || delta.dy !== 0)) {
        emitMove(delta.dx, delta.dy);
      }
    };
    const onOrientation = (event: DeviceOrientationEvent) => {
      const motionIsRecent = performance.now() - lastValidMotionAtRef.current < 250;
      const delta = processor.orientation(
        event,
        0,
        sensitivity / 100,
        engagedRef.current,
        !motionIsRecent,
      );
      if (!delta) {
        return;
      }
      if (motionIsRecent) {
        return;
      }
      markValid();
      if (engagedRef.current && (delta.dx !== 0 || delta.dy !== 0)) {
        emitMove(delta.dx, delta.dy);
      }
    };
    const reset = () => {
      processor.resetMapping();
    };
    window.addEventListener("devicemotion", onMotion);
    window.addEventListener("deviceorientation", onOrientation);
    window.addEventListener("orientationchange", reset);
    screen.orientation?.addEventListener("change", reset);
    return () => {
      window.removeEventListener("devicemotion", onMotion);
      window.removeEventListener("deviceorientation", onOrientation);
      window.removeEventListener("orientationchange", reset);
      screen.orientation?.removeEventListener("change", reset);
      if (dataTimerRef.current !== null) {
        window.clearTimeout(dataTimerRef.current);
      }
      dataTimerRef.current = null;
      processor.resetMapping();
    };
  }, [connected, enabledSurface, listenerGeneration, selected, sensitivity]);

  useEffect(() => {
    if (!selected || !connected || !enabledSurface || document.visibilityState !== "visible") {
      return;
    }
    const wakeLock = navigator.wakeLock;
    if (!wakeLock) {
      return;
    }
    let cancelled = false;
    void wakeLock
      .request("screen")
      .then((lock) => {
        if (cancelled) {
          void lock.release();
        } else {
          wakeLockRef.current = lock;
        }
      })
      .catch(() => {
        // Wake lock is a best-effort enhancement.
      });
    return () => {
      cancelled = true;
      const lock = wakeLockRef.current;
      wakeLockRef.current = null;
      if (lock) {
        void lock.release().catch(() => {
          // The browser may already have released it.
        });
      }
    };
  }, [connected, enabledSurface, selected]);

  useEffect(() => {
    const onVisibility = () => {
      if (document.visibilityState === "hidden") {
        stop();
      }
    };
    const stop = () => {
      setSelected(false);
    };
    document.addEventListener("visibilitychange", onVisibility);
    return () => {
      document.removeEventListener("visibilitychange", onVisibility);
    };
  }, [setSelected]);

  useEffect(() => {
    if (!connected || !enabledSurface) {
      return;
    }
    return () => {
      setSelected(false);
    };
  }, [connected, enabledSurface, sessionKey, setSelected]);

  const setEngaged = useCallback((next: boolean) => {
    if (engagedRef.current === next) {
      return;
    }
    engagedRef.current = next;
    setEngagedState(next);
    processorRef.current.reset();
  }, []);

  return { availability, enableFromUserGesture, engaged, selected, setEngaged, setSelected };
}
