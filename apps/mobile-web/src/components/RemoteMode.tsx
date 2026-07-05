import { useEffect, useRef, useState, type ButtonHTMLAttributes, type ReactNode } from "react";
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
import type { RemoteModeId, RemoteSettings } from "../remoteSettings";

const repeatStartDelayMs = 400;
const repeatIntervalMs = 55;
const miniTrackpadSensitivity = 1.35;
const miniTrackpadTapDistance = 8;
const miniTrackpadDoubleTapMs = 280;
const miniTrackpadDoubleTapDistance = 24;

type RemoteShortcut = {
  key: string;
  modifiers?: string[];
};

type RemoteShortcutMap = {
  previous: RemoteShortcut;
  playPause: RemoteShortcut;
  next: RemoteShortcut;
  seekBackward: RemoteShortcut;
  seekForward: RemoteShortcut;
  volumeDown: RemoteShortcut;
  mute: RemoteShortcut;
  volumeUp: RemoteShortcut;
  back: RemoteShortcut;
  appFullscreen: RemoteShortcut;
  browserFullscreen: RemoteShortcut;
  space: RemoteShortcut;
};

const remoteShortcutMaps: Record<RemoteModeId, RemoteShortcutMap> = {
  standard: {
    previous: { key: "MediaPreviousTrack" },
    playPause: { key: "MediaPlayPause" },
    next: { key: "MediaNextTrack" },
    seekBackward: { key: "ArrowLeft" },
    seekForward: { key: "ArrowRight" },
    volumeDown: { key: "VolumeDown" },
    mute: { key: "VolumeMute" },
    volumeUp: { key: "VolumeUp" },
    back: { key: "Escape" },
    appFullscreen: { key: "F" },
    browserFullscreen: { key: "F11" },
    space: { key: "Space" }
  },
  youtube: {
    previous: { key: "P", modifiers: ["Shift"] },
    playPause: { key: "K" },
    next: { key: "N", modifiers: ["Shift"] },
    seekBackward: { key: "J" },
    seekForward: { key: "L" },
    volumeDown: { key: "ArrowDown" },
    mute: { key: "M" },
    volumeUp: { key: "ArrowUp" },
    back: { key: "Escape" },
    appFullscreen: { key: "F" },
    browserFullscreen: { key: "F11" },
    space: { key: "Space" }
  },
  kodi: {
    previous: { key: "PageDown" },
    playPause: { key: "Space" },
    next: { key: "PageUp" },
    seekBackward: { key: "ArrowLeft" },
    seekForward: { key: "ArrowRight" },
    volumeDown: { key: "-" },
    mute: { key: "F8" },
    volumeUp: { key: "+" },
    back: { key: "Backspace" },
    appFullscreen: { key: "Tab" },
    browserFullscreen: { key: "\\" },
    space: { key: "Space" }
  }
};

type MouseButtonName = "left" | "right";

type RemoteModeProps = {
  audioState: AudioStateMessage | null;
  remoteSettings: RemoteSettings;
  onPointerButtonClick: (button: MouseButtonName) => void;
  onPointerMove: (dx: number, dy: number) => void;
  sendSpecial: (key: string, modifiers?: string[]) => void;
};

type RemoteButtonProps = {
  label: string;
  onClick?: () => void;
  pressProps?: RepeatablePressProps;
  children?: ReactNode;
  className?: string;
  disabled?: boolean;
  title?: string;
};

type RepeatablePressProps = Pick<ButtonHTMLAttributes<HTMLButtonElement>, "onPointerDown" | "onPointerUp" | "onPointerCancel" | "onPointerLeave" | "onClick">;

type MiniTrackpadPointer = {
  id: number;
  startX: number;
  startY: number;
  lastX: number;
  lastY: number;
  maxDistance: number;
};

type PendingMiniTap = {
  timeout: number;
  x: number;
  y: number;
  time: number;
};

