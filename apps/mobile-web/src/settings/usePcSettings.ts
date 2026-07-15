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
} from "../appStorage";
import { defaultRemoteSettings, type RemoteModeId } from "../remoteSettings";

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

  useEffect(() => {
    setTrackpadState((current) => current.storageKey === trackpadStorageKey
      ? current
      : { settings: loadTrackpadSettings(clientId, pcId), storageKey: trackpadStorageKey });
  }, [clientId, pcId, trackpadStorageKey]);

  useEffect(() => {
    if (trackpadState.storageKey === trackpadStorageKey) {
      localStorage.setItem(trackpadStorageKey, JSON.stringify(trackpadState.settings));
    }
  }, [trackpadState, trackpadStorageKey]);

  useEffect(() => {
    setRemoteState((current) =>
      current.storageKey === remoteStorageKey && (current.isStored || current.settings.mode === (hostDefaultRemoteMode ?? defaultRemoteSettings.mode))
        ? current
        : { ...loadRemoteSettings(clientId, pcId, hostDefaultRemoteMode), storageKey: remoteStorageKey }
    );
  }, [clientId, hostDefaultRemoteMode, pcId, remoteStorageKey]);

  useEffect(() => {
    if (remoteState.storageKey === remoteStorageKey && remoteState.isStored) {
      localStorage.setItem(remoteStorageKey, JSON.stringify(remoteState.settings));
    }
  }, [remoteState, remoteStorageKey]);

  useEffect(() => {
    setAppState((current) => current.storageKey === appStorageKey
      ? current
      : { settings: loadAppSettings(clientId, pcId), storageKey: appStorageKey });
  }, [appStorageKey, clientId, pcId]);

  useEffect(() => {
    if (appState.storageKey === appStorageKey) {
      localStorage.setItem(appStorageKey, JSON.stringify(appState.settings));
    }
  }, [appState, appStorageKey]);

  useEffect(() => {
    localStorage.setItem(keyboardSettingsKey(clientId), JSON.stringify(keyboardSettings));
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
