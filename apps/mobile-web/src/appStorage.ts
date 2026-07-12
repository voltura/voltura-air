import { defaultAppSettings, normalizeAppSettings, type AppSettings } from "./appSettings";
import { defaultKeyboardSettings, normalizeKeyboardSettings, type KeyboardSettings } from "./keyboardSettings";
import { defaultTrackpadSettings, normalizeTrackpadSettings, type TrackpadSettings } from "./gestures";
import { resolveRemoteSettings, type RemoteModeId, type RemoteSettings } from "./remoteSettings";

export type ThemeMode = "system" | "light" | "dark";

const liveKeyboardKey = "voltura-air.liveKeyboard";
const liveKeyboardDefaultMigrationKey = "voltura-air.liveKeyboardDefaultOn";
const themeModeKey = "voltura-air.themeMode";
const autoRefreshSessionPrefix = "voltura-air.autoRefresh";

export function trackpadSettingsKey(clientId: string, pcId: string | null): string {
  return pcId ? `voltura-air.trackpadSettings.${clientId}.${pcId}` : baseTrackpadSettingsKey(clientId);
}

export function remoteSettingsKey(clientId: string, pcId: string | null): string {
  return pcId ? `voltura-air.remoteSettings.${clientId}.${pcId}` : baseRemoteSettingsKey(clientId);
}

export function appSettingsKey(clientId: string, pcId: string | null): string {
  return pcId ? `voltura-air.appSettings.${clientId}.${pcId}` : baseAppSettingsKey(clientId);
}

export function keyboardSettingsKey(clientId: string): string {
  return `voltura-air.keyboardSettings.${clientId}`;
}

export function loadLiveKeyboardDefault(): boolean {
  if (localStorage.getItem(liveKeyboardDefaultMigrationKey) !== "true") {
    localStorage.setItem(liveKeyboardDefaultMigrationKey, "true");
    localStorage.setItem(liveKeyboardKey, "true");
    return true;
  }

  return localStorage.getItem(liveKeyboardKey) !== "false";
}

export function saveLiveKeyboardPreference(enabled: boolean): void {
  localStorage.setItem(liveKeyboardKey, String(enabled));
}

export function loadThemeMode(): ThemeMode {
  const stored = localStorage.getItem(themeModeKey);
  return stored === "light" || stored === "dark" ? stored : "system";
}

export function saveThemeMode(themeMode: ThemeMode): void {
  localStorage.setItem(themeModeKey, themeMode);
}

export function resolveTheme(themeMode: ThemeMode, systemPrefersDark: boolean): "light" | "dark" {
  return themeMode === "system" ? (systemPrefersDark ? "dark" : "light") : themeMode;
}

export function loadTrackpadSettings(clientId: string, pcId: string | null): TrackpadSettings {
  const stored = localStorage.getItem(trackpadSettingsKey(clientId, pcId));
  if (!stored) {
    return defaultTrackpadSettings;
  }

  try {
    return normalizeTrackpadSettings(JSON.parse(stored));
  } catch {
    return defaultTrackpadSettings;
  }
}

export function clearTrackpadSettings(clientId: string, pcId: string): void {
  localStorage.removeItem(trackpadSettingsKey(clientId, pcId));
}

export function loadRemoteSettings(clientId: string, pcId: string | null, hostDefaultMode?: RemoteModeId): { settings: RemoteSettings; isStored: boolean } {
  return resolveRemoteSettings(localStorage.getItem(remoteSettingsKey(clientId, pcId)), hostDefaultMode);
}

export function clearRemoteSettings(clientId: string, pcId: string): void {
  localStorage.removeItem(remoteSettingsKey(clientId, pcId));
}

export function loadAppSettings(clientId: string, pcId: string | null): AppSettings {
  const stored = localStorage.getItem(appSettingsKey(clientId, pcId));
  if (!stored) {
    return defaultAppSettings;
  }

  try {
    return normalizeAppSettings(JSON.parse(stored));
  } catch {
    return defaultAppSettings;
  }
}

export function clearAppSettings(clientId: string, pcId: string): void {
  localStorage.removeItem(appSettingsKey(clientId, pcId));
}

export function loadKeyboardSettings(clientId: string): KeyboardSettings {
  const stored = localStorage.getItem(keyboardSettingsKey(clientId));
  if (!stored) {
    return defaultKeyboardSettings;
  }

  try {
    return normalizeKeyboardSettings(JSON.parse(stored));
  } catch {
    return defaultKeyboardSettings;
  }
}

export function getAutoRefreshSessionKey(clientId: string, pcId: string, webClientBuildId: string): string {
  return `${autoRefreshSessionPrefix}.${clientId}.${pcId}.build.${webClientBuildId}`;
}

function baseTrackpadSettingsKey(clientId: string): string {
  return `voltura-air.trackpadSettings.${clientId}`;
}

function baseRemoteSettingsKey(clientId: string): string {
  return `voltura-air.remoteSettings.${clientId}`;
}

function baseAppSettingsKey(clientId: string): string {
  return `voltura-air.appSettings.${clientId}`;
}