export function RemoteMode({
  audioState,
  remoteSettings,
  onPointerButtonClick,
  onPointerMove,
  sendSpecial
}: RemoteModeProps) {
  const [showUtilityPanel, setShowUtilityPanel] = useState(false);
  const repeatTimeoutRef = useRef<number | null>(null);
  const repeatIntervalRef = useRef<number | null>(null);
  const ignoreNextClickRef = useRef(false);
  const miniTrackpadPointerRef = useRef<MiniTrackpadPointer | null>(null);
  const navigationPanelPointerRef = useRef<MiniTrackpadPointer | null>(null);
  const pendingMiniTapRef = useRef<PendingMiniTap | null>(null);
  const modeCopy = getRemoteModeCopy(remoteSettings.mode);

  const stopRepeatingPress = () => {
    if (repeatTimeoutRef.current !== null) {
      window.clearTimeout(repeatTimeoutRef.current);
      repeatTimeoutRef.current = null;
    }

    if (repeatIntervalRef.current !== null) {
      window.clearInterval(repeatIntervalRef.current);
      repeatIntervalRef.current = null;
    }
  };

  const clearPendingMiniTap = () => {
    if (pendingMiniTapRef.current !== null) {
      window.clearTimeout(pendingMiniTapRef.current.timeout);
      pendingMiniTapRef.current = null;
    }
  };
  const shortcuts = remoteShortcutMaps[remoteSettings.mode];
  const sendShortcut = (shortcut: RemoteShortcut) => {
    if (shortcut.modifiers) {
      sendSpecial(shortcut.key, shortcut.modifiers);
      return;
    }

    sendSpecial(shortcut.key);
  };
  const sendPrevious = () => sendShortcut(shortcuts.previous);
  const sendPlayPause = () => sendShortcut(shortcuts.playPause);
  const sendNext = () => sendShortcut(shortcuts.next);
  const sendSeekBackward = () => sendShortcut(shortcuts.seekBackward);
  const sendSeekForward = () => sendShortcut(shortcuts.seekForward);
  const sendVolumeDown = () => sendShortcut(shortcuts.volumeDown);
  const sendMute = () => sendShortcut(shortcuts.mute);
  const sendVolumeUp = () => sendShortcut(shortcuts.volumeUp);
  const sendBack = () => sendShortcut(shortcuts.back);
  const sendAppFullscreen = () => sendShortcut(shortcuts.appFullscreen);
  const sendBrowserFullscreen = () => sendShortcut(shortcuts.browserFullscreen);
  const sendSpace = () => sendShortcut(shortcuts.space);

  useEffect(
    () => () => {
      stopRepeatingPress();
      clearPendingMiniTap();
    },
    []
  );

  const getRepeatablePressProps = (action: () => void): RepeatablePressProps => ({
    onPointerDown: (event) => {
      if (event.button !== 0) {
        return;
      }

      event.preventDefault();
      ignoreNextClickRef.current = true;
      event.currentTarget.setPointerCapture?.(event.pointerId);
      stopRepeatingPress();
      action();
      repeatTimeoutRef.current = window.setTimeout(() => {
        action();
        repeatIntervalRef.current = window.setInterval(action, repeatIntervalMs);
      }, repeatStartDelayMs);
    },
    onPointerUp: stopRepeatingPress,
    onPointerCancel: stopRepeatingPress,
    onPointerLeave: stopRepeatingPress,
    onClick: () => {
      if (ignoreNextClickRef.current) {
        ignoreNextClickRef.current = false;
        return;
      }

      action();
    }
  });

  const queuePointerTap = (x: number, y: number, time: number) => {
    const previousTap = pendingMiniTapRef.current;
    if (
      previousTap &&
      time - previousTap.time <= miniTrackpadDoubleTapMs &&
      Math.hypot(x - previousTap.x, y - previousTap.y) <= miniTrackpadDoubleTapDistance
    ) {
      clearPendingMiniTap();
      onPointerButtonClick("right");
      return;
    }

    clearPendingMiniTap();
    pendingMiniTapRef.current = {
      x,
      y,
      time,
      timeout: window.setTimeout(() => {
        pendingMiniTapRef.current = null;
        onPointerButtonClick("left");
      }, miniTrackpadDoubleTapMs)
    };
  };

  const pressMiniTrackpad = (event: React.PointerEvent<HTMLButtonElement>) => {
    if (event.button !== 0) {
      return;
    }

    event.preventDefault();
    event.currentTarget.setPointerCapture?.(event.pointerId);
    miniTrackpadPointerRef.current = {
      id: event.pointerId,
      startX: event.clientX,
      startY: event.clientY,
      lastX: event.clientX,
      lastY: event.clientY,
      maxDistance: 0
    };
  };

  const moveMiniTrackpad = (event: React.PointerEvent<HTMLButtonElement>) => {
    const pointer = miniTrackpadPointerRef.current;
    if (!pointer || pointer.id !== event.pointerId) {
      return;
    }

    event.preventDefault();
    const dx = event.clientX - pointer.lastX;
    const dy = event.clientY - pointer.lastY;
    pointer.lastX = event.clientX;
    pointer.lastY = event.clientY;
    pointer.maxDistance = Math.max(pointer.maxDistance, Math.hypot(event.clientX - pointer.startX, event.clientY - pointer.startY));

    if (Math.abs(dx) < 0.01 && Math.abs(dy) < 0.01) {
      return;
    }

    onPointerMove(round(dx * miniTrackpadSensitivity), round(dy * miniTrackpadSensitivity));
  };

  const releaseMiniTrackpad = (event: React.PointerEvent<HTMLButtonElement>) => {
    const pointer = miniTrackpadPointerRef.current;
    if (!pointer || pointer.id !== event.pointerId) {
      return;
    }

    event.preventDefault();
    miniTrackpadPointerRef.current = null;
    if (
      typeof event.currentTarget.hasPointerCapture === "function" &&
      typeof event.currentTarget.releasePointerCapture === "function" &&
      event.currentTarget.hasPointerCapture(event.pointerId)
    ) {
      event.currentTarget.releasePointerCapture(event.pointerId);
    }

    if (pointer.maxDistance > miniTrackpadTapDistance) {
      return;
    }

    queuePointerTap(event.clientX, event.clientY, event.timeStamp);
  };

  const cancelMiniTrackpad = (event: React.PointerEvent<HTMLButtonElement>) => {
    const pointer = miniTrackpadPointerRef.current;
    if (!pointer || pointer.id !== event.pointerId) {
      return;
    }

    miniTrackpadPointerRef.current = null;
  };

  const pressNavigationPanel = (event: React.PointerEvent<HTMLDivElement>) => {
    if (!remoteSettings.navigationRing || event.button !== 0 || isInteractiveTarget(event.target)) {
      return;
    }

    event.preventDefault();
    event.currentTarget.setPointerCapture?.(event.pointerId);
    navigationPanelPointerRef.current = {
      id: event.pointerId,
      startX: event.clientX,
      startY: event.clientY,
      lastX: event.clientX,
      lastY: event.clientY,
      maxDistance: 0
    };
  };

  const moveNavigationPanel = (event: React.PointerEvent<HTMLDivElement>) => {
    const pointer = navigationPanelPointerRef.current;
    if (!pointer || pointer.id !== event.pointerId) {
      return;
    }

    event.preventDefault();
    const dx = event.clientX - pointer.lastX;
    const dy = event.clientY - pointer.lastY;
    pointer.lastX = event.clientX;
    pointer.lastY = event.clientY;
    pointer.maxDistance = Math.max(pointer.maxDistance, Math.hypot(event.clientX - pointer.startX, event.clientY - pointer.startY));

    if (Math.abs(dx) < 0.01 && Math.abs(dy) < 0.01) {
      return;
    }

    onPointerMove(round(dx * miniTrackpadSensitivity), round(dy * miniTrackpadSensitivity));
  };

  const releaseNavigationPanel = (event: React.PointerEvent<HTMLDivElement>) => {
    const pointer = navigationPanelPointerRef.current;
    if (!pointer || pointer.id !== event.pointerId) {
      return;
    }

    event.preventDefault();
    navigationPanelPointerRef.current = null;
    if (
      typeof event.currentTarget.hasPointerCapture === "function" &&
      typeof event.currentTarget.releasePointerCapture === "function" &&
      event.currentTarget.hasPointerCapture(event.pointerId)
    ) {
      event.currentTarget.releasePointerCapture(event.pointerId);
    }

    if (pointer.maxDistance > miniTrackpadTapDistance) {
      return;
    }

    queuePointerTap(event.clientX, event.clientY, event.timeStamp);
  };

  const cancelNavigationPanel = (event: React.PointerEvent<HTMLDivElement>) => {
    const pointer = navigationPanelPointerRef.current;
    if (!pointer || pointer.id !== event.pointerId) {
      return;
    }

    navigationPanelPointerRef.current = null;
  };

  const renderNavigationControl = () => {
    if (!remoteSettings.navigationRing) {
      return (
        <div className="remote-dpad" aria-label="Directional pad">
          <button type="button" className="remote-dpad-up" aria-label="D-pad up" {...getRepeatablePressProps(() => sendSpecial("ArrowUp"))}>
            <ArrowUp aria-hidden="true" />
          </button>
          <button type="button" className="remote-dpad-left" aria-label="D-pad left" {...getRepeatablePressProps(() => sendSpecial("ArrowLeft"))}>
            <ArrowLeft aria-hidden="true" />
          </button>
          <button type="button" className="remote-dpad-ok" onClick={() => sendSpecial("Enter")}>
            OK
          </button>
          <button type="button" className="remote-dpad-right" aria-label="D-pad right" {...getRepeatablePressProps(() => sendSpecial("ArrowRight"))}>
            <ArrowRight aria-hidden="true" />
          </button>
          <button type="button" className="remote-dpad-down" aria-label="D-pad down" {...getRepeatablePressProps(() => sendSpecial("ArrowDown"))}>
            <ArrowDown aria-hidden="true" />
          </button>
        </div>
      );
    }

    return (
      <div className="remote-navigation-ring" aria-label="Navigation ring">
        <button type="button" className="remote-ring-zone remote-ring-up" aria-label="D-pad up" {...getRepeatablePressProps(() => sendSpecial("ArrowUp"))}>
          <ArrowUp aria-hidden="true" />
        </button>
        <button type="button" className="remote-ring-zone remote-ring-left" aria-label="D-pad left" {...getRepeatablePressProps(() => sendSpecial("ArrowLeft"))}>
          <ArrowLeft aria-hidden="true" />
        </button>
        <button type="button" className="remote-ring-zone remote-ring-right" aria-label="D-pad right" {...getRepeatablePressProps(() => sendSpecial("ArrowRight"))}>
          <ArrowRight aria-hidden="true" />
        </button>
        <button type="button" className="remote-ring-zone remote-ring-down" aria-label="D-pad down" {...getRepeatablePressProps(() => sendSpecial("ArrowDown"))}>
          <ArrowDown aria-hidden="true" />
        </button>
        <button
          type="button"
          className="remote-mini-trackpad"
          aria-label="Mini trackpad"
          onClick={(event) => event.preventDefault()}
          onPointerCancel={cancelMiniTrackpad}
          onPointerDown={pressMiniTrackpad}
          onPointerLeave={cancelMiniTrackpad}
          onPointerMove={moveMiniTrackpad}
          onPointerUp={releaseMiniTrackpad}
        >
          <span aria-hidden="true" />
        </button>
      </div>
    );
  };

  const utilityPanelId = "remote-utility-panel";

  return (
    <section className={`remote-mode ${showUtilityPanel ? "remote-utility-open" : ""}`} aria-label="Couch remote">
      <div className="remote-section remote-media-section">
        <div className="remote-section-title">
          <span>Media</span>
          <small>{modeCopy.media}</small>
        </div>
        <div className="remote-primary-grid" aria-label="Media controls">
          <RemoteButton label="Previous track" onClick={sendPrevious}>
            <SkipBack aria-hidden="true" />
            <span>Prev</span>
          </RemoteButton>
          <RemoteButton label="Play or pause" className="primary" onClick={sendPlayPause}>
            <Play aria-hidden="true" />
            <Pause aria-hidden="true" />
            <span>Play/Pause</span>
          </RemoteButton>
          <RemoteButton label="Next track" onClick={sendNext}>
            <SkipForward aria-hidden="true" />
            <span>Next</span>
          </RemoteButton>
          <RemoteButton label="Seek backward" title={modeCopy.seekBackwardTitle} pressProps={getRepeatablePressProps(sendSeekBackward)}>
            <Rewind aria-hidden="true" />
            <span>Seek -</span>
          </RemoteButton>
          <RemoteButton label="Space" title={modeCopy.spaceTitle} onClick={sendSpace}>
            <span>Space</span>
          </RemoteButton>
          <RemoteButton label="Seek forward" title={modeCopy.seekForwardTitle} pressProps={getRepeatablePressProps(sendSeekForward)}>
            <FastForward aria-hidden="true" />
            <span>Seek +</span>
          </RemoteButton>
          <RemoteButton label="Esc or back" onClick={sendBack}>
            <CornerDownLeft aria-hidden="true" />
            <span>Esc / Back</span>
          </RemoteButton>
          <RemoteButton label="Fullscreen" title={modeCopy.appFullscreenTitle} onClick={sendAppFullscreen}>
            <Maximize2 aria-hidden="true" />
            <span>Fullscreen</span>
          </RemoteButton>
          <RemoteButton label="Browser fullscreen" title={modeCopy.browserFullscreenTitle} onClick={sendBrowserFullscreen}>
            <Maximize2 aria-hidden="true" />
            <span>Browser Full</span>
          </RemoteButton>
        </div>
      </div>

      <div className="remote-section remote-volume-section">
        <div className="remote-section-title">
          <span>Volume</span>
          <small>{remoteSettings.mode === "standard" && audioState ? `${audioState.volume}%${audioState.muted ? " muted" : ""}` : modeCopy.volume}</small>
        </div>
        <div className="remote-volume-grid" aria-label="Volume controls">
          <RemoteButton label="Volume down" pressProps={getRepeatablePressProps(sendVolumeDown)}>
            <Volume2 aria-hidden="true" />
            <span>Vol -</span>
          </RemoteButton>
          <RemoteButton label={audioState?.muted ? "Unmute PC" : "Mute PC"} onClick={sendMute}>
            {audioState?.muted ? <VolumeX aria-hidden="true" /> : <Volume2 aria-hidden="true" />}
            <span>{audioState?.muted ? "Unmute" : "Mute"}</span>
          </RemoteButton>
          <RemoteButton label="Volume up" pressProps={getRepeatablePressProps(sendVolumeUp)}>
            <Volume2 aria-hidden="true" />
            <span>Vol +</span>
          </RemoteButton>
        </div>
      </div>

      <div
        className="remote-section remote-navigation-section"
        onPointerCancel={cancelNavigationPanel}
        onPointerDown={pressNavigationPanel}
        onPointerLeave={cancelNavigationPanel}
        onPointerMove={moveNavigationPanel}
        onPointerUp={releaseNavigationPanel}
      >
        <div className="remote-section-title">
          <span>Navigation</span>
          <small>{remoteSettings.navigationRing ? "Ring navigation with a center mini-trackpad." : "D-pad for menus, players, slides, and full-screen dialogs."}</small>
        </div>
        {renderNavigationControl()}
      </div>

      <div id={utilityPanelId} className="remote-section remote-utility-section">
        <div className="remote-section-title">
          <span>Windows</span>
          <small>Fast helper keys for couch use.</small>
        </div>
        <div className="remote-utility-grid" aria-label="Windows helper controls">
          <RemoteButton label="Start or search" onClick={() => sendSpecial("Win")}>
            <Search aria-hidden="true" />
            <span>Start</span>
          </RemoteButton>
          <RemoteButton label="Alt Tab" onClick={() => sendSpecial("Tab", ["Alt"])}>
            <span>Alt Tab</span>
          </RemoteButton>
          <RemoteButton label="Browser back" onClick={() => sendSpecial("BrowserBack")}>
            <CornerDownLeft aria-hidden="true" />
            <span>Back</span>
          </RemoteButton>
        </div>
      </div>
      <button
        type="button"
        className="remote-fn-button remote-floating-fn"
        aria-controls={utilityPanelId}
        aria-expanded={showUtilityPanel}
        onClick={() => setShowUtilityPanel((isOpen) => !isOpen)}
      >
        {showUtilityPanel ? "Main" : "Fn"}
      </button>
    </section>
  );
}

