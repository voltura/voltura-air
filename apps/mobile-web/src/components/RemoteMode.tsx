import {
  ArrowDown,
  ArrowLeft,
  ArrowRight,
  ArrowUp,
  CornerDownLeft,
  FastForward,
  Maximize2,
  Pause,
  Play,
  Rewind,
  Search,
  SkipBack,
  SkipForward,
  Volume2,
  VolumeX
} from "lucide-react";
import type { AudioStateMessage } from "../protocol";

const volumeStep = 8;

type RemoteModeProps = {
  audioState: AudioStateMessage | null;
  supportsVolumeControl: boolean;
  onSetVolume: (volume: number) => void;
  onToggleMute: () => void;
  sendSpecial: (key: string, modifiers?: string[]) => void;
};

type RemoteButtonProps = {
  label: string;
  onClick: () => void;
  children?: React.ReactNode;
  className?: string;
  disabled?: boolean;
  title?: string;
};

export function RemoteMode({ audioState, supportsVolumeControl, onSetVolume, onToggleMute, sendSpecial }: RemoteModeProps) {
  const canUseVolume = supportsVolumeControl && audioState !== null;

  const changeVolume = (delta: number) => {
    if (!canUseVolume) {
      return;
    }

    onSetVolume(Math.max(0, Math.min(100, audioState.volume + delta)));
  };

  return (
    <section className="remote-mode" aria-label="Couch remote">
      <div className="remote-section remote-media-section">
        <div className="remote-section-title">
          <span>Media</span>
          <small>Large controls for TV, browser video, music, and presentations.</small>
        </div>
        <div className="remote-primary-grid" aria-label="Media controls">
          <RemoteButton label="Previous track" onClick={() => sendSpecial("MediaPreviousTrack")}>
            <SkipBack aria-hidden="true" />
            <span>Prev</span>
          </RemoteButton>
          <RemoteButton label="Play or pause" className="primary" onClick={() => sendSpecial("MediaPlayPause")}>
            <Play aria-hidden="true" />
            <Pause aria-hidden="true" />
            <span>Play/Pause</span>
          </RemoteButton>
          <RemoteButton label="Next track" onClick={() => sendSpecial("MediaNextTrack")}>
            <SkipForward aria-hidden="true" />
            <span>Next</span>
          </RemoteButton>
          <RemoteButton label="Seek backward" title="Seek backward / left arrow" onClick={() => sendSpecial("ArrowLeft")}>
            <Rewind aria-hidden="true" />
            <span>Seek -</span>
          </RemoteButton>
          <RemoteButton label="Space" title="Space / common browser play-pause" onClick={() => sendSpecial("Space")}>
            <span>Space</span>
          </RemoteButton>
          <RemoteButton label="Seek forward" title="Seek forward / right arrow" onClick={() => sendSpecial("ArrowRight")}>
            <FastForward aria-hidden="true" />
            <span>Seek +</span>
          </RemoteButton>
          <RemoteButton label="Esc or back" onClick={() => sendSpecial("Escape")}>
            <CornerDownLeft aria-hidden="true" />
            <span>Esc / Back</span>
          </RemoteButton>
          <RemoteButton label="Fullscreen" onClick={() => sendSpecial("F11")}>
            <Maximize2 aria-hidden="true" />
            <span>Fullscreen</span>
          </RemoteButton>
        </div>
      </div>

      <div className="remote-section remote-volume-section">
        <div className="remote-section-title">
          <span>Volume</span>
          <small>{canUseVolume ? `${audioState.volume}%${audioState.muted ? " muted" : ""}` : "Volume control is disabled on the PC."}</small>
        </div>
        <div className="remote-volume-grid" aria-label="Volume controls">
          <RemoteButton label="Volume down" disabled={!canUseVolume} onClick={() => changeVolume(-volumeStep)}>
            <Volume2 aria-hidden="true" />
            <span>Vol -</span>
          </RemoteButton>
          <RemoteButton label={audioState?.muted ? "Unmute PC" : "Mute PC"} disabled={!canUseVolume} onClick={onToggleMute}>
            {audioState?.muted ? <VolumeX aria-hidden="true" /> : <Volume2 aria-hidden="true" />}
            <span>{audioState?.muted ? "Unmute" : "Mute"}</span>
          </RemoteButton>
          <RemoteButton label="Volume up" disabled={!canUseVolume} onClick={() => changeVolume(volumeStep)}>
            <Volume2 aria-hidden="true" />
            <span>Vol +</span>
          </RemoteButton>
        </div>
      </div>

      <div className="remote-section remote-navigation-section">
        <div className="remote-section-title">
          <span>Navigation</span>
          <small>D-pad for menus, players, slides, and full-screen dialogs.</small>
        </div>
        <div className="remote-dpad" aria-label="Directional pad">
          <button type="button" className="remote-dpad-up" aria-label="D-pad up" onClick={() => sendSpecial("ArrowUp")}>
            <ArrowUp aria-hidden="true" />
          </button>
          <button type="button" className="remote-dpad-left" aria-label="D-pad left" onClick={() => sendSpecial("ArrowLeft")}>
            <ArrowLeft aria-hidden="true" />
          </button>
          <button type="button" className="remote-dpad-ok" onClick={() => sendSpecial("Enter")}>
            OK
          </button>
          <button type="button" className="remote-dpad-right" aria-label="D-pad right" onClick={() => sendSpecial("ArrowRight")}>
            <ArrowRight aria-hidden="true" />
          </button>
          <button type="button" className="remote-dpad-down" aria-label="D-pad down" onClick={() => sendSpecial("ArrowDown")}>
            <ArrowDown aria-hidden="true" />
          </button>
        </div>
      </div>

      <div className="remote-section remote-utility-section">
        <div className="remote-section-title">
          <span>Windows</span>
          <small>Fast helper keys for couch use.</small>
        </div>
        <div className="remote-utility-grid" aria-label="Windows helper controls">
          <RemoteButton label="Start or search" onClick={() => sendSpecial("Win")}>
            <Search aria-hidden="true" />
            <span>Start/Search</span>
          </RemoteButton>
          <RemoteButton label="Alt Tab" onClick={() => sendSpecial("Tab", ["Alt"])}>
            <span>Alt Tab</span>
          </RemoteButton>
          <RemoteButton label="Browser back" onClick={() => sendSpecial("BrowserBack")}>
            <CornerDownLeft aria-hidden="true" />
            <span>Browser Back</span>
          </RemoteButton>
        </div>
      </div>
    </section>
  );
}

function RemoteButton({ label, onClick, children, className, disabled = false, title }: RemoteButtonProps) {
  return (
    <button type="button" aria-label={label} className={className} disabled={disabled} title={title} onClick={onClick}>
      {children ?? <span>{label}</span>}
    </button>
  );
}
