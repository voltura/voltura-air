import { useCallback, useEffect, useRef, useState } from "react";
import type { PresentationTarget } from "../../../foundation/protocol/messages";
import { createLocalId } from "../../../foundation/identity/localId";

const timerRefreshMs = 250;
const fiveMinutesSeconds = 5 * 60;
const acceleratedTimerMultiplier = 10;
export const maximumPresentationBreaks = 100;

export interface PresentationBreak {
  breakNumber: number;
  presentationElapsedSeconds: number;
  sessionSlideMinimum: number | null;
  sessionSlideMaximum: number | null;
  slideNumberAtStart: number | null;
  slideNumberAtEnd: number | null;
  startedAt: string;
  endedAt: string | null;
  elapsedSeconds: number;
}

export interface PresentationSlide {
  slideNumber: number;
  elapsedSeconds: number | null;
}

interface ResetResumeState {
  mode: "presentation" | "break";
}

export type PresentationCompletionIntent = "end" | "reset";

export function formatPresentationTime(totalSeconds: number): string {
  const normalized = Math.max(0, Math.floor(totalSeconds));
  const hours = Math.floor(normalized / 3600);
  const minutes = Math.floor((normalized % 3600) / 60);
  const seconds = normalized % 60;
  return hours > 0
    ? `${hours.toString().padStart(2, "0")}:${minutes.toString().padStart(2, "0")}:${seconds.toString().padStart(2, "0")}`
    : `${minutes.toString().padStart(2, "0")}:${seconds.toString().padStart(2, "0")}`;
}

function readTimerSpeedMultiplier(): number {
  return import.meta.env.VITE_PRESENTATION_TIMER_SPEED ===
    acceleratedTimerMultiplier.toString()
    ? acceleratedTimerMultiplier
    : 1;
}