function round(value: number): number {
  const rounded = Math.round(value * 100) / 100;
  return Object.is(rounded, -0) ? 0 : rounded;
}

function getRemoteModeCopy(mode: RemoteModeId) {
  switch (mode) {
    case "youtube":
      return {
        media: "YouTube shortcuts.",
        volume: "YouTube player volume.",
        seekBackwardTitle: "YouTube rewind shortcut",
        seekForwardTitle: "YouTube forward shortcut",
        spaceTitle: "Space / common browser play-pause",
        appFullscreenTitle: "YouTube/app fullscreen shortcut",
        browserFullscreenTitle: "Chrome/browser fullscreen shortcut"
      };
    case "kodi":
      return {
        media: "Kodi keyboard shortcuts.",
        volume: "Kodi volume keys.",
        seekBackwardTitle: "Kodi skip backward / left",
        seekForwardTitle: "Kodi skip forward / right",
        spaceTitle: "Kodi pause/play",
        appFullscreenTitle: "Kodi fullscreen playback",
        browserFullscreenTitle: "Kodi fullscreen/windowed"
      };
    default:
      return {
        media: "TV, video, music, and slides.",
        volume: "Uses Windows volume keys.",
        seekBackwardTitle: "Seek backward / left arrow",
        seekForwardTitle: "Seek forward / right arrow",
        spaceTitle: "Space / common browser play-pause",
        appFullscreenTitle: "Video/app fullscreen shortcut",
        browserFullscreenTitle: "Chrome/browser fullscreen shortcut"
      };
  }
}

function isInteractiveTarget(target: EventTarget): boolean {
  return target instanceof Element && target.closest("button, a, input, textarea, select") !== null;
}

function RemoteButton({ label, onClick, pressProps, children, className, disabled = false, title }: RemoteButtonProps) {
  return (
    <button type="button" aria-label={label} className={className} disabled={disabled} title={title} {...(pressProps ?? { onClick })}>
      {children ?? <span>{label}</span>}
    </button>
  );
}
