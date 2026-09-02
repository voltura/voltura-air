import { Share2, X } from "lucide-react";
import type { TouchEvent } from "react";
import {
  screenViewRecordingMaximumDurationMs,
  type ScreenViewRecordingPresentation,
} from "./screenViewRecording";

interface Props {
  presentation: ScreenViewRecordingPresentation;
  onDiscard: () => void;
  onSave: () => void;
}

export function ScreenViewRecordingPanel({ presentation, onDiscard, onSave }: Props) {
  if (presentation.phase === "idle" || presentation.phase === "error") {
    return null;
  }
  const elapsed = formatDuration(presentation.elapsedMs);
  const detail =
    presentation.phase === "recording"
      ? `${presentation.includesSound ? "Video with sound" : "Video only"} · ${elapsed} / 5:00`
      : presentation.message;

  return (
    <div
      className="screen-view-recording-panel"
      role="status"
      onTouchStart={isolateTouch}
      onTouchMove={isolateTouch}
      onTouchEnd={isolateTouch}
      onTouchCancel={isolateTouch}
    >
      <div>
        <strong>{presentation.fileName}</strong>
        <span>{detail}</span>
      </div>
      {presentation.phase === "recording" && (
        <progress max={screenViewRecordingMaximumDurationMs} value={presentation.elapsedMs} />
      )}
      {presentation.phase === "ready" ? (
        <div className="screen-view-recording-actions">
          <button type="button" onClick={onSave}>
            <Share2 aria-hidden="true" /> Save / Share
          </button>
          <button
            type="button"
            className="screen-view-recording-icon-action"
            aria-label="Discard recording"
            title="Discard"
            onClick={onDiscard}
          >
            <X aria-hidden="true" />
          </button>
        </div>
      ) : presentation.phase === "finalizing" ? (
        <button
          type="button"
          className="screen-view-recording-icon-action"
          aria-label="Discard recording"
          title="Discard"
          onClick={onDiscard}
        >
          <X aria-hidden="true" />
        </button>
      ) : null}
    </div>
  );
}

function formatDuration(milliseconds: number): string {
  const totalSeconds = Math.max(0, Math.min(300, Math.floor(milliseconds / 1_000)));
  return `${Math.floor(totalSeconds / 60)}:${(totalSeconds % 60).toString().padStart(2, "0")}`;
}

function isolateTouch(event: TouchEvent<HTMLElement>) {
  event.stopPropagation();
}
