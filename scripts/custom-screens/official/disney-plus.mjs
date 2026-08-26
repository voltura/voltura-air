import { screen, streamingTrackpad } from "../builders/screen-builder.mjs";
const id = "official.disneyPlus";
export default screen({
  id,
  name: "Disney+",
  revision: "official-disney-plus-2",
  category: "Streaming",
  tags: ["Streaming", "Disney+", "Video"],
  shortDescription: "Open Disney+ and control it with a trackpad and volume slider.",
  optionalTargetApplication: "browser",
  sections: streamingTrackpad(id, "https://www.disneyplus.com/"),
});
