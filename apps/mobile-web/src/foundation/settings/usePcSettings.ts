import { useEffect, useMemo, useState } from "react";
import {
  appSettingsKey,
  keyboardSettingsKey,
  loadAppSettings,
  loadKeyboardSettings,
  loadRemoteSettings,
  loadTrackpadSettings,
  remoteSettingsKey,
  trackpadSettingsKey
} from "./appStorage";
import { defaultRemoteSettings, type RemoteModeId } from "./remoteSettings";
import { writeLocalStorage } from "../platform/browserStorage";

export function usePcSettings(
  clientId: string,
  pcId: string | null,
  hostDefaultRemoteMode?: RemoteModeId,
  hostPointerSpeed?: number
) {
  const [keyboardSettings, setKeyboardSettings] = useState(() => loadKeyboardSettings(clientId));
  const trackpadStorageKey = useMemo(() => trackpadSettingsKey(clientId, pcId), [clientId, pcId]);
  const [trackpadState, setTrackpadState] = useState(() => ({ settings: loadTrackpadSettings(clientId, pcId), storageKey: trackpadStorageKey }));
  const remoteStorageKey = useMemo(() => remoteSettingsKey(clientId, pcId), [clientId, pcId]);
  const [remoteState, setRemoteState] = useState(() => ({ ...loadRemoteSettings(clientId, pcId, hostDefaultRemoteMode), storageKey: remoteStorageKey }));
  const appStorageKey = useMemo(() => appSettingsKey(clientId, pcId), [clientId, pcId]);
  const [appState, setAppState] = useState(() => ({ settings: loadAppSettings(clientId, pcId), storageKey: appStorageKey }));

  if (trackpadState.storageKey !== trackpadStorageKey) {
    setTrackpadState({ settings: loadTrackpadSettings(clientId, pcId), storageKey: trackpadStorageKey });
  }

  useEffect(() => {
    if (trackpadState.storageKey === trackpadStorageKey) {
      writeLocalStorage(trackpadStorageKey, JSON.stringify(trackpadState.settings));
    }
  }, [trackpadState, trackpadStorageKey]);

  if (remoteState.storageKey !== remoteStorageKey || (!remoteState.isStored && remoteState.settings.mode !== (hostDefaultRemoteMode ?? defaultRemoteSettings.mode))) {
    setRemoteState({ ...loadRemoteSettings(clientId, pcId, hostDefaultRemoteMode), storageKey: remoteStorageKey });
  }

  useEffect(() => {
    if (remoteState.storageKey === remoteStorageKey && remoteState.isStored) {
      writeLocalStorage(remoteStorageKey, JSON.stringify(remoteState.settings));
    }
  }, [remoteState, remoteStorageKey]);

  if (appState.storageKey !== appStorageKey) {
    setAppState({ settings: loadAppSettings(clientId, pcId), storageKey: appStorageKey });
  }

  useEffect(() => {
    if (appState.storageKey === appStorageKey) {
      writeLocalStorage(appStorageKey, JSON.stringify(appState.settings));
    }
  }, [appState, appStorageKey]);

  useEffect(() => {
    writeLocalStorage(keyboardSettingsKey(clientId), JSON.stringify(keyboardSettings));
  }, [clientId, keyboardSettings]);

  const trackpadSettings = trackpadState.settings;
  const effectiveTrackpadSettings = useMemo(
    () => ({
      ...trackpadSettings,
      ...(typeof hostPointerSpeed === "number" ? { pointerSpeed: hostPointerSpeed } : {})
    }),
    [hostPointerSpeed, trackpadSettings]
  );

  return {
    appSettings: appState.settings,
    effectiveTrackpadSettings,
    keyboardSettings,
    remoteSettings: remoteState.settings,
    setAppSettingsState: setAppState,
    setKeyboardSettings,
    setRemoteSettingsState: setRemoteState,
    setTrackpadSettingsState: setTrackpadState,
    trackpadSettings
  };
}
