import { BarChart3, CircleStop, Coffee, Maximize2, Minimize2, Pause, Play, Timer } from "lucide-react";
import { useEffect, useMemo, useRef, useState } from "react";
import { ModalDialog } from "../../../ui/overlays/ModalDialog";
import type { PresentationTarget } from "../../../foundation/protocol/messages";
import {
  formatPresentationTime,
  type PresentationBreak,
  type PresentationSlide
} from "./presentationTimer";

const targetLabels: Record<PresentationTarget, string> = {
  powerpoint: "PowerPoint",
  "google-slides": "Google Slides",
  pdf: "PDF / browser"
};

interface PresentationStatisticsProps {
  breaks: PresentationBreak[];
  canPause: boolean;
  currentSlideNumber: number | null;
  currentSessionSlideMaximum: number | null;
  currentSessionSlideMinimum: number | null;
  elapsedSeconds: number;
  isExpanded: boolean;
  isPaused: boolean;
  isResetPending: boolean;
  isRunning: boolean;
  onExpandedChange: (expanded: boolean) => void;
  onEndPresentation: () => void;
  onPause: () => void;
  onResume: () => void;
  presentationSessionCount: number;
  sessionStartedAt: string | null;
  sessionTarget: PresentationTarget | null;
  slides: PresentationSlide[];
  totalBreakSeconds: number;
}

interface TimelineEntry {
  kind: "presentation" | "break";
  label: string;
  slideLabel?: string | undefined;
  seconds: number;
}

interface PresentationLogRow {
  breakEntry: PresentationBreak;
  presentationSeconds: number;
}

function formatRecordedDate(value: string): string {
  return new Intl.DateTimeFormat(undefined, {
    dateStyle: "medium",
    timeStyle: "medium"
  }).format(new Date(value));
}

function formatSlideDuration(value: number | null): string {
  return value === null ? "Not recorded" : formatPresentationTime(value);
}

function formatSlideSpan(minimum: number | null, maximum: number | null): string | undefined {
  if (minimum === null || maximum === null) {
    return undefined;
  }

  return minimum === maximum ? `Slide ${minimum}` : `Slides ${minimum}–${maximum}`;
}

function buildTimeline(
  breaks: PresentationBreak[],
  elapsedSeconds: number,
  currentSessionSlideMinimum: number | null,
  currentSessionSlideMaximum: number | null
): TimelineEntry[] {
  const entries: TimelineEntry[] = [];
  let previousPresentationElapsed = 0;

  for (const presentationBreak of breaks) {
    entries.push({
      kind: "presentation",
      label: `Session ${presentationBreak.breakNumber}`,
      slideLabel: formatSlideSpan(
        presentationBreak.sessionSlideMinimum,
        presentationBreak.sessionSlideMaximum
      ),
      seconds: Math.max(0, presentationBreak.presentationElapsedSeconds - previousPresentationElapsed)
    });
    entries.push({
      kind: "break",
      label: `Break ${presentationBreak.breakNumber}`,
      seconds: presentationBreak.elapsedSeconds
    });
    previousPresentationElapsed = presentationBreak.presentationElapsedSeconds;
  }

  if (elapsedSeconds > previousPresentationElapsed) {
    entries.push({
      kind: "presentation",
      label: `Session ${breaks.length + 1}`,
      slideLabel: formatSlideSpan(currentSessionSlideMinimum, currentSessionSlideMaximum),
      seconds: elapsedSeconds - previousPresentationElapsed
    });
  }

  return entries;
}

function buildPresentationLogRows(breaks: PresentationBreak[]): PresentationLogRow[] {
  let previousPresentationElapsed = 0;
  return breaks.map((breakEntry) => {
    const row = {
      breakEntry,
      presentationSeconds: Math.max(
        0,
        breakEntry.presentationElapsedSeconds - previousPresentationElapsed
      )
    };
    previousPresentationElapsed = breakEntry.presentationElapsedSeconds;
    return row;
  });
}

function getCurrentSessionSeconds(
  breaks: PresentationBreak[],
  elapsedSeconds: number
): number {
  const latestBreak = breaks.at(-1);
  if (!latestBreak) {
    return elapsedSeconds;
  }

  if (latestBreak.endedAt !== null) {
    return Math.max(0, elapsedSeconds - latestBreak.presentationElapsedSeconds);
  }

  const previousCheckpoint = breaks.at(-2)?.presentationElapsedSeconds ?? 0;
  return Math.max(0, latestBreak.presentationElapsedSeconds - previousCheckpoint);
}

