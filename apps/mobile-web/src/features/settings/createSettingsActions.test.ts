import { describe, expect, it, vi } from "vitest";
import { defaultTrackpadSettings } from "../../foundation/input/gestures";
import { createSettingsActions } from "./createSettingsActions";

const settingsState = {
  setAppSettingsState: vi.fn(),
  setKeyboardSettings: vi.fn(),
  setRemoteSettingsState: vi.fn(),
  setTrackpadSettingsState: vi.fn()
};

function createActions() {
  return createSettingsActions({
    clientId: "client-a",
    effectiveTrackpadSettings: defaultTrackpadSettings,
    forgetPc: vi.fn(),
    setHostPointerSpeed: vi.fn(),
    settingsState
  });
}

describe("createSettingsActions", () => {
  it("persists remote settings without owning remote-mode navigation", () => {
    const actions = createActions();
    actions.updateRemoteSetting("navigationRing", false);

    expect(settingsState.setRemoteSettingsState).toHaveBeenCalledOnce();
  });
});
