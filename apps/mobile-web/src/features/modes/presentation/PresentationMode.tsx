import { ChevronDown, ChevronLeft, ChevronRight, CircleStop, Eclipse, MousePointer2, Pause, Play, RotateCcw, Timer, Vibrate, Volume2, VolumeX } from "lucide-react";
import { useEffect, useRef, useState, type ReactNode } from "react";
import "./presentation.css";
import type { PendingPresentationCommand } from "../../../foundation/connection/usePresentationControl";
import type { AudioStateMessage, PresentationAction, PresentationCapability, PresentationCommandResultMessage, PresentationReportSavePayload, PresentationReportSaveResultMessage, PresentationTarget, SystemPowerAction } from "../../../foundation/protocol/messages";
import { formatPresentationTime, maximumPresentationBreaks, usePresentationTimer } from "./presentationTimer";
import { InfoButton } from "../../../ui/overlays/InfoButton";
import { ModalDialog } from "../../../ui/overlays/ModalDialog";
import { PresentationStatistics } from "./PresentationStatistics";

interface PresentationModeProps {
  audioState: AudioStateMessage | null;
  blackoutAvailable: boolean;
  capability: PresentationCapability | undefined;
  connected: boolean;
  pending: PendingPresentationCommand | null;
  pendingPowerAction: SystemPowerAction | null;
  result: PresentationCommandResultMessage | null;
  onCommand: (target: PresentationTarget, action: PresentationAction, enabled?: boolean) => void;
  onMute: () => void;
  onPowerAction?: (action: SystemPowerAction) => void;
  onSessionActiveChange?: ((active: boolean) => void) | undefined;
  onSaveReport?: ((report: PresentationReportSavePayload) => void) | undefined;
  reportSaveResult?: PresentationReportSaveResultMessage | null | undefined;
  onVolumeDown: () => void;
  onVolumeUp: () => void;
  reportSavePending?: boolean | undefined;
  reportSavingAvailable?: boolean | undefined;
  renderTrackpad: (options: {
    isFullscreen: boolean;
    onToggleFullscreen: () => void;
  }) => ReactNode;
}

const targetOptions = [
  { id: "powerpoint", label: "PowerPoint" },
  { id: "google-slides", label: "Google Slides" },
  { id: "pdf", label: "PDF / browser" }
] satisfies { id: PresentationTarget; label: string }[];

