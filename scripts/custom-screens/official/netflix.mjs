import { screen, streamingPlayback } from "../builders/screen-builder.mjs";
const id = "official.netflix";
export default screen({
  id,
  name: "Netflix",
  revision: "official-netflix-2",
  category: "Streaming",
  tags: ["Streaming", "Netflix", "Video"],
  shortDescription: "Open Netflix and control web playback and volume.",
  optionalTargetApplication: "browser",
  sections: streamingPlayback(id, "https://www.netflix.com/"),
});
