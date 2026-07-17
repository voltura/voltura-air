import { CornerDownLeft, FastForward, Layers, Maximize2, Pause, Play, Power, Rewind, SkipBack, SkipForward, SquareX } from "lucide-react";
import type { RemoteModeCopy } from "./remoteModeCopy";
import { RemoteButton, type RepeatablePressProps } from "./RemoteButton";

interface RemoteMediaSectionProps {
  getRepeatablePressProps: (action: () => void) => RepeatablePressProps;
  isKodiMode: boolean;
  modeCopy: RemoteModeCopy;
  onAppFullscreen: () => void;
  onBack: () => void;
  onBrowserFullscreen: () => void;
  onNext: () => void;
  onPlayPause: () => void;
  onPowerMenu: () => void;
  onPrevious: () => void;
  onSeekBackward: () => void;
  onSeekForward: () => void;
  onSpace: () => void;
  onStopPlayback: () => void;
}

export function RemoteMediaSection({
  getRepeatablePressProps,
  isKodiMode,
  modeCopy,
  onAppFullscreen,
  onBack,
  onBrowserFullscreen,
  onNext,
  onPlayPause,
  onPowerMenu,
  onPrevious,
  onSeekBackward,
  onSeekForward,
  onSpace,
  onStopPlayback
}: RemoteMediaSectionProps) {
  return (
    <div className="remote-section remote-media-section">
      <div className="remote-section-title">
        <span>Media</span>
        <small>{modeCopy.media}</small>
      </div>
      <div className="remote-primary-grid" aria-label="Media controls">
        <RemoteButton label="Previous track" onClick={onPrevious}>
          <SkipBack aria-hidden="true" />
          <span>Prev</span>
        </RemoteButton>
        <RemoteButton label="Play or pause" onClick={onPlayPause}>
          <span className="remote-play-pause-icons" aria-hidden="true"><Play /><Pause /></span>
          <span>Play/Pause</span>
        </RemoteButton>
        <RemoteButton label="Next track" onClick={onNext}>
          <SkipForward aria-hidden="true" />
          <span>Next</span>
        </RemoteButton>
        <RemoteButton label="Seek backward" title={isKodiMode ? undefined : modeCopy.seekBackwardTitle} pressProps={getRepeatablePressProps(onSeekBackward)}>
          <Rewind aria-hidden="true" />
          <span>Seek -</span>
        </RemoteButton>
        {isKodiMode ? (
          <RemoteButton label="Stop playback" className="remote-icon-button" onClick={onStopPlayback}>
            <SquareX aria-hidden="true" />
            <span>Stop</span>
          </RemoteButton>
        ) : (
          <RemoteButton label="Space" title={modeCopy.spaceTitle} onClick={onSpace}><span>Space</span></RemoteButton>
        )}
        <RemoteButton label="Seek forward" title={isKodiMode ? undefined : modeCopy.seekForwardTitle} pressProps={getRepeatablePressProps(onSeekForward)}>
          <FastForward aria-hidden="true" />
          <span>Seek +</span>
        </RemoteButton>
        <RemoteButton label="Esc or back" onClick={onBack}>
          <CornerDownLeft aria-hidden="true" />
          <span>Esc / Back</span>
        </RemoteButton>
        <RemoteButton label={modeCopy.appFullscreenLabel} title={isKodiMode ? undefined : modeCopy.appFullscreenTitle} onClick={onAppFullscreen}>
          {isKodiMode ? <Layers aria-hidden="true" /> : <Maximize2 aria-hidden="true" />}
          <span>{modeCopy.appFullscreenLabel}</span>
        </RemoteButton>
        {isKodiMode ? (
          <RemoteButton label="Power menu" className="remote-icon-button" onClick={onPowerMenu}>
            <Power aria-hidden="true" />
            <span>Power options</span>
          </RemoteButton>
        ) : (
          <RemoteButton label="Browser fullscreen" title={modeCopy.browserFullscreenTitle} onClick={onBrowserFullscreen}>
            <Maximize2 aria-hidden="true" />
            <span>Browser Full</span>
          </RemoteButton>
        )}
      </div>
    </div>
  );
}
