import {
  screen,
  mediaTransport,
  button,
  buttonGrid,
  volumeControls,
  knownApp,
} from "../builders/screen-builder.mjs";
const id = "official.spotify";
export default screen({
  id,
  name: "Spotify",
  revision: "official-spotify-1",
  category: "Media",
  tags: ["Media", "Spotify", "Music"],
  shortDescription: "Launch Spotify and control system media playback and volume.",
  optionalTargetApplication: "spotify",
  sections: [
    buttonGrid(`${id}.app`, "Spotify", [
      button(`${id}.launch`, "Open Spotify", knownApp("spotify"), {
        icon: "app-window",
        size: "wide",
      }),
    ]),
    mediaTransport(id),
    volumeControls(`${id}.volume`),
  ],
});
