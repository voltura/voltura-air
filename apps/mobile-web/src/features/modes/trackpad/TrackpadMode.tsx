import { useRef } from "react";
import { Maximize2, Minimize2, MousePointer2, Volume2, VolumeX } from "lucide-react";
import type { TrackpadSettings } from "../../../gestures";
import type { AudioStateMessage } from "../../../protocol";

type MouseButtonName = "left" | "right";

interface TrackpadModeProps {
  audioState: AudioStateMessage | null;
  isExpanded: boolean;
  supportsVolumeControl: boolean;
  trackpadSettings: TrackpadSettings;
  onSetVolume: (volume: number) => void;
  onToggleExpanded: () => void;
  onToggleMute: () => void;
  onTouchCancel: (event: React.TouchEvent<HTMLDivElement>) => void;
  onTouchEnd: (event: React.TouchEvent<HTMLDivElement>) => void;
  onTouchMove: (event: React.TouchEvent<HTMLDivElement>) => void;
  onTouchStart: (event: React.TouchEvent<HTMLDivElement>) => void;
  onMouseButtonDown: (button: MouseButtonName) => void;
  onMouseButtonUp: (button: MouseButtonName) => void;
}

export function TrackpadMode({
  audioState,
  isExpanded,
  supportsVolumeControl,
  trackpadSettings,
  onSetVolume,
  onToggleExpanded,
  onToggleMute,
  onTouchCancel,
  onTouchEnd,
  onTouchMove,
  onTouchStart,
  onMouseButtonDown,
  onMouseButtonUp
}: TrackpadModeProps) {
  const activeButtonPointers = useRef(new Map<number, MouseButtonName>());
  const stopTouchPropagation = (event: React.TouchEvent<HTMLButtonElement>) => {
    event.stopPropagation();
  };
  const stopContextMenu = (event: React.MouseEvent) => {
    event.preventDefault();
  };
  const clickButtons = trackpadSettings.leftHandedButtons
    ? [
        { label: "Right", button: "right" as const },
        { label: "Left", button: "left" as const }
      ]
    : [
        { label: "Left", button: "left" as const },
        { label: "Right", button: "right" as const }
      ];
  const showVolumeControl = !isExpanded && supportsVolumeControl && trackpadSettings.showVolumeControl && audioState !== null;

  const pressMouseButton = (event: React.PointerEvent<HTMLButtonElement>, button: MouseButtonName) => {
    event.preventDefault();
    event.stopPropagation();
    activeButtonPointers.current.set(event.pointerId, button);
    onMouseButtonDown(button);
    try {
      event.currentTarget.setPointerCapture?.(event.pointerId);
    } catch {
      // Pointer capture is an enhancement for drag-to-hold. Some mobile
      // browsers expose the API but reject capture for touch pointers.
    }
  };

  const releaseMouseButton = (event: React.PointerEvent<HTMLButtonElement>) => {
    event.preventDefault();
    event.stopPropagation();
    const button = activeButtonPointers.current.get(event.pointerId);
    if (!button) {
      return;
    }

    activeButtonPointers.current.delete(event.pointerId);
    onMouseButtonUp(button);
    try {
      if (event.currentTarget.hasPointerCapture?.(event.pointerId)) {
        event.currentTarget.releasePointerCapture?.(event.pointerId);
      }
    } catch {
      // The pointer may already have been released by the browser.
    }
  };

  return (
    <section
      className={`trackpad-mode ${isExpanded ? "expanded" : ""} ${showVolumeControl ? "has-volume-control" : ""} ${trackpadSettings.largeClickButtons ? "large-click-buttons" : ""}`}
    >
      {showVolumeControl && (
        <div className={`volume-control ${audioState.muted ? "muted" : ""}`}>
          <button
            className="icon-button"
            type="button"
            aria-label={audioState.muted ? "Unmute PC" : "Mute PC"}
            title={audioState.muted ? "Unmute PC" : "Mute PC"}
            onClick={onToggleMute}
            onTouchStart={stopTouchPropagation}
            onTouchMove={stopTouchPropagation}
            onTouchEnd={stopTouchPropagation}
          >
            {audioState.muted ? <VolumeX aria-hidden="true" /> : <Volume2 aria-hidden="true" />}
          </button>
          <div className="range-row">
            <input
              aria-label="PC volume"
              type="range"
              min="0"
              max="100"
              step="1"
              value={audioState.volume}
              onChange={(event) => { onSetVolume(Number(event.target.value)); }}
            />
            <output>{audioState.volume}%</output>
          </div>
        </div>
      )}
      <div className="trackpad-surface" onContextMenu={stopContextMenu} onTouchCancel={onTouchCancel} onTouchStart={onTouchStart} onTouchMove={onTouchMove} onTouchEnd={onTouchEnd}>
        <button
          className="trackpad-expand-button"
          type="button"
          aria-label={isExpanded ? "Restore trackpad" : "Expand trackpad"}
          title={isExpanded ? "Restore trackpad" : "Expand trackpad"}
          onClick={(event) => {
            event.stopPropagation();
            onToggleExpanded();
          }}
          onTouchStart={(event) => { event.stopPropagation(); }}
          onTouchMove={(event) => { event.stopPropagation(); }}
          onTouchEnd={(event) => { event.stopPropagation(); }}
        >
          {isExpanded ? <Minimize2 aria-hidden="true" /> : <Maximize2 aria-hidden="true" />}
        </button>
        <MousePointer2 aria-hidden="true" />
        {isExpanded && (
          <div className="trackpad-click-zones" aria-label="Mouse buttons">
            {clickButtons.map((button) => (
              <button
                key={button.label}
                type="button"
                onPointerCancel={releaseMouseButton}
                onPointerDown={(event) => { pressMouseButton(event, button.button); }}
                onPointerUp={releaseMouseButton}
                onTouchEnd={stopTouchPropagation}
                onTouchMove={stopTouchPropagation}
                onTouchStart={stopTouchPropagation}
              >
                {button.label}
              </button>
            ))}
          </div>
        )}
      </div>
      {!isExpanded && (
        <div className="mouse-buttons">
          {clickButtons.map((button) => (
            <button
              key={button.label}
              onPointerCancel={releaseMouseButton}
              onPointerDown={(event) => { pressMouseButton(event, button.button); }}
              onPointerUp={releaseMouseButton}
              type="button"
            >
              <span>{button.label}</span>
            </button>
          ))}
        </div>
      )}
    </section>
  );
}
