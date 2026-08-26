import { screen, button, buttonGrid, knownApp, shortcut } from "../builders/screen-builder.mjs";
const id = "official.zoom";
export default screen({
  id,
  name: "Zoom",
  revision: "official-zoom-2",
  category: "Communication",
  tags: ["Zoom", "Meetings", "Productivity"],
  shortDescription: "Launch Zoom and use its standard meeting keyboard shortcuts.",
  optionalTargetApplication: "zoom",
  sections: [
    buttonGrid(`${id}.app`, "Zoom", [
      button(`${id}.launch`, "Open Zoom", knownApp("zoom"), { icon: "app-window", size: "wide" }),
    ]),
    buttonGrid(`${id}.meeting`, "Meeting", [
      button(`${id}.mute`, "Mute", shortcut("A", ["Alt"]), { icon: "volume-x" }),
      button(`${id}.video`, "Video", shortcut("V", ["Alt"]), { icon: "monitor" }),
      button(`${id}.hand`, "Raise hand", shortcut("Y", ["Alt"]), { icon: "arrow-up" }),
      button(`${id}.share`, "Share screen", shortcut("S", ["Alt"]), { icon: "monitor" }),
      button(`${id}.chat`, "Chat", shortcut("H", ["Alt"]), { icon: "command" }),
      button(`${id}.record`, "Local record", shortcut("R", ["Alt"]), { icon: "command" }),
      button(`${id}.end`, "End meeting", shortcut("Q", ["Alt"]), {
        icon: "square-x",
        size: "wide",
      }),
    ]),
  ],
});