export function PresentationMode({
  audioState,
  blackoutAvailable,
  capability,
  connected,
  pending,
  pendingPowerAction,
  reportSavePending = false,
  reportSaveResult = null,
  reportSavingAvailable = false,
  renderTrackpad,
  result,
  onCommand,
  onMute,
  onPowerAction,
  onSessionActiveChange,
  onSaveReport,
  onVolumeDown,
  onVolumeUp
}: PresentationModeProps) {
  const [target, setTarget] = useState<PresentationTarget>("powerpoint");
  const [isTargetSelectorOpen, setIsTargetSelectorOpen] = useState(false);
  const [isTimerExpanded, setIsTimerExpanded] = useState(true);
  const [isTrackpadExpanded, setIsTrackpadExpanded] = useState(false);
  const [isTrackpadFullscreen, setIsTrackpadFullscreen] = useState(false);
  const [isStatisticsExpanded, setIsStatisticsExpanded] = useState(false);
  const laserPointerActive = capability?.laserPointerActive === true;
  const savePresentationRef = useRef<HTMLButtonElement | null>(null);
  const safeCompletionActionRef = useRef<HTMLButtonElement | null>(null);
  const laserPointerActiveRef = useRef(capability?.laserPointerActive === true);
  const laserPointerRequestedRef = useRef(false);
  const targetRef = useRef(target);
  const onCommandRef = useRef(onCommand);
  const timer = usePresentationTimer();
  const {
    isResetPending: timerResetPending,
    reset: resetTimer,
    sessionReportId: timerSessionReportId
  } = timer;
  const supported = capability !== undefined;
  const canControl = connected && capability?.canControl === true;
  const controlsDisabled = !canControl || pending !== null;
  const blackoutDisabled = controlsDisabled || !blackoutAvailable || pendingPowerAction !== null || !onPowerAction;
  const targetLabel = targetOptions.find((option) => option.id === target)?.label ?? target;
  const sessionTargetLabel = targetOptions.find((option) => option.id === timer.sessionTarget)?.label ?? targetLabel;
  const canSaveReport = connected && reportSavingAvailable && onSaveReport !== undefined;
  const sessionActive = timer.sessionStartedAt !== null;
  const presentationEnded = timer.completionIntent === "end";

  useEffect(() => {
    onSessionActiveChange?.(sessionActive);
  }, [onSessionActiveChange, sessionActive]);

  useEffect(() => () => {
    onSessionActiveChange?.(false);
  }, [onSessionActiveChange]);

  useEffect(() => {
    if (reportSaveResult?.succeeded === true &&
        reportSaveResult.reportId === timerSessionReportId &&
        timerResetPending) {
      const completion = window.setTimeout(() => {
        setIsStatisticsExpanded(false);
        resetTimer();
      }, 0);
      return () => { window.clearTimeout(completion); };
    }
    return undefined;
  }, [reportSaveResult, resetTimer, timerResetPending, timerSessionReportId]);

  useEffect(() => {
    laserPointerActiveRef.current = laserPointerActive;
  }, [laserPointerActive]);

  useEffect(() => {
    if (result?.succeeded === true) {
      laserPointerRequestedRef.current = result.laserPointerActive;
    }
  }, [result]);

  useEffect(() => {
    targetRef.current = target;
    onCommandRef.current = onCommand;
  }, [onCommand, target]);

  useEffect(() => () => {
    if (laserPointerActiveRef.current || laserPointerRequestedRef.current) {
      onCommandRef.current(targetRef.current, "pointer", false);
    }
  }, []);

  const request = (action: PresentationAction, enabled?: boolean) => {
    if (enabled === undefined) {
      onCommand(target, action);
    } else {
      onCommand(target, action, enabled);
    }
  };
  const previousSlide = () => {
    request("previous");
    timer.changeSlide("previous", target);
  };
  const nextSlide = () => {
    request("next");
    timer.changeSlide("next", target);
  };
  const startSlideshow = () => {
    request("start");
    timer.startSlideshow(target);
  };
  const toggleTrackpad = () => {
    setIsTrackpadExpanded((current) => {
      const next = !current;
      if (next) {
        setIsTimerExpanded(false);
      } else {
        setIsTrackpadFullscreen(false);
      }
      return next;
    });
  };
  const toggleTimer = () => {
    setIsTimerExpanded((current) => {
      const next = !current;
      if (next) {
        setIsTrackpadExpanded(false);
        setIsTrackpadFullscreen(false);
      }
      return next;
    });
  };
  const activateLaserPointer = () => {
    laserPointerRequestedRef.current = !laserPointerActive;
    request("pointer", !laserPointerActive);
    if (!laserPointerActive) {
      setIsTimerExpanded(false);
      setIsTrackpadExpanded(true);
    }
  };
  const endSlideshow = () => {
    request("end");
    if (timer.sessionStartedAt !== null) {
      timer.requestEnd();
    }
  };
  const resetWithoutSaving = () => {
    setIsStatisticsExpanded(false);
    timer.reset();
  };
  const saveReport = () => {
    if (!onSaveReport ||
        timer.sessionReportId === null ||
        timer.sessionTarget === null ||
        timer.sessionStartedAt === null ||
        timer.completionEndedAt === null) {
      return;
    }

    const completionEndedAt = timer.completionEndedAt;
    onSaveReport({
      reportId: timer.sessionReportId,
      target: timer.sessionTarget,
      startedAt: timer.sessionStartedAt,
      endedAt: completionEndedAt,
      utcOffsetMinutes: -new Date(completionEndedAt).getTimezoneOffset(),
      plannedDurationSeconds: timer.durationMinutes * 60,
      presentationDurationSeconds: timer.elapsedSeconds,
      endedDuringBreak: timer.isPaused,
      breaks: timer.breaks.map((entry) => {
        const slideNumberAtEnd = entry.slideNumberAtEnd ?? timer.currentSlideNumber;
        return {
          breakNumber: entry.breakNumber,
          presentationElapsedSeconds: entry.presentationElapsedSeconds,
          breakDurationSeconds: entry.elapsedSeconds,
          startedAt: entry.startedAt,
          endedAt: entry.endedAt ?? completionEndedAt,
          ...(entry.sessionSlideMinimum === null ? {} : { sessionSlideMinimum: entry.sessionSlideMinimum }),
          ...(entry.sessionSlideMaximum === null ? {} : { sessionSlideMaximum: entry.sessionSlideMaximum }),
          ...(entry.slideNumberAtStart === null ? {} : { slideNumberAtStart: entry.slideNumberAtStart }),
          ...(slideNumberAtEnd === null ? {} : { slideNumberAtEnd })
        };
      }),
      slides: timer.slides.map((slide) => ({
        slideNumber: slide.slideNumber,
        ...(slide.elapsedSeconds === null ? {} : { durationSeconds: slide.elapsedSeconds })
      }))
    });
  };
  const selectTarget = (nextTarget: PresentationTarget) => {
    setTarget(nextTarget);
    setIsTargetSelectorOpen(false);
  };

  return (
    <section
      className={`presentation-mode${isTrackpadExpanded ? " trackpad-open" : ""}${isTimerExpanded ? " timer-open" : ""}${isTrackpadFullscreen ? " trackpad-fullscreen" : ""}`}
      aria-labelledby="presentation-title"
    >
      <div className="presentation-controls-panel">
        <header className="presentation-header">
          <div>
            <div className="presentation-title-row">
              <h1 id="presentation-title">Presentation</h1>
              <InfoButton
                description="Choose the active presentation app, then keep that app focused on the PC."
                size="detailed"
                title="Presentation guidance"
              />
            </div>
          </div>
          <div className="presentation-target-selector">
            <button
              className="presentation-target-selector-toggle"
              type="button"
              aria-expanded={isTargetSelectorOpen}
              aria-haspopup="menu"
              aria-label={`Change presentation mode (${targetLabel})`}
              onClick={() => { setIsTargetSelectorOpen((current) => !current); }}
            >
              <span>{targetLabel}</span>
              <ChevronDown aria-hidden="true" />
            </button>
            {isTargetSelectorOpen && (
              <>
                <button className="presentation-target-selector-scrim" type="button" aria-label="Close presentation mode selector" onClick={() => { setIsTargetSelectorOpen(false); }} />
                <div className="presentation-target-selector-menu" role="menu" aria-label="Change presentation mode">
                  {targetOptions.map((option) => (
                    <button
                      type="button"
                      key={option.id}
                      role="menuitemradio"
                      aria-checked={target === option.id}
                      className={target === option.id ? "active" : ""}
                      onClick={() => { selectTarget(option.id); }}
                    >
                      {option.label}
                    </button>
                  ))}
                </div>
              </>
            )}
          </div>
        </header>
        {!supported && <p className="presentation-permission-message" role="alert">Update the Windows host to use Presentation mode.</p>}
        {supported && !capability.canControl && <p className="presentation-permission-message" role="alert">Presentation control is blocked by the host. Enable its global or per-device permission.</p>}
        {supported && capability.canControl && !blackoutAvailable && <p id="presentation-blackout-disabled" className="presentation-permission-message" role="alert">Blackout is disabled by the host. Enable its power permission for this device.</p>}

        <div className="presentation-navigation">
          <button type="button" disabled={controlsDisabled} onClick={previousSlide}>
            <ChevronLeft aria-hidden="true" />
            <span>Previous</span>
          </button>
          <button type="button" className="primary" disabled={controlsDisabled} onClick={nextSlide}>
            <span>Next</span>
            <ChevronRight aria-hidden="true" />
          </button>
        </div>

        <div className="presentation-actions">
          {target === "powerpoint" && (
            <button
              type="button"
              disabled={controlsDisabled}
              onClick={startSlideshow}
            >
              <Play aria-hidden="true" /><span>Start slideshow</span>
            </button>
          )}
          <button type="button" disabled={controlsDisabled} onClick={endSlideshow}>
            <CircleStop aria-hidden="true" /><span>End slideshow</span>
          </button>
          {target !== "pdf" && (
            <button
              type="button"
              aria-describedby={!blackoutAvailable ? "presentation-blackout-disabled" : undefined}
              disabled={blackoutDisabled}
              onClick={() => {
                if (!blackoutDisabled) {
                  onPowerAction("blackoutDisplay");
                }
              }}
            >
              <Eclipse aria-hidden="true" /><span>Blackout</span>
            </button>
          )}
          <button
            type="button"
            className={laserPointerActive ? "active" : undefined}
            aria-pressed={laserPointerActive}
            disabled={controlsDisabled}
            onClick={activateLaserPointer}
          >
            <MousePointer2 aria-hidden="true" /><span>Laser pointer</span>
          </button>
        </div>

        <div className="presentation-volume-actions" aria-label="Volume controls">
          <button type="button" aria-label="Volume down" disabled={!connected} onClick={onVolumeDown}>
            <Volume2 aria-hidden="true" /><span>Vol -</span>
          </button>
          <button type="button" aria-label={audioState?.muted ? "Unmute PC" : "Mute PC"} disabled={!connected} onClick={onMute}>
            {audioState?.muted ? <VolumeX aria-hidden="true" /> : <Volume2 aria-hidden="true" />}
            <span>{audioState?.muted ? "Unmute" : "Mute"}</span>
          </button>
          <button type="button" aria-label="Volume up" disabled={!connected} onClick={onVolumeUp}>
            <Volume2 aria-hidden="true" /><span>Vol +</span>
          </button>
        </div>

        {result && !result.succeeded && (
          <div className="presentation-result error" role="alert" aria-live="polite">
            {result.message}
          </div>
        )}
      </div>

      <div className="presentation-side-stack">
        <aside className="presentation-trackpad" aria-labelledby="presentation-trackpad-title">
          <button
            className="presentation-trackpad-heading"
            type="button"
            aria-expanded={isTrackpadExpanded}
            aria-controls="presentation-trackpad-content"
            onClick={toggleTrackpad}
          >
            <MousePointer2 aria-hidden="true" />
            <h2 id="presentation-trackpad-title">Trackpad</h2>
            <ChevronDown aria-hidden="true" />
          </button>
          {isTrackpadExpanded && (
            <div className="presentation-trackpad-content" id="presentation-trackpad-content">
              {renderTrackpad({
                isFullscreen: isTrackpadFullscreen,
                onToggleFullscreen: () => { setIsTrackpadFullscreen((current) => !current); }
              })}
            </div>
          )}
        </aside>

        <aside className="presentation-timer" aria-labelledby="presentation-timer-title">
        <button
          className="presentation-timer-heading"
          type="button"
          aria-expanded={isTimerExpanded}
          aria-controls="presentation-timer-content"
          onClick={toggleTimer}
        >
          <Timer aria-hidden="true" />
          <h2 id="presentation-timer-title">{timer.isPaused ? "Break timer" : "Timer"}</h2>
          {!isTimerExpanded && timer.sessionStartedAt !== null && (
            <output
              className={`presentation-timer-heading-time${timer.isPaused ? " break" : ""}`}
              aria-label={timer.isPaused ? "Elapsed break time" : "Elapsed presentation time"}
            >
              {formatPresentationTime(timer.isPaused ? timer.breakElapsedSeconds : timer.elapsedSeconds)}
            </output>
          )}
          <ChevronDown aria-hidden="true" />
        </button>
        {isTimerExpanded && (
          <div className="presentation-timer-content" id="presentation-timer-content">
            <div className={`presentation-timer-live${timer.breaks.length > 0 ? " has-history" : timer.sessionStartedAt !== null ? " has-statistics" : ""}`}>
              <div className="presentation-primary-time">
                <output className={`presentation-time${timer.isPaused ? " break-time" : ""}`} aria-label={timer.isPaused ? "Elapsed break time" : "Elapsed presentation time"}>{formatPresentationTime(timer.isPaused ? timer.breakElapsedSeconds : timer.elapsedSeconds)}</output>
                {timer.slides.length > 0 && (
                  <p className="presentation-slide-count">
                    {timer.slides.length} {timer.slides.length === 1 ? "slide" : "slides"}
                  </p>
                )}
                {timer.speedMultiplier > 1 && (
                  <p className="presentation-test-speed" role="status">
                    Test speed {timer.speedMultiplier}×
                  </p>
                )}
              </div>
              <PresentationStatistics
                breaks={timer.breaks}
                canPause={timer.canPause}
                currentSlideNumber={timer.currentSlideNumber}
                currentSessionSlideMaximum={timer.currentSessionSlideMaximum}
                currentSessionSlideMinimum={timer.currentSessionSlideMinimum}
                elapsedSeconds={timer.elapsedSeconds}
                isExpanded={isStatisticsExpanded}
                isPaused={timer.isPaused}
                isResetPending={timer.isResetPending}
                isRunning={timer.isRunning}
                onExpandedChange={setIsStatisticsExpanded}
                onEndPresentation={timer.requestEnd}
                onPause={timer.pause}
                onResume={() => { timer.start(target); }}
                presentationSessionCount={timer.presentationSessionCount}
                sessionStartedAt={timer.sessionStartedAt}
                sessionTarget={timer.sessionTarget}
                slides={timer.slides}
                totalBreakSeconds={timer.totalBreakSeconds}
              />
            </div>
            {timer.milestoneMessage && <p className="presentation-milestone" role="status" aria-live="polite">{timer.milestoneMessage}</p>}
            <div className="presentation-timer-actions">
              {!timer.isRunning && !timer.isResetPending && <button type="button" className="primary" onClick={() => { timer.start(target); }}><Play aria-hidden="true" /><span>{timer.elapsedSeconds > 0 ? "Resume" : "Start"}</span></button>}
              {timer.isRunning && (
                <button
                  type="button"
                  className="primary"
                  disabled={!timer.canPause}
                  aria-describedby={!timer.canPause ? "presentation-break-limit" : undefined}
                  onClick={timer.pause}
                >
                  <Pause aria-hidden="true" /><span>Pause</span>
                </button>
              )}
              {timer.isResetPending && <button type="button" className="primary" disabled><Pause aria-hidden="true" /><span>Timer frozen</span></button>}
              <div className="presentation-reset-control">
                <button type="button" disabled={timer.sessionStartedAt === null || timer.isResetPending} onClick={timer.requestReset}><RotateCcw aria-hidden="true" /><span>Reset</span></button>
              </div>
            </div>
            {!timer.canPause && <p id="presentation-break-limit" className="presentation-break-limit" role="status">This session has reached the {maximumPresentationBreaks}-break limit. Save or reset it before starting another break.</p>}
            <label className="presentation-duration">
              <span>Planned duration</span>
              <select value={timer.durationMinutes} onChange={(event) => { timer.changeDuration(Number(event.target.value)); }}>
                {[10, 15, 30, 45, 60].map((minutes) => <option key={minutes} value={minutes}>{minutes} minutes</option>)}
              </select>
            </label>
            {timer.supportsVibration && (
              <label className="presentation-vibration">
                <input type="checkbox" checked={timer.vibrationEnabled} onChange={(event) => { timer.setVibrationEnabled(event.target.checked); }} />
                <Vibrate aria-hidden="true" />
                <span>Vibrate at 5 minutes remaining and time elapsed</span>
              </label>
            )}
          </div>
        )}
        </aside>
      </div>

      <ModalDialog
        actions={(
          <>
            <button
              ref={savePresentationRef}
              type="button"
              className="primary"
              disabled={!canSaveReport || reportSavePending}
              onClick={saveReport}
            >
              {reportSavePending
                ? "Saving…"
                : presentationEnded
                  ? "Save presentation"
                  : "Save and reset"}
            </button>
            <button type="button" onClick={resetWithoutSaving}>
              {presentationEnded ? "Discard presentation data" : "Reset without saving"}
            </button>
            <button ref={safeCompletionActionRef} type="button" onClick={timer.cancelReset}>
              {presentationEnded ? "Continue timing" : "Cancel"}
            </button>
          </>
        )}
        actionsClassName="presentation-reset-dialog-actions"
        className="presentation-reset-dialog"
        dismissLabel={presentationEnded ? "Continue timing" : "Cancel"}
        initialFocusRef={canSaveReport ? savePresentationRef : safeCompletionActionRef}
        isOpen={timer.isResetPending}
        onClose={timer.cancelReset}
        title={presentationEnded ? "Presentation ended" : "Save presentation data"}
      >
        {presentationEnded ? (
          <p>
            {sessionTargetLabel} ended with {formatPresentationTime(timer.elapsedSeconds)} of presentation time
            across {timer.presentationSessionCount} {timer.presentationSessionCount === 1 ? "session" : "sessions"}
            {timer.slides.length > 0 ? ` and ${timer.slides.length} ${timer.slides.length === 1 ? "slide" : "slides"}` : ""}.
          </p>
        ) : (
          <p>
            {sessionTargetLabel} has {formatPresentationTime(timer.elapsedSeconds)} of presentation time
            {timer.breaks.length > 0 ? ` and ${timer.breaks.length} ${timer.breaks.length === 1 ? "break" : "breaks"}` : ""}.
          </p>
        )}
        {!reportSavingAvailable && (
          <p className="presentation-save-unavailable">
            {presentationEnded
              ? "This PC does not support saving presentation statistics yet. You can continue timing or discard this data."
              : "This PC does not support saving presentation statistics yet. You can cancel and continue, or reset without saving."}
          </p>
        )}
        {reportSavingAvailable && !connected && <p className="presentation-save-unavailable">Reconnect to the PC to save this presentation. The frozen timer will stay here while you reconnect.</p>}
        {reportSaveResult?.succeeded === false &&
          reportSaveResult.reportId === timer.sessionReportId && (
            <p className="presentation-save-unavailable" role="alert">{reportSaveResult.message}</p>
          )}
      </ModalDialog>
    </section>
  );
}
