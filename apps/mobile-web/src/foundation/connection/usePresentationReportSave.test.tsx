import { act, renderHook } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { PresentationReportSavePayload } from "../protocol/messages";
import type { ConnectionState } from "./connectionTypes";
import { usePresentationReportSave } from "./usePresentationReportSave";

const payload: PresentationReportSavePayload = {
  reportId: "report-1",
  target: "powerpoint",
  startedAt: "2026-07-23T08:00:00.000Z",
  endedAt: "2026-07-23T08:03:00.000Z",
  utcOffsetMinutes: 0,
  plannedDurationSeconds: 180,
  presentationDurationSeconds: 120,
  endedDuringBreak: false,
  breaks: [
    {
      breakNumber: 1,
      presentationElapsedSeconds: 60,
      breakDurationSeconds: 60,
      startedAt: "2026-07-23T08:01:00.000Z",
      endedAt: "2026-07-23T08:02:00.000Z",
      sessionSlideMinimum: 1,
      sessionSlideMaximum: 2,
      slideNumberAtStart: 2,
      slideNumberAtEnd: 2
    }
  ],
  slides: [
    { slideNumber: 1, durationSeconds: 60 },
    { slideNumber: 2, durationSeconds: 60 }
  ]
};

describe("usePresentationReportSave", () => {
  afterEach(() => {
    vi.useRealTimers();
  });

  it("sends one correlated save and accepts only its matching result", () => {
    const send = vi.fn();
    const { result } = renderHook(() => usePresentationReportSave("paired", send));

    let operationId: string | null = null;
    act(() => { operationId = result.current.requestPresentationReportSave(payload); });

    expect(operationId).not.toBeNull();
    expect(send).toHaveBeenCalledExactlyOnceWith({
      type: "presentation.report.save",
      operationId,
      ...payload
    });
    act(() => {
      expect(result.current.completePresentationReportSave({
        type: "presentation.report.save.result",
        operationId: "unrelated",
        reportId: payload.reportId,
        succeeded: true,
        message: "Saved."
      })).toBe(false);
    });
    expect(result.current.pendingPresentationReportSave).not.toBeNull();

    act(() => {
      expect(result.current.completePresentationReportSave({
        type: "presentation.report.save.result",
        operationId: operationId!,
        reportId: payload.reportId,
        succeeded: true,
        message: "Saved."
      })).toBe(true);
    });
    expect(result.current.pendingPresentationReportSave).toBeNull();
    expect(result.current.presentationReportSaveResult?.succeeded).toBe(true);
  });

  it("retains the operation identity when retrying the same frozen report", () => {
    vi.useFakeTimers();
    const send = vi.fn();
    const { result } = renderHook(() => usePresentationReportSave("paired", send));

    let firstOperationId: string | null = null;
    act(() => { firstOperationId = result.current.requestPresentationReportSave(payload); });
    act(() => { vi.advanceTimersByTime(8_000); });
    expect(result.current.presentationReportSaveResult?.code).toBe("VAIR-PRESENTATION-SAVE-TIMEOUT");

    let retryOperationId: string | null = null;
    act(() => { retryOperationId = result.current.requestPresentationReportSave(payload); });

    expect(retryOperationId).toBe(firstOperationId);
    expect(send).toHaveBeenCalledTimes(2);
  });

  it("freezes the pending identity for retry when the connection is lost", () => {
    const send = vi.fn();
    const { result, rerender } = renderHook(
      ({ state }) => usePresentationReportSave(state, send),
      { initialProps: { state: "paired" as ConnectionState } }
    );

    let operationId: string | null = null;
    act(() => { operationId = result.current.requestPresentationReportSave(payload); });
    rerender({ state: "disconnected" });

    expect(result.current.pendingPresentationReportSave).toBeNull();
    expect(result.current.presentationReportSaveResult?.code).toBe("VAIR-PRESENTATION-SAVE-DISCONNECTED");

    rerender({ state: "paired" });
    let retryOperationId: string | null = null;
    act(() => { retryOperationId = result.current.requestPresentationReportSave(payload); });
    expect(retryOperationId).toBe(operationId);
  });

  it("does not send while disconnected or while another save is pending", () => {
    const send = vi.fn();
    const disconnected = renderHook(() => usePresentationReportSave("disconnected", send));
    act(() => {
      expect(disconnected.result.current.requestPresentationReportSave(payload)).toBeNull();
    });

    const paired = renderHook(() => usePresentationReportSave("paired", send));
    act(() => { paired.result.current.requestPresentationReportSave(payload); });
    act(() => {
      expect(paired.result.current.requestPresentationReportSave({
        ...payload,
        reportId: "report-2"
      })).toBeNull();
    });
    expect(send).toHaveBeenCalledOnce();
  });
});