export function PresentationStatistics({
  breaks,
  canPause,
  currentSlideNumber,
  currentSessionSlideMaximum,
  currentSessionSlideMinimum,
  elapsedSeconds,
  isExpanded,
  isPaused,
  isResetPending,
  isRunning,
  onExpandedChange,
  onEndPresentation,
  onPause,
  onResume,
  presentationSessionCount,
  sessionStartedAt,
  sessionTarget,
  slides,
  totalBreakSeconds
}: PresentationStatisticsProps) {
  const [selectedBreakNumber, setSelectedBreakNumber] = useState<number | null>(null);
  const expandButtonRef = useRef<HTMLButtonElement | null>(null);
  const minimizeButtonRef = useRef<HTMLButtonElement | null>(null);
  const selectedBreak = breaks.find((entry) => entry.breakNumber === selectedBreakNumber) ?? null;
  const presentationLogRows = useMemo(() => buildPresentationLogRows(breaks), [breaks]);
  const compactRows = presentationLogRows.slice(-10).reverse();
  const timeline = useMemo(
    () => buildTimeline(
      breaks,
      elapsedSeconds,
      currentSessionSlideMinimum,
      currentSessionSlideMaximum
    ),
    [breaks, currentSessionSlideMaximum, currentSessionSlideMinimum, elapsedSeconds]
  );
  const totalTimelineSeconds = Math.max(
    1,
    timeline.reduce((total, entry) => total + entry.seconds, 0)
  );
  const totalElapsedSeconds = elapsedSeconds + totalBreakSeconds;
  const currentSessionSeconds = getCurrentSessionSeconds(breaks, elapsedSeconds);

  useEffect(() => {
    if (isExpanded) {
      minimizeButtonRef.current?.focus();
    }
  }, [isExpanded]);

  const closeExpanded = () => {
    onExpandedChange(false);
    window.requestAnimationFrame(() => { expandButtonRef.current?.focus(); });
  };

  const currentStatus = isResetPending
    ? "Ready to save or reset"
    : isPaused
      ? "Break in progress"
      : isRunning
        ? "Presentation in progress"
        : "Ready";

  return (
    <>
      {breaks.length === 0 && sessionStartedAt !== null && (
        <button
          ref={expandButtonRef}
          className="presentation-statistics-expand presentation-statistics-empty-expand"
          type="button"
          aria-label="Expand presentation statistics"
          title="Expand presentation statistics"
          onClick={() => { onExpandedChange(true); }}
        >
          <Maximize2 aria-hidden="true" />
        </button>
      )}

      {breaks.length > 0 && (
        <section className="presentation-break-history" aria-label="Presentation log">
          <div className="presentation-break-history-heading">
            <div className="presentation-break-columns" aria-hidden="true">
              <span title="Presentation time"><Timer /></span>
              <span title="Break duration"><Coffee /></span>
            </div>
            <button
              ref={expandButtonRef}
              className="presentation-statistics-expand"
              type="button"
              aria-label="Expand presentation statistics"
              title="Expand presentation statistics"
              onClick={() => { onExpandedChange(true); }}
            >
              <Maximize2 aria-hidden="true" />
            </button>
          </div>
          <div className="presentation-break-list">
            {compactRows.map(({ breakEntry, presentationSeconds }) => (
              <button
                className="presentation-break-row"
                key={breakEntry.breakNumber}
                type="button"
                aria-label={`Presentation session ${breakEntry.breakNumber}: ${formatPresentationTime(presentationSeconds)}, followed by break ${breakEntry.breakNumber}: ${formatPresentationTime(breakEntry.elapsedSeconds)}`}
                onClick={() => { setSelectedBreakNumber(breakEntry.breakNumber); }}
              >
                <span className="presentation-segment-value">{formatPresentationTime(presentationSeconds)}</span>
                <span className="presentation-break-value">{formatPresentationTime(breakEntry.elapsedSeconds)}</span>
              </button>
            ))}
          </div>
        </section>
      )}

      {isExpanded && (
        <section className="presentation-statistics-fullscreen" aria-labelledby="presentation-statistics-title">
          <header className="presentation-statistics-fullscreen-header">
            <div>
              <p>Live statistics</p>
              <h2 id="presentation-statistics-title">Presentation statistics</h2>
            </div>
            <div className="presentation-statistics-header-actions">
              <button
                className="presentation-statistics-end"
                type="button"
                onClick={onEndPresentation}
              >
                <CircleStop aria-hidden="true" />
                <span>End presentation</span>
              </button>
              <button
                ref={minimizeButtonRef}
                className="presentation-statistics-minimize"
                type="button"
                aria-label="Restore presentation timer"
                title="Restore presentation timer"
                onClick={closeExpanded}
              >
                <Minimize2 aria-hidden="true" />
              </button>
            </div>
          </header>

          <div className="presentation-statistics-scroll">
            <dl className="presentation-statistics-summary">
              <div className="primary"><dt>Session time</dt><dd>{formatPresentationTime(currentSessionSeconds)}</dd></div>
              <div><dt>Presentation total</dt><dd>{formatPresentationTime(elapsedSeconds)}</dd></div>
              <div><dt>Slides</dt><dd>{slides.length}</dd></div>
              <div><dt>Sessions</dt><dd>{presentationSessionCount}</dd></div>
              <div><dt>Total elapsed</dt><dd>{formatPresentationTime(totalElapsedSeconds)}</dd></div>
              <div><dt>Breaks</dt><dd>{breaks.length} · {formatPresentationTime(totalBreakSeconds)}</dd></div>
              <div><dt>Type</dt><dd>{sessionTarget ? targetLabels[sessionTarget] : "Not started"}</dd></div>
              <div className="presentation-statistics-status-card">
                <dt>Status</dt>
                <dd>
                  <span>{currentStatus}</span>
                  {!isResetPending && (isRunning || isPaused) && (
                    <button
                      type="button"
                      className="primary"
                      disabled={isRunning && !canPause}
                      onClick={isPaused ? onResume : onPause}
                    >
                      {isPaused ? <Play aria-hidden="true" /> : <Pause aria-hidden="true" />}
                      <span>{isPaused ? "Resume" : "Pause"}</span>
                    </button>
                  )}
                </dd>
              </div>
            </dl>

            {sessionStartedAt && (
              <p className="presentation-statistics-started">
                Started {formatRecordedDate(sessionStartedAt)}
              </p>
            )}

            <section className="presentation-statistics-timeline-section" aria-labelledby="presentation-statistics-timeline-title">
              <div className="presentation-statistics-section-heading">
                <BarChart3 aria-hidden="true" />
                <h3 id="presentation-statistics-timeline-title">Presentation flow</h3>
              </div>
              <div
                className="presentation-statistics-timeline"
                role="img"
                aria-label={timeline.map((entry) => `${entry.label}${entry.slideLabel ? `, ${entry.slideLabel}` : ""}, ${formatPresentationTime(entry.seconds)}`).join(", ")}
              >
                {timeline.map((entry) => (
                  <span
                    className={`presentation-statistics-timeline-segment ${entry.kind}`}
                    key={`${entry.kind}-${entry.label}`}
                    title={`${entry.label}: ${formatPresentationTime(entry.seconds)}`}
                    style={{ flexGrow: Math.max(0.02, entry.seconds / totalTimelineSeconds) }}
                  />
                ))}
              </div>
              <div className="presentation-statistics-legend">
                <span className="presentation"><Timer aria-hidden="true" />Presentation</span>
                <span className="break"><Coffee aria-hidden="true" />Break</span>
              </div>
            </section>

            {slides.length > 0 && (
              <section className="presentation-statistics-details" aria-labelledby="presentation-slide-breakdown-title">
                <div className="presentation-slide-breakdown-heading">
                  <h3 id="presentation-slide-breakdown-title">Slide breakdown</h3>
                  <p>Estimated from presentation controls</p>
                </div>
                <div className="presentation-statistics-table" role="list">
                  {slides.map((slide) => {
                    const isCurrent = slide.slideNumber === currentSlideNumber;
                    return (
                      <div
                        className="presentation-statistics-log-entry presentation"
                        role="listitem"
                        aria-current={isCurrent ? "step" : undefined}
                        aria-label={`Slide ${slide.slideNumber}: ${formatSlideDuration(slide.elapsedSeconds)}${isCurrent ? ", current slide" : ""}`}
                        key={slide.slideNumber}
                      >
                        <span className="presentation-statistics-log-icon" aria-hidden="true"><Timer /></span>
                        <strong>
                          Slide #{slide.slideNumber}
                          {isCurrent && <span className="presentation-slide-current">Current</span>}
                        </strong>
                        <span>{formatSlideDuration(slide.elapsedSeconds)}</span>
                      </div>
                    );
                  })}
                </div>
              </section>
            )}

            <section className="presentation-statistics-details" aria-labelledby="presentation-statistics-details-title">
              <h3 id="presentation-statistics-details-title">Sessions and breaks</h3>
              <div className="presentation-statistics-table" role="list">
                {timeline.map((entry) => (
                  <div className={`presentation-statistics-log-entry ${entry.kind}`} role="listitem" key={`${entry.kind}-${entry.label}`}>
                    <span className="presentation-statistics-log-icon" aria-hidden="true">
                      {entry.kind === "presentation" ? <Timer /> : <Coffee />}
                    </span>
                    <strong>
                      {entry.label}
                      {entry.slideLabel && <span className="presentation-session-slides">{entry.slideLabel}</span>}
                    </strong>
                    <span>{formatPresentationTime(entry.seconds)}</span>
                  </div>
                ))}
              </div>
            </section>
          </div>
        </section>
      )}

      <ModalDialog
        className="presentation-break-dialog"
        dismissLabel="OK"
        focusDismissAction
        isOpen={selectedBreak !== null}
        onClose={() => { setSelectedBreakNumber(null); }}
        title={selectedBreak ? `Break ${selectedBreak.breakNumber}` : "Break"}
      >
        {selectedBreak && (
          <dl className="presentation-break-dialog-details">
            <div><dt>Presentation checkpoint</dt><dd>{formatPresentationTime(selectedBreak.presentationElapsedSeconds)}</dd></div>
            <div><dt>Break duration</dt><dd>{formatPresentationTime(selectedBreak.elapsedSeconds)}</dd></div>
            <div><dt>Started</dt><dd>{formatRecordedDate(selectedBreak.startedAt)}</dd></div>
            <div><dt>Status</dt><dd>{selectedBreak.endedAt === null ? "In progress" : "Completed"}</dd></div>
            {selectedBreak.endedAt && <div><dt>Ended</dt><dd>{formatRecordedDate(selectedBreak.endedAt)}</dd></div>}
          </dl>
        )}
      </ModalDialog>
    </>
  );
}
