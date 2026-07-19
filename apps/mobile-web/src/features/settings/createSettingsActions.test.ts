import { describe, expect, it, vi } from "vitest";
import { defaultTrackpadSettings } from "../../foundation/input/gestures";
import { defaultRemoteSettings, type RemoteSettings } from "../../foundation/settings/remoteSettings";
import { createSettingsActions } from "./createSettingsActions";

const settingsState = {
  setAppSettingsState: vi.fn(),
  setKeyboardSettings: vi.fn(),
  setRemoteSettingsState: vi.fn(),
  setTrackpadSettingsState: vi.fn()
};

function createRemoteActions(remoteSettings: RemoteSettings) {
  const launches: string[] = [];
  const maybeLaunchRemoteMode = (mode: unknown, settings: RemoteSettings) => {
    if (mode === "youtube" && settings.openYoutube) {
      launches.push("openYoutube");
    } else if (mode === "kodi" && settings.startKodi) {
      launches.push("startOrActivateKodi");
    }
  };

  const actions = createSettingsActions({
    clientId: "client-a",
    effectiveTrackpadSettings: defaultTrackpadSettings,
    forgetPc: vi.fn(),
    onSelectRemoteMode: maybeLaunchRemoteMode,
    remoteSettings,
    setHostPointerSpeed: vi.fn(),
    settingsState
  });
  return { actions, launches };
}

describe("createSettingsActions remote launch ownership", () => {
  it.each([
    ["kodi" as const, "youtube" as const, "openYoutube"],
    ["youtube" as const, "kodi" as const, "startOrActivateKodi"]
  ])("launches only the newly selected mode when changing from %s to %s", (from, to, expectedLaunch) => {
    const remoteSettings = {
      ...defaultRemoteSettings,
      mode: from,
      openYoutube: true,
      startKodi: true
    };
    const { actions, launches } = createRemoteActions(remoteSettings);

    actions.updateRemoteSetting("mode", to);

    expect(launches).toEqual([expectedLaunch]);
  });

  it("does not launch when an unrelated remote setting changes", () => {
    const { actions, launches } = createRemoteActions({
      ...defaultRemoteSettings,
      mode: "youtube",
      openYoutube: true
    });

    actions.updateRemoteSetting("navigationRing", false);

    expect(launches).toEqual([]);
  });
});
