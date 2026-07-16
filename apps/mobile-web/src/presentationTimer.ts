import { useEffect, useRef, useState } from "react";

const timerRefreshMs = 250;
const fiveMinutesSeconds = 5 * 60;

export function formatPresentationTime(totalSeconds: number): string {
  const normalized = Math.max(0, Math.floor(totalSeconds));
  const hours = Math.floor(normalized / 3600);
  const minutes = Math.floor((normalized % 3600) / 60);
  const seconds = normalized % 60;
  return hours > 0
    ? `${hours.toString().padStart(2, "0")}:${minutes.toString().padStart(2, "0")}:${seconds.toString().padStart(2, "0")}`
    : `${minutes.toString().padStart(2, "0")}:${seconds.toString().padStart(2, "0")}`;
}

export function usePresentationTimer() {
  const [elapsedSeconds, setElapsedSeconds] = useState(0);
  const [isRunning, setIsRunning] = useState(false);
  const [durationMinutes, setDurationMinutes] = useState(15);
  const [vibrationEnabled, setVibrationEnabled] = useState(false);
  const [milestoneMessage, setMilestoneMessage] = useState("Timer ready.");
  const startedAtRef = useRef<number | null>(null);
  const accumulatedSecondsRef = useRef(0);
  const previousElapsedRef = useRef(0);
  const fiveMinuteMilestoneRef = useRef(false);
  const elapsedMilestoneRef = useRef(false);
  const supportsVibration = typeof navigator.vibrate === "function";

  useEffect(() => {
    if (!isRunning || startedAtRef.current === null) {
      return;
    }

    const updateElapsed = () => {
      const next = accumulatedSecondsRef.current + (Date.now() - startedAtRef.current!) / 1000;
      setElapsedSeconds(next);
    };
    updateElapsed();
    const interval = window.setInterval(updateElapsed, timerRefreshMs);
    return () => window.clearInterval(interval);
  }, [isRunning]);

  useEffect(() => {
    const durationSeconds = durationMinutes * 60;
    const fiveMinuteThreshold = durationSeconds - fiveMinutesSeconds;
    if (!fiveMinuteMilestoneRef.current && previousElapsedRef.current < fiveMinuteThreshold && elapsedSeconds >= fiveMinuteThreshold) {
      fiveMinuteMilestoneRef.current = true;
      setMilestoneMessage("5 minutes remaining.");
      if (vibrationEnabled && supportsVibration) {
        navigator.vibrate([160, 100, 160]);
      }
    }

    if (!elapsedMilestoneRef.current && previousElapsedRef.current < durationSeconds && elapsedSeconds >= durationSeconds) {
      elapsedMilestoneRef.current = true;
      setMilestoneMessage("Planned time elapsed.");
      if (vibrationEnabled && supportsVibration) {
        navigator.vibrate([300, 150, 300]);
      }
    }

    previousElapsedRef.current = elapsedSeconds;
  }, [durationMinutes, elapsedSeconds, supportsVibration, vibrationEnabled]);

  const start = () => {
    if (isRunning) {
      return;
    }

    startedAtRef.current = Date.now();
    setIsRunning(true);
    setMilestoneMessage("Timer running.");
  };

  const pause = () => {
    if (!isRunning || startedAtRef.current === null) {
      return;
    }

    accumulatedSecondsRef.current += (Date.now() - startedAtRef.current) / 1000;
    startedAtRef.current = null;
    setElapsedSeconds(accumulatedSecondsRef.current);
    setIsRunning(false);
    setMilestoneMessage("Timer paused.");
  };

  const reset = () => {
    startedAtRef.current = isRunning ? Date.now() : null;
    accumulatedSecondsRef.current = 0;
    previousElapsedRef.current = 0;
    fiveMinuteMilestoneRef.current = false;
    elapsedMilestoneRef.current = false;
    setElapsedSeconds(0);
    setMilestoneMessage(isRunning ? "Timer restarted." : "Timer ready.");
  };

  const changeDuration = (minutes: number) => {
    setDurationMinutes(minutes);
    fiveMinuteMilestoneRef.current = false;
    elapsedMilestoneRef.current = false;
    previousElapsedRef.current = elapsedSeconds;
    setMilestoneMessage(isRunning ? "Timer running." : elapsedSeconds > 0 ? "Timer paused." : "Timer ready.");
  };

  return {
    changeDuration,
    durationMinutes,
    elapsedSeconds,
    isRunning,
    milestoneMessage,
    pause,
    reset,
    start,
    supportsVibration,
    vibrationEnabled,
    setVibrationEnabled
  };
}
