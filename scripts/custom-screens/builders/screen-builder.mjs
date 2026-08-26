import { button, buttonGrid, navigationPad, trackpad, volumeControls } from "./layouts.mjs";
import { builtIn, knownApp, openWebsite, shortcut } from "./actions.mjs";

export function screen(definition) {
  return {
    metadata: {
      id: definition.id,
      name: definition.name,
      shortDescription: definition.shortDescription,
      longDescription: definition.longDescription ?? definition.shortDescription,
      tags: definition.tags,
      category: definition.category,
      minimumVolturaAirVersion: definition.minimumVolturaAirVersion ?? "0.8.10",
      requiredCapabilities: requiredCapabilities(definition.sections),
      optionalTargetApplication: definition.optionalTargetApplication ?? null,
      official: true,
    },
    screen: {
      id: definition.id,
      name: definition.name,
      revision: definition.revision,
      assignedClientIds: [],
      orientationLayoutsEnabled: definition.orientationLayoutsEnabled ?? true,
      showNavigationHeader: definition.showNavigationHeader ?? true,
      sections: definition.sections,
    },
  };
}

function requiredCapabilities(sections) {
  const capabilities = new Set(["customScreens"]);
  for (const section of sections) {
    if (["trackpad", "collapsibleTrackpad", "navigationRing", "dpad"].includes(section.kind)) {
      capabilities.add("remoteInput");
    }
    if (section.kind === "volume") capabilities.add("volumeControl");
    for (const control of section.buttons) {
      const capability = {
        text: "remoteInput",
        shortcut: "remoteInput",
        builtIn: "remoteInput",
        urlOpen: "urlOpen",
        knownApp: "remoteAppLaunch",
        hostAction: "hostActions",
      }[control.action.kind];
      if (capability) capabilities.add(capability);
    }
  }
  return [...capabilities].sort();
}

export const mediaTransport = (prefix, options = {}) =>
  buttonGrid(`${prefix}.transport`, options.name ?? "Playback", [
    button(`${prefix}.previous`, "Previous", builtIn("media.previous"), { icon: "skip-back" }),
    button(`${prefix}.playPause`, "Play / pause", builtIn("media.playPause"), {
      icon: "play",
      size: "wide",
    }),
    button(`${prefix}.next`, "Next", builtIn("media.next"), { icon: "skip-forward" }),
    ...(options.stop
      ? [button(`${prefix}.stop`, "Stop", builtIn("media.stop"), { icon: "square-x" })]
      : []),
  ]);

export const streamingPlayback = (prefix, url) => [
  buttonGrid(`${prefix}.open`, "Open", [
    button(`${prefix}.website`, "Open service", openWebsite(url), {
      icon: "app-window",
      size: "wide",
    }),
  ]),
  buttonGrid(`${prefix}.playback`, "Playback", [
    button(`${prefix}.playPause`, "Play / pause", shortcut("Space"), {
      icon: "play",
      size: "wide",
    }),
    button(`${prefix}.seekBack`, "Back 10 sec", shortcut("ArrowLeft"), {
      icon: "arrow-left",
      repeat: false,
    }),
    button(`${prefix}.seekForward`, "Ahead 10 sec", shortcut("ArrowRight"), {
      icon: "arrow-right",
      repeat: false,
    }),
    button(`${prefix}.fullscreen`, "Fullscreen", shortcut("F"), { icon: "maximize" }),
  ]),
  volumeControls(`${prefix}.volume`),
];

export const streamingTrackpad = (prefix, url) => [
  buttonGrid(`${prefix}.open`, "Open", [
    button(`${prefix}.website`, "Open service", openWebsite(url), {
      icon: "app-window",
      size: "wide",
    }),
  ]),
  trackpad(`${prefix}.trackpad`, {
    collapsible: true,
    initiallyExpanded: false,
    heightMode: "content",
  }),
  volumeControls(`${prefix}.volume`),
];

export {
  button,
  buttonGrid,
  navigationPad,
  trackpad,
  volumeControls,
  builtIn,
  knownApp,
  openWebsite,
  shortcut,
};
