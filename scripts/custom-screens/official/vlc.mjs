import { screen, button, buttonGrid, volumeControls, knownApp, shortcut } from "../builders/screen-builder.mjs";

const id = "official.vlc";
export default screen({ id, name: "VLC", revision: "official-vlc-2", category: "Media", tags: ["Media", "VLC", "Windows"], shortDescription: "Playback, seek, volume, and window controls for VLC.", optionalTargetApplication: "vlc", sections: [
  buttonGrid(`${id}.app`, "VLC", [button(`${id}.launch`, "Open VLC", knownApp("vlc"), { icon: "app-window", size: "wide" })]),
  buttonGrid(`${id}.transport`, "Playback", [button(`${id}.previous`, "Previous", shortcut("P"), { icon: "skip-back" }), button(`${id}.playPause`, "Play / pause", shortcut("Space"), { icon: "play", size: "wide" }), button(`${id}.next`, "Next", shortcut("N"), { icon: "skip-forward" }), button(`${id}.stop`, "Stop", shortcut("S"), { icon: "square-x" })]),
  buttonGrid(`${id}.seek`, "Seek", [button(`${id}.back10`, "Back 10 sec", shortcut("ArrowLeft", ["Shift"]), { icon: "arrow-left", repeat: true }), button(`${id}.ahead10`, "Ahead 10 sec", shortcut("ArrowRight", ["Shift"]), { icon: "arrow-right", repeat: true }), button(`${id}.fullscreen`, "Fullscreen", shortcut("F"), { icon: "maximize" }), button(`${id}.mute`, "Mute", shortcut("M"), { icon: "volume-x" })]),
  volumeControls(`${id}.volume`)
] });
