import {
  screen,
  mediaTransport,
  button,
  buttonGrid,
  volumeControls,
  knownApp,
  shortcut,
} from "../builders/screen-builder.mjs";
const id = "official.plex";
export default screen({
  id,
  name: "Plex",
  revision: "official-plex-2",
  category: "Media",
  tags: ["Media", "Plex", "Video"],
  shortDescription: "Launch Plex for Windows and control navigation, playback, and volume.",
  optionalTargetApplication: "plex",
  sections: [
    buttonGrid(`${id}.app`, "Plex", [
      button(`${id}.launch`, "Open Plex", knownApp("plex"), { icon: "app-window", size: "wide" }),
    ]),
    buttonGrid(`${id}.navigation`, "Navigation", [
      button(`${id}.back`, "Back", shortcut("ArrowLeft", ["Alt"]), { icon: "arrow-left" }),
      button(`${id}.forward`, "Forward", shortcut("ArrowRight", ["Alt"]), { icon: "arrow-right" }),
      button(`${id}.fullscreen`, "Fullscreen", shortcut("F11"), { icon: "maximize" }),
    ]),
    mediaTransport(id),
    volumeControls(`${id}.volume`),
  ],
});
