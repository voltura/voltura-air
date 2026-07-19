import { ChevronDown, ChevronLeft, ChevronRight, CircleStop, Eclipse, MousePointer2, Pause, Play, RotateCcw, Timer, Vibrate } from "lucide-react";
import { useState } from "react";
import "./presentation.css";
import type { PendingPresentationCommand } from "../../../foundation/connection/usePresentationControl";
import type { PresentationAction, PresentationCapability, PresentationCommandResultMessage, PresentationTarget, SystemPowerAction } from "../../../foundation/protocol/messages";
import { formatPresentationTime, usePresentationTimer } from "./presentationTimer";
import { InfoButton } from "../../../ui/overlays/InfoButton";

interface PresentationModeProps {
  blackoutAvailable: boolean;
  capability: PresentationCapability | undefined;
  connected: boolean;
  pending: PendingPresentationCommand | null;
  pendingPowerAction: SystemPowerAction | null;
  result: PresentationCommandResultMessage | null;
  onCommand: (target: PresentationTarget, action: PresentationAction) => void;
  onPowerAction?: (action: SystemPowerAction) => void;
}

const targetOptions = [
  { id: "powerpoint", label: "PowerPoint" },
  { id: "google-slides", label: "Google Slides" },
  { id: "pdf", label: "PDF / browser" }
] satisfies { id: PresentationTarget; label: string }[];

export function PresentationMode({ blackoutAvailable, capability, connected, pending, pendingPowerAction, result, onCommand, onPowerAction }: PresentationModeProps) {
  const [target, setTarget] = useState<PresentationTarget>("powerpoint");
  const [isTargetSelectorOpen, setIsTargetSelectorOpen] = useState(false);
  const [isTimerExpanded, setIsTimerExpanded] = useState(true);
  const timer = usePresentationTimer();
  const supported = capability !== undefined;
  const canControl = connected && capability?.canControl === true;
  const controlsDisabled = !canControl || pending !== null;
  const blackoutDisabled = controlsDisabled || !blackoutAvailable || pendingPowerAction !== null || !onPowerAction;
  const targetLabel = targetOptions.find((option) => option.id === target)?.label ?? target;

  const request = (action: PresentationAction) => { onCommand(target, action); };
  const selectTarget = (nextTarget: PresentationTarget) => {
    setTarget(nextTarget);
    setIsTargetSelectorOpen(false);
  };

  return (
    <section className="presentation-mode" aria-labelledby="presentation-title">
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
          <button type="button" disabled={controlsDisabled} onClick={() => { request("previous"); }}>
            <ChevronLeft aria-hidden="true" />
            <span>Previous</span>
          </button>
          <button type="button" className="primary" disabled={controlsDisabled} onClick={() => { request("next"); }}>
            <span>Next</span>
            <ChevronRight aria-hidden="true" />
          </button>
        </div>

        <div className="presentation-actions">
          {target === "powerpoint" && (
            <button type="button" disabled={controlsDisabled} onClick={() => { request("start"); }}>
              <Play aria-hidden="true" /><span>Start slideshow</span>
            </button>
          )}
          <button type="button" disabled={controlsDisabled} onClick={() => { request("end"); }}>
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
          {target !== "pdf" && (
            <button type="button" disabled={controlsDisabled} onClick={() => { request("pointer"); }}>
              <MousePointer2 aria-hidden="true" /><span>Laser pointer</span>
            </button>
          )}
        </div>

        {result && !result.succeeded && (
          <div className="presentation-result error" role="alert" aria-live="polite">
            {result.message}
          </div>
        )}
      </div>

      <aside className="presentation-timer" aria-labelledby="presentation-timer-title">
        <button
          className="presentation-timer-heading"
          type="button"
          aria-expanded={isTimerExpanded}
          aria-controls="presentation-timer-content"
          onClick={() => { setIsTimerExpanded((current) => !current); }}
        >
          <Timer aria-hidden="true" />
          <h2 id="presentation-timer-title">{timer.isPaused ? "Break timer" : "Timer"}</h2>
          <ChevronDown aria-hidden="true" />
        </button>
        {isTimerExpanded && (
          <div className="presentation-timer-content" id="presentation-timer-content">
            <output className={`presentation-time${timer.isPaused ? " break-time" : ""}`} aria-label={timer.isPaused ? "Elapsed break time" : "Elapsed presentation time"}>{formatPresentationTime(timer.isPaused ? timer.breakElapsedSeconds : timer.elapsedSeconds)}</output>
            {timer.milestoneMessage && <p className="presentation-milestone" role="status" aria-live="polite">{timer.milestoneMessage}</p>}
            <div className="presentation-timer-actions">
              {!timer.isRunning && <button type="button" className="primary" onClick={timer.start}><Play aria-hidden="true" /><span>{timer.elapsedSeconds > 0 ? "Resume" : "Start"}</span></button>}
              {timer.isRunning && <button type="button" className="primary" onClick={timer.pause}><Pause aria-hidden="true" /><span>Pause</span></button>}
              <div className="presentation-reset-control">
                {timer.isPaused && <span className="presentation-static-time" aria-label="Presentation time during break">{formatPresentationTime(timer.elapsedSeconds)}</span>}
                <button type="button" disabled={!timer.isRunning && timer.elapsedSeconds === 0} onClick={timer.reset}><RotateCcw aria-hidden="true" /><span>Reset</span></button>
              </div>
            </div>
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
            <p className="presentation-timer-note">The timer stays on this device and resets when this page reloads.</p>
          </div>
        )}
      </aside>
    </section>
  );
}
