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
  const launchRequests: string[] = [];
  const requestRemoteModeLaunch = (mode: unknown, settings: RemoteSettings) => {
    if (mode === "youtube" && settings.openYoutube) {
      launchRequests.push("openYoutube");
    } else if (mode === "kodi" && settings.startKodi) {
      launchRequests.push("startOrActivateKodi");
    }
  };

  const actions = createSettingsActions({
    clientId: "client-a",
    effectiveTrackpadSettings: defaultTrackpadSettings,
    forgetPc: vi.fn(),
    onSelectRemoteMode: requestRemoteModeLaunch,
    remoteSettings,
    setHostPointerSpeed: vi.fn(),
    settingsState
  });
  return { actions, launchRequests };
}

describe("createSettingsActions remote launch request ownership", () => {
  it.each([
    ["kodi" as const, "youtube" as const, "openYoutube"],
    ["youtube" as const, "kodi" as const, "startOrActivateKodi"]
  ])("requests only the newly selected mode when changing from %s to %s", (from, to, expectedLaunch) => {
    const remoteSettings = {
      ...defaultRemoteSettings,
      mode: from,
      openYoutube: true,
      startKodi: true
    };
    const { actions, launchRequests } = createRemoteActions(remoteSettings);

    actions.updateRemoteSetting("mode", to);

    expect(launchRequests).toEqual([expectedLaunch]);
  });

  it("does not launch when an unrelated remote setting changes", () => {
    const { actions, launchRequests } = createRemoteActions({
      ...defaultRemoteSettings,
      mode: "youtube",
      openYoutube: true
    });

    actions.updateRemoteSetting("navigationRing", false);

    expect(launchRequests).toEqual([]);
  });
});
