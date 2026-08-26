import { screen, button, buttonGrid, knownApp, shortcut } from "../builders/screen-builder.mjs";
const id = "official.windowsPhotos";
export default screen({
  id,
  name: "Windows Photos",
  revision: "official-windows-photos-2",
  category: "Media",
  tags: ["Windows", "Photos", "Slideshow"],
  shortDescription: "Open Microsoft Photos and control images, zoom, rotation, and slideshows.",
  optionalTargetApplication: "windowsPhotos",
  sections: [
    buttonGrid(`${id}.app`, "Photos", [
      button(`${id}.launch`, "Open Photos", knownApp("windowsPhotos"), {
        icon: "app-window",
        size: "wide",
      }),
    ]),
    buttonGrid(`${id}.controls`, "Photo controls", [
      button(`${id}.previous`, "Previous", shortcut("ArrowLeft"), {
        icon: "arrow-left",
        repeat: true,
      }),
      button(`${id}.next`, "Next", shortcut("ArrowRight"), { icon: "arrow-right", repeat: true }),
      button(`${id}.zoomIn`, "Zoom in", shortcut("+", ["Control"]), { icon: "maximize" }),
      button(`${id}.zoomOut`, "Zoom out", shortcut("-", ["Control"]), { icon: "minimize" }),
      button(`${id}.rotate`, "Rotate", shortcut("R", ["Control"]), { icon: "refresh" }),
      button(`${id}.slideshow`, "Slideshow", shortcut("F5"), { icon: "play" }),
      button(`${id}.exit`, "Exit slideshow", shortcut("Escape"), { icon: "escape", size: "wide" }),
    ]),
  ],
});
