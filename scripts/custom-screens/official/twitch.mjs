import { screen, streamingTrackpad } from "../builders/screen-builder.mjs";
const id = "official.twitch";
export default screen({
  id,
  name: "Twitch",
  revision: "official-twitch-2",
  category: "Streaming",
  tags: ["Streaming", "Twitch", "Live"],
  shortDescription: "Open Twitch and control it with a trackpad and volume slider.",
  optionalTargetApplication: "browser",
  sections: streamingTrackpad(id, "https://www.twitch.tv/"),
});
