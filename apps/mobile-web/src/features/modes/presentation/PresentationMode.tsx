import { ChevronDown, ChevronLeft, ChevronRight, CircleStop, Eclipse, ListOrdered, MousePointer2, Pause, Play, RotateCcw, Timer, Vibrate, Volume2, VolumeX } from "lucide-react";
import { useEffect, useRef, useState, type ReactNode } from "react";
import "./presentation.css";
import type { PendingPresentationCommand } from "../../../foundation/connection/usePresentationControl";
import type { AppLaunchActionSummary, AppLaunchResultMessage, AudioStateMessage, PowerPointLaunchResultMessage, PresentationAction, PresentationCapability, PresentationCommandOptions, PresentationCommandResultMessage, PresentationReportSavePayload, PresentationReportSaveResultMessage, PresentationSessionAction, PresentationTarget, SystemPowerAction } from "../../../foundation/protocol/messages";
import { formatPresentationTime, maximumPresentationBreaks, usePresentationTimer } from "./presentationTimer";
import { InfoButton } from "../../../ui/overlays/InfoButton";
import { ModalDialog } from "../../../ui/overlays/ModalDialog";
import { uiDurations } from "../../../ui/tokens.g";
import { PresentationStatistics } from "./PresentationStatistics";
import { PowerPointPresentationChooser, type PowerPointChooserSelection } from "./PowerPointPresentationChooser";

