import { describe, expect, it, vi } from "vitest";
import { defaultTrackpadSettings } from "../../foundation/input/gestures";
import { createSettingsActions } from "./createSettingsActions";
import {
  appSettingsKey,
  remoteSettingsKey,
  trackpadSettingsKey,
} from "../../foundation/settings/appStorage";

const settingsState = {
  setAppSettingsState: vi.fn(),
  setKeyboardSettings: vi.fn(),
  setRemoteSettingsState: vi.fn(),
  setTrackpadSettingsState: vi.fn(),
};

function createActions() {
  return createSettingsActions({
    clientId: "client-a",
    effectiveTrackpadSettings: defaultTrackpadSettings,
    forgetPc: vi.fn(),
    setHostPointerSpeed: vi.fn(),
    settingsState,
  });
}

describe("createSettingsActions", () => {
  it("does not clear saved settings before Forget PC revocation succeeds", () => {
    const keys = [
      appSettingsKey("client-a", "pc"),
      remoteSettingsKey("client-a", "pc"),
      trackpadSettingsKey("client-a", "pc"),
    ];
    keys.forEach((key) => localStorage.setItem(key, "saved"));
    createActions().forgetPcAndSettings("pc");
    keys.forEach((key) => expect(localStorage.getItem(key)).toBe("saved"));
  });
  it("persists remote settings without owning remote-mode navigation", () => {
    const actions = createActions();
    actions.updateRemoteSetting("navigationRing", false);

    expect(settingsState.setRemoteSettingsState).toHaveBeenCalledOnce();
  });
});
