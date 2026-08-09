import vlc from "./official/vlc.mjs";
import spotify from "./official/spotify.mjs";
import browser from "./official/browser.mjs";
import netflix from "./official/netflix.mjs";
import primeVideo from "./official/prime-video.mjs";
import disneyPlus from "./official/disney-plus.mjs";
import twitch from "./official/twitch.mjs";
import plex from "./official/plex.mjs";
import zoom from "./official/zoom.mjs";
import windows from "./official/windows.mjs";
import power from "./official/power.mjs";
import displays from "./official/displays.mjs";
import photos from "./official/photos.mjs";
import blender from "./official/blender.mjs";

export const officialScreens = [vlc, spotify, browser, netflix, primeVideo, disneyPlus, twitch, plex, zoom, windows, power, displays, photos, blender];

export const packageFilenames = new Map([
  ["official.vlc", "vlc.volturascreen"], ["official.spotify", "spotify.volturascreen"],
  ["official.browser", "web-browser.volturascreen"], ["official.netflix", "netflix.volturascreen"],
  ["official.primeVideo", "prime-video.volturascreen"], ["official.disneyPlus", "disney-plus.volturascreen"],
  ["official.twitch", "twitch.volturascreen"], ["official.plex", "plex.volturascreen"],
  ["official.zoom", "zoom.volturascreen"], ["official.windows", "windows.volturascreen"],
  ["official.power", "power.volturascreen"], ["official.displays", "displays.volturascreen"],
  ["official.windowsPhotos", "windows-photos.volturascreen"], ["official.blender", "blender.volturascreen"]
]);
