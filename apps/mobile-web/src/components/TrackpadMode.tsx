import { Maximize2, Minimize2, MousePointer2, Volume2, VolumeX } from "lucide-react";
import type { TrackpadSettings } from "../gestures";
import type { AudioStateMessage } from "../protocol";

type TrackpadModeProps = {
  audioState: AudioStateMessage | null;
  isExpanded: boolean;
  supportsVolumeControl: boolean;
  trackpadSettings: TrackpadSettings;
  onSetVolume: (volume: number) => void;
  onToggleExpanded: () => void;
  onToggleMute: () => void;
  onTouchEnd: (event: React.TouchEvent<HTMLDivElement>) => void;
  onTouchMove: (event: React.TouchEvent<HTMLDivElement>) => void;
  onTouchStart: (event: React.TouchEvent<HTMLDivElement>) => void;
  onLeftClick: () => void;
  onRightClick: () => void;
};

export function TrackpadMode({
  audioState,
  isExpanded,
  supportsVolumeControl,
  trackpadSettings,
  onSetVolume,
  onToggleExpanded,
  onToggleMute,
  onTouchEnd,
  onTouchMove,
  onTouchStart,
  onLeftClick,
  onRightClick
}: TrackpadModeProps) {
  const stopTouchPropagation = (event: React.TouchEvent<HTMLButtonElement>) => {
    event.stopPropagation();
  };
  const showVolumeControl = !isExpanded && supportsVolumeControl && trackpadSettings.showVolumeControl && audioState !== null;

  return (
    <section className={`trackpad-mode ${isExpanded ? "expanded" : ""} ${showVolumeControl ? "has-volume-control" : ""}`}>
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
              onChange={(event) => onSetVolume(Number(event.target.value))}
            />
            <output>{audioState.volume}%</output>
          </div>
        </div>
      )}
      <div className="trackpad-surface" onTouchStart={onTouchStart} onTouchMove={onTouchMove} onTouchEnd={onTouchEnd}>
        <button
          className="trackpad-expand-button"
          type="button"
          aria-label={isExpanded ? "Restore trackpad" : "Expand trackpad"}
          title={isExpanded ? "Restore trackpad" : "Expand trackpad"}
          onClick={(event) => {
            event.stopPropagation();
            onToggleExpanded();
          }}
          onTouchStart={(event) => event.stopPropagation()}
          onTouchMove={(event) => event.stopPropagation()}
          onTouchEnd={(event) => event.stopPropagation()}
        >
          {isExpanded ? <Minimize2 aria-hidden="true" /> : <Maximize2 aria-hidden="true" />}
        </button>
        <MousePointer2 aria-hidden="true" />
        {isExpanded && (
          <div className="trackpad-click-zones" aria-label="Mouse buttons">
            <button type="button" onClick={onLeftClick} onTouchStart={stopTouchPropagation} onTouchMove={stopTouchPropagation} onTouchEnd={stopTouchPropagation}>
              Left
            </button>
            <button type="button" onClick={onRightClick} onTouchStart={stopTouchPropagation} onTouchMove={stopTouchPropagation} onTouchEnd={stopTouchPropagation}>
              Right
            </button>
          </div>
        )}
      </div>
      {!isExpanded && (
        <div className="mouse-buttons">
          <button onClick={onLeftClick}>
            <span>Left</span>
          </button>
          <button onClick={onRightClick}>
            <span>Right</span>
          </button>
        </div>
      )}
    </section>
  );
}