interface PresentationModeProps {
  activationRequestId?: number | undefined;
  audioState: AudioStateMessage | null;
  blackoutAvailable: boolean;
  capability: PresentationCapability | undefined;
  connected: boolean;
  pending: PendingPresentationCommand | null;
  pendingPowerPointLaunch?: { operationId: string; presentationId: string } | null | undefined;
  powerPointLaunchResult?: PowerPointLaunchResultMessage | null | undefined;
  powerPointAppLaunchAction?: AppLaunchActionSummary | undefined;
  powerPointAppLaunchResult?: AppLaunchResultMessage | null | undefined;
  pendingPowerPointAppLaunch?: boolean | undefined;
  powerPointRefreshPending?: boolean | undefined;
  sessionPending?: boolean | undefined;
  pendingPowerAction: SystemPowerAction | null;
  result: PresentationCommandResultMessage | null;
  onActivationRequestHandled?: (() => void) | undefined;
  onCommand: (target: PresentationTarget, action: PresentationAction, options?: boolean | PresentationCommandOptions) => void;
  onSessionCommand?: (action: PresentationSessionAction, options?: { enabled?: boolean; runtimePresentationId?: string }) => void;
  onMute: () => void;
  onPowerAction?: (action: SystemPowerAction) => void;
  onPowerPointRefresh?: () => void;
  onPowerPointLaunch?: (presentationId: string) => void;
  onPowerPointAppLaunch?: (actionId: string) => void;
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
const maximumDirectSlideNumber = 1000;

export function PresentationMode({
  activationRequestId = 0,
  audioState,
  blackoutAvailable,
  capability,
  connected,
  pending,
  pendingPowerPointLaunch = null,
  powerPointLaunchResult = null,
  powerPointAppLaunchAction,
  powerPointAppLaunchResult = null,
  pendingPowerPointAppLaunch = false,
  powerPointRefreshPending = false,
  pendingPowerAction,
  sessionPending = false,
  reportSavePending = false,
  reportSaveResult = null,
  reportSavingAvailable = false,
  renderTrackpad,
  result,
  onActivationRequestHandled,
  onCommand,
  onMute,
  onPowerAction,
  onPowerPointRefresh,
  onPowerPointLaunch,
  onPowerPointAppLaunch,
  onSessionActiveChange,
  onSaveReport,
  onSessionCommand,
  onVolumeDown,
  onVolumeUp
}: PresentationModeProps) {
  const [target, setTarget] = useState<PresentationTarget>("powerpoint");
  const [isTargetSelectorOpen, setIsTargetSelectorOpen] = useState(false);
  const [isTimerExpanded, setIsTimerExpanded] = useState(true);
  const [isTrackpadExpanded, setIsTrackpadExpanded] = useState(false);
  const [isTrackpadFullscreen, setIsTrackpadFullscreen] = useState(false);
  const [isStatisticsExpanded, setIsStatisticsExpanded] = useState(false);
  const [isNavigationOpen, setIsNavigationOpen] = useState(false);
  const [gotoSlideNumber, setGotoSlideNumber] = useState("");
  const [runtimePresentationId, setRuntimePresentationId] = useState<string | null>(null);
  const [savedPresentationId, setSavedPresentationId] = useState<string | null>(null);
  const [isPowerPointChooserOpen, setIsPowerPointChooserOpen] = useState(false);
  const [powerPointChooserSelection, setPowerPointChooserSelection] = useState<PowerPointChooserSelection>(null);
  const [visiblyPendingOperationId, setVisiblyPendingOperationId] = useState<string | null>(null);
  const laserPointerActive = capability?.laserPointerActive === true;
  const savePresentationRef = useRef<HTMLButtonElement | null>(null);
  const safeCompletionActionRef = useRef<HTMLButtonElement | null>(null);
  const laserPointerActiveRef = useRef(capability?.laserPointerActive === true);
  const laserPointerRequestedRef = useRef(false);
  const foregroundedPresentationRequestRef = useRef(0);
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
  const controlsDisabled = !canControl;
  const commandPending = pending !== null;
  const commandControlsDisabled = controlsDisabled || commandPending;
  const showPendingCommandDisabled = pending !== null &&
    visiblyPendingOperationId === pending.operationId;
  const blackoutDisabled = controlsDisabled || !blackoutAvailable || pendingPowerAction !== null || !onPowerAction;
  const targetLabel = targetOptions.find((option) => option.id === target)?.label ?? target;
  const sessionTargetLabel = targetOptions.find((option) => option.id === timer.sessionTarget)?.label ?? targetLabel;
  const canSaveReport = connected && reportSavingAvailable && onSaveReport !== undefined;
  const sessionActive = timer.sessionStartedAt !== null;
  const presentationEnded = timer.completionIntent === "end";
  const powerPointPresentations = capability?.powerPoint?.presentations;
  const availablePowerPointPresentations = capability?.powerPoint?.availablePresentations ?? [];
  const selectedSavedPowerPoint = availablePowerPointPresentations.find(
    (presentation) => presentation.presentationId === savedPresentationId) ?? null;
  const effectiveRuntimePresentationId =
    powerPointPresentations?.some(
      (presentation) => presentation.runtimePresentationId === runtimePresentationId) === true
      ? runtimePresentationId
      : selectedSavedPowerPoint === null && powerPointPresentations?.length === 1
        ? powerPointPresentations[0]?.runtimePresentationId ?? null
        : null;
  const selectedPowerPoint = powerPointPresentations?.find(
    (presentation) => presentation.runtimePresentationId === effectiveRuntimePresentationId) ?? null;
  const directSlideMaximum = Math.max(
    1,
    Math.min(
      selectedPowerPoint?.slideCount ?? maximumDirectSlideNumber,
      maximumDirectSlideNumber));
  const directSlideValue = Math.min(
    directSlideMaximum,
    Math.max(1, Number(gotoSlideNumber) || 1));
  const powerPointOptions = effectiveRuntimePresentationId === null
    ? undefined
    : { runtimePresentationId: effectiveRuntimePresentationId };
  const powerPointRunning = selectedPowerPoint?.state === "presenting";
  const powerPointSession = capability?.powerPoint?.session;
  const hasPowerPointAutomation = capability?.powerPoint !== undefined &&
    capability.powerPoint !== null;
  const verifiedPowerPoint = capability?.powerPoint?.state === "ready";
  const powerPointBlank = selectedPowerPoint?.slideShowState === "black" ||
    selectedPowerPoint?.slideShowState === "white";
  const powerPointRunningControlDisabled = controlsDisabled ||
    (hasPowerPointAutomation && (!verifiedPowerPoint || !powerPointRunning));
  const powerPointStartControlDisabled = controlsDisabled ||
    !verifiedPowerPoint ||
    selectedPowerPoint === null;
  const powerPointReadyControlDisabled = controlsDisabled ||
    !verifiedPowerPoint ||
    selectedPowerPoint === null;
  const powerPointNavigationDisabled = controlsDisabled ||
    (hasPowerPointAutomation &&
      (!verifiedPowerPoint ||
       selectedPowerPoint === null ||
       (!powerPointRunning && selectedPowerPoint.currentSlideIndex === null)));
  const navigationControlDisabled = target === "powerpoint"
    ? powerPointNavigationDisabled
    : controlsDisabled;
  const endControlDisabled = target === "powerpoint"
    ? powerPointRunningControlDisabled
    : controlsDisabled;
  const laserControlDisabled = target === "powerpoint"
    ? powerPointRunningControlDisabled
    : controlsDisabled;
  const usesAuthoritativePowerPointSession = target === "powerpoint" &&
    verifiedPowerPoint;
  const reportedSessionActive = sessionActive ||
    powerPointSession?.state === "tracking";
  const presentationChangeLockedMessage = powerPointSession?.state !== undefined &&
    powerPointSession.state !== "inactive"
      ? "Save or discard the current presentation before changing decks."
      : laserPointerActive
        ? "Turn off the laser pointer before changing decks."
        : null;
  const savedPowerPointStartDisabled = controlsDisabled ||
    selectedSavedPowerPoint === null ||
    pendingPowerPointLaunch !== null ||
    presentationChangeLockedMessage !== null ||
    onPowerPointLaunch === undefined;

  useEffect(() => {
    if (powerPointLaunchResult?.succeeded !== true ||
        !powerPointLaunchResult.runtimePresentationId) {
      return;
    }

    const runtimeId = powerPointLaunchResult.runtimePresentationId;
    const completion = window.setTimeout(() => {
      setRuntimePresentationId(runtimeId);
      setSavedPresentationId(null);
      setPowerPointChooserSelection(null);
      setIsPowerPointChooserOpen(false);
    }, 0);
    return () => { window.clearTimeout(completion); };
  }, [powerPointLaunchResult]);

  useEffect(() => {
    if (pending === null) {
      return;
    }

    const operationId = pending.operationId;
    const timeout = window.setTimeout(() => {
      setVisiblyPendingOperationId(operationId);
    }, uiDurations.slow);
    return () => { window.clearTimeout(timeout); };
  }, [pending]);

  const pendingVisualState = (baseDisabled = controlsDisabled) =>
    commandPending && !baseDisabled && !showPendingCommandDisabled
      ? "deferred"
      : undefined;

  useEffect(() => {
    if (target !== "powerpoint" || activationRequestId === 0) {
      return;
    }

    if (!verifiedPowerPoint ||
        capability?.powerPoint?.foregroundActivationSupported !== true ||
        selectedPowerPoint === null) {
      return;
    }

    if (foregroundedPresentationRequestRef.current === activationRequestId) {
      return;
    }

    foregroundedPresentationRequestRef.current = activationRequestId;
    onCommand("powerpoint", "activate", {
      runtimePresentationId: selectedPowerPoint.runtimePresentationId
    });
    onActivationRequestHandled?.();
  }, [
    activationRequestId,
    capability?.powerPoint?.foregroundActivationSupported,
    onActivationRequestHandled,
    onCommand,
    selectedPowerPoint,
    selectedPowerPoint?.runtimePresentationId,
    target,
    verifiedPowerPoint
  ]);

  useEffect(() => {
    onSessionActiveChange?.(reportedSessionActive);
  }, [onSessionActiveChange, reportedSessionActive]);

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

  const request = (action: PresentationAction, options?: boolean | PresentationCommandOptions) => {
    const runtimeOptions = target === "powerpoint" &&
      (powerPointOptions !== undefined || options !== undefined)
      ? {
          ...powerPointOptions,
          ...(typeof options === "boolean" ? { enabled: options } : options)
        }
      : options;
    if (runtimeOptions === undefined) {
      onCommand(target, action);
    } else {
      onCommand(target, action, runtimeOptions);
    }
  };
  const previousSlide = () => {
    request("previous");
    if (!usesAuthoritativePowerPointSession) {
      timer.changeSlide("previous", target);
    }
  };
  const nextSlide = () => {
    request("next");
    if (!usesAuthoritativePowerPointSession) {
      timer.changeSlide("next", target);
    }
  };
  const startSlideshow = () => {
    if (target === "powerpoint" && selectedSavedPowerPoint !== null) {
      onPowerPointLaunch?.(selectedSavedPowerPoint.presentationId);
      return;
    }
    request("start");
    if (!usesAuthoritativePowerPointSession) {
      timer.startSlideshow(target);
    }
  };
  const startSlideshowFromCurrent = () => {
    request("start-current");
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
    if (!usesAuthoritativePowerPointSession && timer.sessionStartedAt !== null) {
      timer.requestEnd();
    }
  };
  const goToSlide = (requestedSlideNumber?: number) => {
    const slideNumber = requestedSlideNumber ?? Number(gotoSlideNumber);
    if (!Number.isInteger(slideNumber) ||
        slideNumber < 1 ||
        slideNumber > maximumDirectSlideNumber ||
        (selectedPowerPoint !== null && slideNumber > selectedPowerPoint.slideCount)) {
      return;
    }

    request("goto", { slideNumber });
    setIsNavigationOpen(false);
  };
  const toggleNavigation = () => {
    setIsNavigationOpen((current) => {
      const next = !current;
      if (next) {
        setGotoSlideNumber(String(selectedPowerPoint?.currentSlideIndex ?? 1));
      }
      return next;
    });
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
  const commitPowerPointSelection = (selection: PowerPointChooserSelection) => {
    if (selection?.kind === "open") {
      setRuntimePresentationId(selection.id);
      setSavedPresentationId(null);
    } else if (selection?.kind === "saved") {
      setSavedPresentationId(selection.id);
      setRuntimePresentationId(null);
    }
  };

  if (target === "powerpoint" &&
      isPowerPointChooserOpen &&
      capability?.powerPoint) {
    return (
      <PowerPointPresentationChooser
        appLaunchAction={powerPointAppLaunchAction}
        appLaunchResult={powerPointAppLaunchResult}
        appLaunchPending={pendingPowerPointAppLaunch}
        capability={capability.powerPoint}
        launchPending={pendingPowerPointLaunch !== null}
        launchResult={powerPointLaunchResult}
        lockedMessage={presentationChangeLockedMessage}
        onBack={() => {
          if (presentationChangeLockedMessage === null) {
            commitPowerPointSelection(powerPointChooserSelection);
          }
          setIsPowerPointChooserOpen(false);
        }}
        onLaunchApp={onPowerPointAppLaunch}
        onLaunchSaved={(presentationId) => {
          commitPowerPointSelection({ kind: "saved", id: presentationId });
          onPowerPointLaunch?.(presentationId);
        }}
        onRefresh={onPowerPointRefresh}
        onSelectOpen={(runtimeId) => {
          commitPowerPointSelection({ kind: "open", id: runtimeId });
          setPowerPointChooserSelection(null);
          setIsPowerPointChooserOpen(false);
        }}
        onSelectionChange={setPowerPointChooserSelection}
        refreshPending={powerPointRefreshPending}
        selection={powerPointChooserSelection}
      />
    );
  }

  return (
    <section
      className={`presentation-mode${isTrackpadExpanded ? " trackpad-open" : ""}${isTimerExpanded ? " timer-open" : ""}${isTrackpadFullscreen ? " trackpad-fullscreen" : ""}`}
      aria-labelledby="presentation-title"
    >
      <div className="presentation-controls-panel" aria-busy={pending !== null}>
        <header className="presentation-header">
          <div>
            <div className="presentation-title-row">
              <h1 id="presentation-title">Presentation</h1>
              <InfoButton
                description={verifiedPowerPoint
                  ? "Choose an open PowerPoint presentation. Voltura Air verifies and controls that slideshow directly."
                  : "Choose the active presentation app, then keep that app focused on the PC."}
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
        {isTrackpadExpanded && (
          <div className="presentation-trackpad-summary" aria-label="Current presentation">
            <div className="presentation-trackpad-summary-details">
              <strong title={selectedPowerPoint?.name ?? targetLabel}>
                {selectedPowerPoint?.name ?? targetLabel}
              </strong>
              <span>
                {selectedPowerPoint?.state === "presenting"
                  ? `Slide ${selectedPowerPoint.currentSlideIndex ?? "–"} of ${selectedPowerPoint.slideCount}`
                  : powerPointSession?.state === "tracking"
                    ? formatPresentationTime(powerPointSession.elapsedSeconds)
                  : "Trackpad active"}
              </span>
            </div>
            <div className="presentation-navigation presentation-trackpad-summary-navigation">
              <button type="button" aria-label="Previous slide" data-pending-visual={pendingVisualState(navigationControlDisabled)} disabled={commandPending || navigationControlDisabled} onClick={previousSlide}>
                <ChevronLeft aria-hidden="true" /><span>Previous</span>
              </button>
              <button type="button" className="primary" aria-label="Next slide" data-pending-visual={pendingVisualState(navigationControlDisabled)} disabled={commandPending || navigationControlDisabled} onClick={nextSlide}>
                <span>Next</span><ChevronRight aria-hidden="true" />
              </button>
            </div>
          </div>
        )}
        {!supported && <p className="presentation-permission-message" role="alert">Update the Windows host to use Presentation mode.</p>}
        {supported && !capability.canControl && <p className="presentation-permission-message" role="alert">Presentation control is blocked by the host. Enable its global or per-device permission.</p>}
        {supported && capability.canControl && !blackoutAvailable && <p id="presentation-blackout-disabled" className="presentation-permission-message" role="alert">Blackout is disabled by the host. Enable its power permission for this device.</p>}
        <div className="presentation-control-columns">
          <div className="presentation-control-primary">
        {target === "powerpoint" && hasPowerPointAutomation && capability?.powerPoint && (
          <div className="presentation-powerpoint-source">
            <div className="presentation-powerpoint-summary">
              <div>
                <strong title={selectedPowerPoint?.name ?? selectedSavedPowerPoint?.title}>
                  {selectedPowerPoint?.name ??
                    selectedSavedPowerPoint?.title ??
                    "No presentation selected"}
                </strong>
                <span aria-live="polite">
                  {pendingPowerPointLaunch
                    ? "Opening saved presentation…"
                    : selectedPowerPoint?.state === "presenting"
                      ? `Slide ${selectedPowerPoint.currentSlideIndex ?? "–"} of ${selectedPowerPoint.slideCount} · Presenting`
                      : selectedPowerPoint
                        ? `${selectedPowerPoint.slideCount} slides · Ready`
                        : selectedSavedPowerPoint
                          ? "Ready to start"
                        : verifiedPowerPoint
                          ? "Choose an open or saved deck"
                          : "PowerPoint unavailable"}
                </span>
              </div>
              <button
                type="button"
                disabled={!connected || pendingPowerPointLaunch !== null}
                onClick={() => {
                  setPowerPointChooserSelection(selectedPowerPoint
                    ? { kind: "open", id: selectedPowerPoint.runtimePresentationId }
                    : selectedSavedPowerPoint
                      ? { kind: "saved", id: selectedSavedPowerPoint.presentationId }
                      : null);
                  setIsPowerPointChooserOpen(true);
                }}
              >
                {selectedPowerPoint || selectedSavedPowerPoint ? "Change" : "Choose"}
              </button>
            </div>
            {powerPointLaunchResult?.succeeded === false &&
              powerPointLaunchResult.presentationId === savedPresentationId && (
                <p className="presentation-permission-message" role="alert">
                  {powerPointLaunchResult.message}
                </p>
              )}
            {powerPointSession && powerPointSession.state !== "inactive" && (
              <div className="presentation-authoritative-session" role="status">
                <strong>
                  {powerPointSession.state === "pending-review"
                    ? "Session paused"
                    : powerPointSession.breakActive ? "Break" : "Tracking"}
                </strong>
                <span>
                  {formatPresentationTime(
                    powerPointSession.breakActive
                      ? powerPointSession.breakElapsedSeconds
                      : powerPointSession.elapsedSeconds)}
                  {powerPointSession.currentSlideIndex
                    ? ` · Slide ${powerPointSession.currentSlideIndex} of ${powerPointSession.slideCount}`
                    : ""}
                </span>
                {powerPointSession.state === "tracking" && powerPointSession.isOwner && (
                  <div className="presentation-tracking-actions">
                    <button
                      type="button"
                      className="primary"
                      disabled={!connected || sessionPending || onSessionCommand === undefined}
                      onClick={() => {
                        onSessionCommand?.("break", {
                          enabled: !powerPointSession.breakActive
                        });
                      }}
                    >
                      {powerPointSession.breakActive ? "Resume presentation" : "Start break"}
                    </button>
                    {capability?.powerPoint?.foregroundActivationSupported === true &&
                      selectedPowerPoint && (
                        <button
                          type="button"
                          disabled={!connected || commandPending}
                          title="Bring PowerPoint to the front"
                          onClick={() => { request("activate"); }}
                        >
                          Focus PPT
                        </button>
                      )}
                  </div>
                )}
                {powerPointSession.state === "pending-review" && powerPointSession.isOwner && (
                  <div className="presentation-session-actions">
                    <button
                      type="button"
                      className="primary"
                      disabled={
                        !connected ||
                        commandPending ||
                        (selectedPowerPoint?.currentSlideIndex === null ||
                         selectedPowerPoint?.currentSlideIndex === undefined) ||
                        selectedPowerPoint.runtimePresentationId !==
                          powerPointSession.runtimePresentationId
                      }
                      onClick={startSlideshowFromCurrent}
                    >
                      Continue presentation
                    </button>
                    <button type="button" disabled={!connected || sessionPending || onSessionCommand === undefined} onClick={() => { onSessionCommand?.("save"); }}>Save</button>
                    <button type="button" disabled={!connected || sessionPending || onSessionCommand === undefined} onClick={() => { onSessionCommand?.("discard"); }}>Discard</button>
                  </div>
                )}
              </div>
            )}
            {selectedPowerPoint?.state === "presenting" &&
              powerPointSession?.state === "inactive" && (
                <button
                  type="button"
                  disabled={controlsDisabled}
                  onClick={() => {
                    onSessionCommand?.("start", {
                      runtimePresentationId: selectedPowerPoint.runtimePresentationId
                    });
                  }}
                >
                  <Timer aria-hidden="true" /><span>Start tracking</span>
                </button>
              )}
          </div>
        )}

        <div className="presentation-navigation">
          <button type="button" data-pending-visual={pendingVisualState(navigationControlDisabled)} disabled={commandPending || navigationControlDisabled} onClick={previousSlide}>
            <ChevronLeft aria-hidden="true" />
            <span>Previous</span>
          </button>
          <button type="button" className="primary" data-pending-visual={pendingVisualState(navigationControlDisabled)} disabled={commandPending || navigationControlDisabled} onClick={nextSlide}>
            <span>Next</span>
            <ChevronRight aria-hidden="true" />
          </button>
        </div>
          </div>

          <div className="presentation-control-secondary">
        <div className="presentation-actions">
          {target === "powerpoint" && (
            selectedSavedPowerPoint ? (
              <button
                type="button"
                data-pending-visual={pendingPowerPointLaunch !== null ? "deferred" : undefined}
                disabled={savedPowerPointStartDisabled}
                onClick={startSlideshow}
              >
                <Play aria-hidden="true" /><span>Start slideshow</span>
              </button>
            ) : verifiedPowerPoint ? <>
              <button
                type="button"
                data-pending-visual={pendingVisualState(powerPointStartControlDisabled)}
                disabled={commandPending || powerPointStartControlDisabled}
                onClick={startSlideshow}
              >
                <Play aria-hidden="true" /><span>Start from beginning</span>
              </button>
              <button
                type="button"
                data-pending-visual={pendingVisualState(powerPointStartControlDisabled)}
                disabled={commandPending || powerPointStartControlDisabled}
                onClick={startSlideshowFromCurrent}
              >
                <Play aria-hidden="true" /><span>Start from current</span>
              </button>
              <button
                type="button"
                aria-expanded={isNavigationOpen}
                data-pending-visual={pendingVisualState(powerPointReadyControlDisabled)}
                disabled={commandPending || powerPointReadyControlDisabled}
                onClick={toggleNavigation}
              >
                <ListOrdered aria-hidden="true" /><span>Go to slide</span>
              </button>
              <button
                type="button"
                title="Pause or resume PowerPoint's automatic slide advancement."
                data-pending-visual={pendingVisualState(controlsDisabled || !powerPointRunning)}
                disabled={commandControlsDisabled || !powerPointRunning}
                onClick={() => { request("pause", { enabled: selectedPowerPoint?.slideShowState !== "paused" }); }}
              >
                {selectedPowerPoint?.slideShowState === "paused"
                  ? <Play aria-hidden="true" />
                  : <Pause aria-hidden="true" />}
                <span>{selectedPowerPoint?.slideShowState === "paused" ? "Resume auto-play" : "Pause auto-play"}</span>
              </button>
            </> : (
              <button
                type="button"
                data-pending-visual={pendingVisualState()}
                disabled={hasPowerPointAutomation || commandControlsDisabled}
                onClick={startSlideshow}
              >
                <Play aria-hidden="true" /><span>Start slideshow</span>
              </button>
            )
          )}
          <button type="button" data-pending-visual={pendingVisualState(endControlDisabled)} disabled={commandPending || endControlDisabled} onClick={endSlideshow}>
            <CircleStop aria-hidden="true" /><span>End slideshow</span>
          </button>
          {target === "powerpoint" && verifiedPowerPoint ? (
            <>
              <button
                type="button"
                className={selectedPowerPoint?.slideShowState === "black" ? "active" : undefined}
                data-pending-visual={pendingVisualState(powerPointReadyControlDisabled)}
                disabled={commandPending || powerPointReadyControlDisabled}
                onClick={() => { request("black"); }}
              >
                <Eclipse aria-hidden="true" /><span>Black screen</span>
              </button>
              <button
                type="button"
                className={selectedPowerPoint?.slideShowState === "white" ? "active" : undefined}
                data-pending-visual={pendingVisualState(powerPointReadyControlDisabled)}
                disabled={commandPending || powerPointReadyControlDisabled}
                onClick={() => { request("white"); }}
              >
                <Eclipse aria-hidden="true" /><span>White screen</span>
              </button>
              {powerPointBlank && (
                <button
                  type="button"
                  className="presentation-return-slides"
                  data-pending-visual={pendingVisualState()}
                  disabled={commandControlsDisabled}
                  onClick={() => {
                    request(selectedPowerPoint?.slideShowState === "black" ? "black" : "white");
                  }}
                >
                  <Play aria-hidden="true" /><span>Return to slides</span>
                </button>
              )}
            </>
          ) : target !== "pdf" && (
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
            data-pending-visual={pendingVisualState(laserControlDisabled)}
            disabled={(commandPending && !laserPointerActive) || laserControlDisabled}
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
          </div>
        </div>

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
                onToggleFullscreen: () => {
                  if (isTrackpadFullscreen) {
                    setIsTrackpadFullscreen(false);
                    setIsTrackpadExpanded(false);
                    return;
                  }

                  setIsTrackpadFullscreen(true);
                }
              })}
            </div>
          )}
        </aside>

