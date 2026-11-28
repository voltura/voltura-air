export const remoteModeIds = ["standard", "youtube", "kodi"] as const;

export type RemoteModeId = (typeof remoteModeIds)[number];

export interface RemoteSettings {
  navigationRing: boolean;
  mode: RemoteModeId;
  openYoutube: boolean;
  showBrowserHelpers: boolean;
  showWindowHelpers: boolean;
  startKodi: boolean;
}

export const defaultRemoteSettings: RemoteSettings = {
  navigationRing: true,
  mode: "standard",
  openYoutube: true,
  showBrowserHelpers: true,
  showWindowHelpers: true,
  startKodi: true,
};

export function normalizeRemoteSettings(value: unknown = {}): RemoteSettings {
  const candidate =
    typeof value === "object" && value !== null
      ? (value as Partial<Record<keyof RemoteSettings | "youtubeMode", unknown>>)
      : {};
  return {
    navigationRing:
      typeof candidate.navigationRing === "boolean"
        ? candidate.navigationRing
        : defaultRemoteSettings.navigationRing,
    mode: normalizeRemoteMode(candidate.mode, candidate.youtubeMode),
    openYoutube:
      typeof candidate.openYoutube === "boolean"
        ? candidate.openYoutube
        : defaultRemoteSettings.openYoutube,
    showBrowserHelpers:
      typeof candidate.showBrowserHelpers === "boolean"
        ? candidate.showBrowserHelpers
        : defaultRemoteSettings.showBrowserHelpers,
    showWindowHelpers:
      typeof candidate.showWindowHelpers === "boolean"
        ? candidate.showWindowHelpers
        : defaultRemoteSettings.showWindowHelpers,
    startKodi:
      typeof candidate.startKodi === "boolean"
        ? candidate.startKodi
        : defaultRemoteSettings.startKodi,
  };
}

export function normalizeRemoteMode(value: unknown, legacyYoutubeMode?: unknown): RemoteModeId {
  if (isRemoteModeId(value)) {
    return value;
  }

  if (legacyYoutubeMode === true) {
    return "youtube";
  }

  return defaultRemoteSettings.mode;
}

export function isRemoteModeId(value: unknown): value is RemoteModeId {
  return typeof value === "string" && remoteModeIds.includes(value as RemoteModeId);
}

export function resolveRemoteSettings(
  stored: string | null,
  hostDefaultMode?: RemoteModeId,
): { settings: RemoteSettings; isStored: boolean } {
  if (stored) {
    try {
      const parsed: unknown = JSON.parse(stored);
      if (typeof parsed === "object" && parsed !== null && !Array.isArray(parsed)) {
        return { isStored: true, settings: normalizeRemoteSettings(parsed) };
      }
    } catch {
      // Unreadable saved settings use the host's default mode.
    }
  }
  return {
    isStored: false,
    settings: { ...defaultRemoteSettings, mode: hostDefaultMode ?? defaultRemoteSettings.mode },
  };
}
