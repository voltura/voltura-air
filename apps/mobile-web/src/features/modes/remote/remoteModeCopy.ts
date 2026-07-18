import type { RemoteModeId } from "../../../foundation/settings/remoteSettings";

export function getRemoteModeCopy(mode: RemoteModeId) {
  switch (mode) {
    case "youtube":
      return {
        media: "YouTube shortcuts.",
        volume: "YouTube player volume.",
        seekBackwardTitle: "YouTube rewind shortcut",
        seekForwardTitle: "YouTube forward shortcut",
        spaceTitle: "Space / common browser play-pause",
        appFullscreenLabel: "Fullscreen",
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
        appFullscreenLabel: "Toggle video",
        appFullscreenTitle: undefined,
        browserFullscreenTitle: "Kodi fullscreen/windowed"
      };
    case "standard":
      return {
        media: "TV, video, music, and slides.",
        volume: "Uses Windows volume keys.",
        seekBackwardTitle: "Seek backward / left arrow",
        seekForwardTitle: "Seek forward / right arrow",
        spaceTitle: "Space / common browser play-pause",
        appFullscreenLabel: "Fullscreen (F)",
        appFullscreenTitle: "Video/app fullscreen shortcut",
        browserFullscreenTitle: "Chrome/browser fullscreen shortcut"
      };
  }
}

export type RemoteModeCopy = ReturnType<typeof getRemoteModeCopy>;
