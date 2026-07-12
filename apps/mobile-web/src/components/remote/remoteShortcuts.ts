import type { RemoteModeId } from "../../remoteSettings";

export type RemoteShortcut = {
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
  stop?: RemoteShortcut;
  info?: RemoteShortcut;
  subtitles?: RemoteShortcut;
  powerMenu?: RemoteShortcut;
};

export const remoteShortcutMaps: Record<RemoteModeId, RemoteShortcutMap> = {
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
    previous: { key: "MediaPreviousTrack" },
    playPause: { key: "Space" },
    next: { key: "MediaNextTrack" },
    seekBackward: { key: "ArrowLeft" },
    seekForward: { key: "ArrowRight" },
    volumeDown: { key: "-" },
    mute: { key: "F8" },
    volumeUp: { key: "+" },
    back: { key: "Backspace" },
    appFullscreen: { key: "Tab" },
    browserFullscreen: { key: "\\" },
    space: { key: "Space" },
    stop: { key: "X" },
    info: { key: "I" },
    subtitles: { key: "T" },
    powerMenu: { key: "S" }
  }
};