        {!usesAuthoritativePowerPointSession && (
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
                onPause={() => {
                  timer.pause();
                  if (target === "powerpoint" && powerPointSession?.state === "tracking") {
                    onSessionCommand?.("break", { enabled: true });
                  }
                }}
                onResume={() => {
                  timer.start(target);
                  if (target === "powerpoint" && powerPointSession?.state === "tracking") {
                    onSessionCommand?.("break", { enabled: false });
                  }
                }}
                presentationSessionCount={timer.presentationSessionCount}
                sessionStartedAt={timer.sessionStartedAt}
                sessionTarget={timer.sessionTarget}
                slides={timer.slides}
                totalBreakSeconds={timer.totalBreakSeconds}
              />
            </div>
            {timer.milestoneMessage && <p className="presentation-milestone" role="status" aria-live="polite">{timer.milestoneMessage}</p>}
            <div className="presentation-timer-actions">
              {!timer.isRunning && !timer.isResetPending && <button type="button" className="primary" onClick={() => {
                timer.start(target);
                if (target === "powerpoint" && powerPointSession?.state === "tracking") {
                  onSessionCommand?.("break", { enabled: false });
                }
              }}><Play aria-hidden="true" /><span>{timer.elapsedSeconds > 0 ? "Resume" : "Start"}</span></button>}
              {timer.isRunning && (
                <button
                  type="button"
                  className="primary"
                  disabled={!timer.canPause}
                  aria-describedby={!timer.canPause ? "presentation-break-limit" : undefined}
                  onClick={() => {
                    timer.pause();
                    if (target === "powerpoint" && powerPointSession?.state === "tracking") {
                      onSessionCommand?.("break", { enabled: true });
                    }
                  }}
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
        )}
      </div>

      <ModalDialog
        actions={(
          <>
            <button type="button" data-pending-visual={pendingVisualState()} disabled={commandControlsDisabled} onClick={() => { goToSlide(1); }}>
              First
            </button>
            <button type="button" className="primary" data-pending-visual={pendingVisualState()} disabled={commandControlsDisabled} onClick={() => { goToSlide(directSlideValue); }}>
              Go to {directSlideValue}
            </button>
            <button type="button" data-pending-visual={pendingVisualState()} disabled={commandControlsDisabled} onClick={() => { goToSlide(directSlideMaximum); }}>
              Last
            </button>
          </>
        )}
        actionsClassName="presentation-navigation-dialog-actions"
        className="presentation-navigation-dialog"
        dismissLabel="Close"
        isOpen={target === "powerpoint" && isNavigationOpen}
        onClose={() => { setIsNavigationOpen(false); }}
        title="Go to slide"
      >
        <p>Drag and release to navigate.</p>
        <label className="presentation-slide-range">
          <output htmlFor="presentation-slide-range">
            Slide <strong>{directSlideValue}</strong> of {directSlideMaximum}
          </output>
          <div className="range-row">
            <input
              id="presentation-slide-range"
              type="range"
              aria-label="Slide number"
              min="1"
              max={directSlideMaximum}
              step="1"
              value={directSlideValue}
              onChange={(event) => { setGotoSlideNumber(event.target.value); }}
              onPointerUp={(event) => { goToSlide(Number(event.currentTarget.value)); }}
              onKeyUp={(event) => {
                if (event.key === "Enter") {
                  goToSlide(Number(event.currentTarget.value));
                }
              }}
            />
            <output htmlFor="presentation-slide-range">{directSlideValue}</output>
          </div>
          <span className="presentation-slide-range-limits">
            <span>1</span><span>{directSlideMaximum}</span>
          </span>
        </label>
      </ModalDialog>

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
