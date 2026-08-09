import { screen, streamingPlayback } from "../builders/screen-builder.mjs";
const id = "official.primeVideo";
export default screen({ id, name: "Prime Video", revision: "official-prime-video-2", category: "Streaming", tags: ["Streaming", "Prime Video", "Video"], shortDescription: "Open Prime Video and control web playback and volume.", optionalTargetApplication: "browser", sections: streamingPlayback(id, "https://www.primevideo.com/") });