export function usePresentationTimer() {
  const speedMultiplier = readTimerSpeedMultiplier();
  const [elapsedSeconds, setElapsedSeconds] = useState(0);
  const [isRunning, setIsRunning] = useState(false);
  const [isPaused, setIsPaused] = useState(false);
  const [breakElapsedSeconds, setBreakElapsedSeconds] = useState(0);
  const [breaks, setBreaks] = useState<PresentationBreak[]>([]);
  const [durationMinutes, setDurationMinutes] = useState(15);
  const [vibrationEnabled, setVibrationEnabled] = useState(false);
  const [milestoneMessage, setMilestoneMessage] = useState("");
  const [sessionStartedAt, setSessionStartedAt] = useState<string | null>(null);
  const [sessionTarget, setSessionTarget] = useState<PresentationTarget | null>(null);
  const [sessionReportId, setSessionReportId] = useState<string | null>(null);
  const [completionEndedAt, setCompletionEndedAt] = useState<string | null>(null);
  const [isResetPending, setIsResetPending] = useState(false);
  const [completionIntent, setCompletionIntent] = useState<PresentationCompletionIntent | null>(null);
  const [currentSlideNumber, setCurrentSlideNumber] = useState<number | null>(null);
  const [currentSessionSlideRange, setCurrentSessionSlideRange] = useState<{
    maximum: number | null;
    minimum: number | null;
  }>({ maximum: null, minimum: null });
  const [slides, setSlides] = useState<PresentationSlide[]>([]);
  const startedAtRef = useRef<number | null>(null);
  const breakStartedAtRef = useRef<number | null>(null);
  const activeSlideStartedAtRef = useRef<number | null>(null);
  const currentSlideNumberRef = useRef<number | null>(null);
  const sessionSlideMinimumRef = useRef<number | null>(null);
  const sessionSlideMaximumRef = useRef<number | null>(null);
  const slideshowStartedRef = useRef(false);
  const slideSecondsRef = useRef(new Map<number, number | null>());
  const accumulatedSecondsRef = useRef(0);
  const breakAccumulatedSecondsRef = useRef(0);
  const previousElapsedRef = useRef(0);
  const fiveMinuteMilestoneRef = useRef(false);
  const elapsedMilestoneRef = useRef(false);
  const resetResumeStateRef = useRef<ResetResumeState | null>(null);
  const supportsVibration = typeof navigator.vibrate === "function";

  useEffect(() => {
    if (!isRunning || isResetPending || startedAtRef.current === null) {
      return;
    }

    const updateElapsed = () => {
      const now = Date.now();
      const next = accumulatedSecondsRef.current +
        ((now - startedAtRef.current!) / 1000) * speedMultiplier;
      setElapsedSeconds(next);
      const activeSlide = currentSlideNumberRef.current;
      const activeSlideStartedAt = activeSlideStartedAtRef.current;
      if (activeSlide !== null && activeSlideStartedAt !== null) {
        const activeSlideSeconds =
          (slideSecondsRef.current.get(activeSlide) ?? 0) +
          ((now - activeSlideStartedAt) / 1000) * speedMultiplier;
        setSlides((current) => current.map((slide) => slide.slideNumber === activeSlide
          ? { ...slide, elapsedSeconds: activeSlideSeconds }
          : slide));
      }
    };
    updateElapsed();
    const interval = window.setInterval(updateElapsed, timerRefreshMs);
    return () => { window.clearInterval(interval); };
  }, [isResetPending, isRunning, speedMultiplier]);

  useEffect(() => {
    if (!isPaused || isResetPending || breakStartedAtRef.current === null) {
      return;
    }

    const updateBreakElapsed = () => {
      setBreakElapsedSeconds(
        breakAccumulatedSecondsRef.current +
        ((Date.now() - breakStartedAtRef.current!) / 1000) * speedMultiplier
      );
    };
    updateBreakElapsed();
    const interval = window.setInterval(updateBreakElapsed, timerRefreshMs);
    return () => { window.clearInterval(interval); };
  }, [isPaused, isResetPending, speedMultiplier]);

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

  const readPresentationElapsed = (now: number) => accumulatedSecondsRef.current +
    (startedAtRef.current === null ? 0 : ((now - startedAtRef.current) / 1000) * speedMultiplier);

  const readBreakElapsed = (now: number) => breakAccumulatedSecondsRef.current +
    (breakStartedAtRef.current === null
      ? 0
      : ((now - breakStartedAtRef.current) / 1000) * speedMultiplier);

  const publishSlides = () => {
    setSlides([...slideSecondsRef.current.entries()]
      .sort(([left], [right]) => left - right)
      .map(([slideNumber, slideSeconds]) => ({ slideNumber, elapsedSeconds: slideSeconds })));
  };

  const closeActiveSlide = (now: number) => {
    const slideNumber = currentSlideNumberRef.current;
    const slideStartedAt = activeSlideStartedAtRef.current;
    if (slideNumber === null || slideStartedAt === null) {
      return;
    }

    slideSecondsRef.current.set(
      slideNumber,
      (slideSecondsRef.current.get(slideNumber) ?? 0) +
      ((now - slideStartedAt) / 1000) * speedMultiplier
    );
    activeSlideStartedAtRef.current = null;
    publishSlides();
  };

  const startAt = (target: PresentationTarget, now: number) => {
    if (sessionStartedAt === null) {
      setSessionStartedAt(new Date(now).toISOString());
      setSessionTarget(target);
      setSessionReportId(createLocalId());
    }

    if (isPaused) {
      const finalBreakElapsed = readBreakElapsed(now);
      setBreakElapsedSeconds(finalBreakElapsed);
      setBreaks((current) => current.map((entry, index) => index === current.length - 1
        ? {
            ...entry,
            elapsedSeconds: finalBreakElapsed,
            endedAt: new Date(now).toISOString(),
            slideNumberAtEnd: currentSlideNumberRef.current
          }
        : entry));
    }

    breakStartedAtRef.current = null;
    breakAccumulatedSecondsRef.current = 0;
    startedAtRef.current = now;
    setIsRunning(true);
    setIsPaused(false);

    if (currentSlideNumberRef.current !== null && activeSlideStartedAtRef.current === null) {
      activeSlideStartedAtRef.current = now;
      sessionSlideMinimumRef.current = currentSlideNumberRef.current;
      sessionSlideMaximumRef.current = currentSlideNumberRef.current;
      setCurrentSessionSlideRange({
        maximum: currentSlideNumberRef.current,
        minimum: currentSlideNumberRef.current
      });
    }
  };

  const start = (target: PresentationTarget) => {
    if (isRunning || isResetPending) {
      return;
    }

    startAt(target, Date.now());
  };

  const startSlideshow = (target: PresentationTarget) => {
    if (slideshowStartedRef.current || isResetPending) {
      return;
    }

    const now = Date.now();
    if (!isRunning) {
      startAt(target, now);
    }

    slideshowStartedRef.current = true;
    currentSlideNumberRef.current = 1;
    slideSecondsRef.current.set(1, slideSecondsRef.current.get(1) ?? 0);
    activeSlideStartedAtRef.current = now;
    sessionSlideMinimumRef.current = 1;
    sessionSlideMaximumRef.current = 1;
    setCurrentSessionSlideRange({ maximum: 1, minimum: 1 });
    setCurrentSlideNumber(1);
    publishSlides();
  };

  const changeSlide = (
    direction: "next" | "previous",
    target: PresentationTarget
  ) => {
    if (!slideshowStartedRef.current && direction === "next" && !isResetPending) {
      const now = Date.now();
      if (!isRunning && !isPaused) {
        startAt(target, now);
      }

      slideshowStartedRef.current = true;
      currentSlideNumberRef.current = 2;
      slideSecondsRef.current.set(1, null);
      slideSecondsRef.current.set(2, 0);
      activeSlideStartedAtRef.current = isPaused ? null : now;
      sessionSlideMinimumRef.current = isPaused ? null : 2;
      sessionSlideMaximumRef.current = isPaused ? null : 2;
      setCurrentSessionSlideRange(isPaused
        ? { maximum: null, minimum: null }
        : { maximum: 2, minimum: 2 });
      setCurrentSlideNumber(2);
      publishSlides();
      return;
    }

    const currentSlide = currentSlideNumberRef.current;
    if (!slideshowStartedRef.current || currentSlide === null || isResetPending) {
      return;
    }

    const nextSlide = direction === "next" ? currentSlide + 1 : Math.max(1, currentSlide - 1);
    if (nextSlide === currentSlide) {
      return;
    }

    const now = Date.now();
    closeActiveSlide(now);
    currentSlideNumberRef.current = nextSlide;
    if (isRunning) {
      sessionSlideMinimumRef.current = Math.min(sessionSlideMinimumRef.current ?? nextSlide, nextSlide);
      sessionSlideMaximumRef.current = Math.max(sessionSlideMaximumRef.current ?? nextSlide, nextSlide);
      setCurrentSessionSlideRange({
        maximum: sessionSlideMaximumRef.current,
        minimum: sessionSlideMinimumRef.current
      });
    }
    slideSecondsRef.current.set(nextSlide, slideSecondsRef.current.get(nextSlide) ?? 0);
    activeSlideStartedAtRef.current = isRunning ? now : null;
    setCurrentSlideNumber(nextSlide);
    publishSlides();
  };

  const pause = () => {
    if (!isRunning || startedAtRef.current === null || breaks.length >= maximumPresentationBreaks) {
      return;
    }

    const now = Date.now();
    closeActiveSlide(now);
    const presentationElapsed = readPresentationElapsed(now);
    accumulatedSecondsRef.current = presentationElapsed;
    startedAtRef.current = null;
    breakStartedAtRef.current = now;
    breakAccumulatedSecondsRef.current = 0;
    setElapsedSeconds(presentationElapsed);
    setBreakElapsedSeconds(0);
    setBreaks((current) => [
      ...current,
      {
        breakNumber: current.length + 1,
        presentationElapsedSeconds: presentationElapsed,
        sessionSlideMinimum: sessionSlideMinimumRef.current,
        sessionSlideMaximum: sessionSlideMaximumRef.current,
        slideNumberAtStart: currentSlideNumberRef.current,
        slideNumberAtEnd: null,
        startedAt: new Date(now).toISOString(),
        endedAt: null,
        elapsedSeconds: 0
      }
    ]);
    setIsRunning(false);
    setIsPaused(true);
  };

  const requestCompletion = (intent: PresentationCompletionIntent) => {
    if (isResetPending || sessionStartedAt === null) {
      return;
    }

    const now = Date.now();
    if (isRunning) {
      closeActiveSlide(now);
      const presentationElapsed = readPresentationElapsed(now);
      accumulatedSecondsRef.current = presentationElapsed;
      startedAtRef.current = null;
      setElapsedSeconds(presentationElapsed);
      resetResumeStateRef.current = { mode: "presentation" };
    } else if (isPaused) {
      const currentBreakElapsed = readBreakElapsed(now);
      breakAccumulatedSecondsRef.current = currentBreakElapsed;
      breakStartedAtRef.current = null;
      setBreakElapsedSeconds(currentBreakElapsed);
      resetResumeStateRef.current = { mode: "break" };
    } else {
      resetResumeStateRef.current = { mode: "presentation" };
    }
    setIsRunning(false);
    setCompletionEndedAt(new Date(now).toISOString());
    setCompletionIntent(intent);
    setIsResetPending(true);
  };
  const requestEnd = () => { requestCompletion("end"); };
  const requestReset = () => { requestCompletion("reset"); };

  const cancelReset = () => {
    const resumeState = resetResumeStateRef.current;
    if (!isResetPending || resumeState === null) {
      return;
    }

    const now = Date.now();
    setIsResetPending(false);
    setCompletionEndedAt(null);
    setCompletionIntent(null);
    if (resumeState.mode === "break") {
      breakStartedAtRef.current = now;
      setIsPaused(true);
    } else {
      startedAtRef.current = now;
      setIsPaused(false);
      setIsRunning(true);
      if (currentSlideNumberRef.current !== null) {
        activeSlideStartedAtRef.current = now;
      }
    }
    resetResumeStateRef.current = null;
  };

  const reset = useCallback(() => {
    startedAtRef.current = null;
    breakStartedAtRef.current = null;
    accumulatedSecondsRef.current = 0;
    breakAccumulatedSecondsRef.current = 0;
    previousElapsedRef.current = 0;
    fiveMinuteMilestoneRef.current = false;
    elapsedMilestoneRef.current = false;
    resetResumeStateRef.current = null;
    activeSlideStartedAtRef.current = null;
    currentSlideNumberRef.current = null;
    sessionSlideMinimumRef.current = null;
    sessionSlideMaximumRef.current = null;
    slideshowStartedRef.current = false;
    slideSecondsRef.current.clear();
    setElapsedSeconds(0);
    setBreakElapsedSeconds(0);
    setBreaks([]);
    setIsRunning(false);
    setIsPaused(false);
    setIsResetPending(false);
    setCompletionIntent(null);
    setMilestoneMessage("");
    setSessionStartedAt(null);
    setSessionTarget(null);
    setSessionReportId(null);
    setCompletionEndedAt(null);
    setCurrentSlideNumber(null);
    setCurrentSessionSlideRange({ maximum: null, minimum: null });
    setSlides([]);
  }, []);

  const changeDuration = (minutes: number) => {
    setDurationMinutes(minutes);
    fiveMinuteMilestoneRef.current = false;
    elapsedMilestoneRef.current = false;
    previousElapsedRef.current = elapsedSeconds;
    setMilestoneMessage("");
  };

  const displayedBreaks = breaks.map((entry, index) => (
    index === breaks.length - 1 && entry.endedAt === null
      ? { ...entry, elapsedSeconds: breakElapsedSeconds }
      : entry
  ));
  const totalBreakSeconds = displayedBreaks.reduce((total, entry) => total + entry.elapsedSeconds, 0);
  const presentationSessionCount = sessionStartedAt === null
    ? 0
    : 1 + displayedBreaks.filter((entry) => entry.endedAt !== null).length;
  return {
    breakElapsedSeconds,
    breaks: displayedBreaks,
    canPause: breaks.length < maximumPresentationBreaks,
    cancelReset,
    changeSlide,
    changeDuration,
    completionIntent,
    completionEndedAt,
    currentSlideNumber,
    durationMinutes,
    elapsedSeconds,
    isResetPending,
    isRunning,
    isPaused,
    milestoneMessage,
    pause,
    presentationSessionCount,
    requestEnd,
    currentSessionSlideMaximum: currentSessionSlideRange.maximum,
    currentSessionSlideMinimum: currentSessionSlideRange.minimum,
    requestReset,
    reset,
    sessionStartedAt,
    sessionReportId,
    sessionTarget,
    slides,
    start,
    startSlideshow,
    supportsVibration,
    speedMultiplier,
    totalBreakSeconds,
    vibrationEnabled,
    setVibrationEnabled
  };
}
