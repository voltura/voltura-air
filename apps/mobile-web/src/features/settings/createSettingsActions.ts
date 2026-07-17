import { clearAppSettings, clearRemoteSettings, clearTrackpadSettings } from "../../appStorage";
import type { AppSettings } from "../../appSettings";
import type { TrackpadSettings } from "../../gestures";
import { triggerHapticFeedback } from "../../hapticFeedback";
import type { KeyboardSettings } from "../../keyboardSettings";
import type { RemoteSettings } from "../../remoteSettings";
import type { usePcSettings } from "../../settings/usePcSettings";

type SettingsState = Pick<
  ReturnType<typeof usePcSettings>,
  | "setAppSettingsState"
  | "setKeyboardSettings"
  | "setRemoteSettingsState"
  | "setTrackpadSettingsState"
>;

interface SettingsActionOptions {
  clientId: string;
  effectiveTrackpadSettings: TrackpadSettings;
  forgetPc: (pcId: string) => void;
  onLaunchRemoteMode: (mode: unknown, settings: RemoteSettings) => void;
  onOpenRemote: () => void;
  remoteSettings: RemoteSettings;
  setHostPointerSpeed: (speed: number) => void;
  settingsState: SettingsState;
}

export function createSettingsActions({
  clientId,
  effectiveTrackpadSettings,
  forgetPc,
  onLaunchRemoteMode,
  onOpenRemote,
  remoteSettings,
  setHostPointerSpeed,
  settingsState
}: SettingsActionOptions) {
  const updateTrackpadSetting = <Key extends keyof TrackpadSettings>(key: Key, value: TrackpadSettings[Key]) => {
    settingsState.setTrackpadSettingsState((current) => ({
      ...current,
      settings: { ...current.settings, [key]: value }
    }));

    if (key === "pointerSpeed" && typeof value === "number") {
      setHostPointerSpeed(value);
    }

    if (key === "hapticFeedback" && value === true) {
      triggerHapticFeedback({ ...effectiveTrackpadSettings, hapticFeedback: true });
    }
  };

  const updateKeyboardSetting = <Key extends keyof KeyboardSettings>(key: Key, value: KeyboardSettings[Key]) => {
    settingsState.setKeyboardSettings((current) => ({ ...current, [key]: value }));
  };

  const updateRemoteSetting = <Key extends keyof RemoteSettings>(key: Key, value: RemoteSettings[Key]) => {
    const nextSettings = { ...remoteSettings, [key]: value };
    if (key === "mode") {
      if (value === "youtube" || value === "kodi") {
        onOpenRemote();
      }

      if (value !== remoteSettings.mode) {
        onLaunchRemoteMode(value, nextSettings);
      }
    }

    settingsState.setRemoteSettingsState((current) => ({
      ...current,
      isStored: true,
      settings: { ...current.settings, [key]: value }
    }));
  };

  const updateAppSetting = <Key extends keyof AppSettings>(key: Key, value: AppSettings[Key]) => {
    settingsState.setAppSettingsState((current) => ({
      ...current,
      settings: { ...current.settings, [key]: value }
    }));
  };

  const forgetPcAndSettings = (pcId: string) => {
    clearTrackpadSettings(clientId, pcId);
    clearRemoteSettings(clientId, pcId);
    clearAppSettings(clientId, pcId);
    forgetPc(pcId);
  };

  return {
    forgetPcAndSettings,
    updateAppSetting,
    updateKeyboardSetting,
    updateRemoteSetting,
    updateTrackpadSetting
  };
}
