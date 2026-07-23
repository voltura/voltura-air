import { act, renderHook } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { maximumPresentationBreaks, usePresentationTimer } from "./presentationTimer";

describe("usePresentationTimer", () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date("2026-07-23T08:00:00.000Z"));
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("accumulates revisited slide time and keeps the current session slide range", () => {
    const { result } = renderHook(() => usePresentationTimer());

    act(() => { result.current.startSlideshow("powerpoint"); });
    act(() => { vi.advanceTimersByTime(10_000); });
    act(() => { result.current.changeSlide("next", "powerpoint"); });
    act(() => { vi.advanceTimersByTime(5_000); });
    act(() => { result.current.changeSlide("previous", "powerpoint"); });
    act(() => { vi.advanceTimersByTime(2_000); });

    expect(result.current.currentSlideNumber).toBe(1);
    expect(result.current.currentSessionSlideMinimum).toBe(1);
    expect(result.current.currentSessionSlideMaximum).toBe(2);
    expect(result.current.slides).toEqual([
      { slideNumber: 1, elapsedSeconds: 12 },
      { slideNumber: 2, elapsedSeconds: 5 }
    ]);
  });

  it("starts a slide-two session when Next is the first presenter action", () => {
    const { result } = renderHook(() => usePresentationTimer());

    act(() => { result.current.changeSlide("next", "google-slides"); });

    expect(result.current.isRunning).toBe(true);
    expect(result.current.sessionTarget).toBe("google-slides");
    expect(result.current.currentSlideNumber).toBe(2);
    expect(result.current.slides).toEqual([
      { slideNumber: 1, elapsedSeconds: null },
      { slideNumber: 2, elapsedSeconds: 0 }
    ]);
  });

  it("records live and final breaks while counting presenting sessions", () => {
    const { result } = renderHook(() => usePresentationTimer());

    act(() => { result.current.start("pdf"); });
    act(() => { vi.advanceTimersByTime(10_000); });
    act(() => { result.current.pause(); });
    act(() => { vi.advanceTimersByTime(4_000); });

    expect(result.current.breaks).toHaveLength(1);
    expect(result.current.breaks[0]?.presentationElapsedSeconds).toBe(10);
    expect(result.current.breaks[0]?.elapsedSeconds).toBe(4);
    expect(result.current.presentationSessionCount).toBe(1);

    act(() => { result.current.start("pdf"); });
    act(() => { vi.advanceTimersByTime(6_000); });

    expect(result.current.breaks[0]?.elapsedSeconds).toBe(4);
    expect(result.current.breaks[0]?.endedAt).not.toBeNull();
    expect(result.current.elapsedSeconds).toBe(16);
    expect(result.current.presentationSessionCount).toBe(2);
  });

  it("does not count frozen confirmation time when reset is cancelled", () => {
    const { result } = renderHook(() => usePresentationTimer());

    act(() => { result.current.start("powerpoint"); });
    act(() => { vi.advanceTimersByTime(10_000); });
    act(() => { result.current.requestReset(); });
    act(() => { vi.advanceTimersByTime(60_000); });
    act(() => { result.current.cancelReset(); });
    act(() => { vi.advanceTimersByTime(5_000); });

    expect(result.current.isRunning).toBe(true);
    expect(result.current.isResetPending).toBe(false);
    expect(result.current.elapsedSeconds).toBe(15);
  });

  it("prevents another break after the bounded break limit", () => {
    const { result } = renderHook(() => usePresentationTimer());

    act(() => { result.current.start("powerpoint"); });
    for (let index = 0; index < maximumPresentationBreaks; index += 1) {
      act(() => { result.current.pause(); });
      if (index < maximumPresentationBreaks - 1) {
        act(() => { result.current.start("powerpoint"); });
      }
    }

    expect(result.current.breaks).toHaveLength(maximumPresentationBreaks);
    expect(result.current.canPause).toBe(false);
    act(() => { result.current.pause(); });
    expect(result.current.breaks).toHaveLength(maximumPresentationBreaks);
  });

  it("clears every report and slide field on reset", () => {
    const { result } = renderHook(() => usePresentationTimer());

    act(() => { result.current.startSlideshow("powerpoint"); });
    act(() => { vi.advanceTimersByTime(1_000); });
    act(() => { result.current.reset(); });

    expect(result.current.sessionStartedAt).toBeNull();
    expect(result.current.sessionReportId).toBeNull();
    expect(result.current.sessionTarget).toBeNull();
    expect(result.current.currentSlideNumber).toBeNull();
    expect(result.current.currentSessionSlideMinimum).toBeNull();
    expect(result.current.currentSessionSlideMaximum).toBeNull();
    expect(result.current.slides).toEqual([]);
    expect(result.current.breaks).toEqual([]);
    expect(result.current.elapsedSeconds).toBe(0);
  });
});
