export const remoteModeIds = ["standard", "youtube", "kodi"] as const;

export type RemoteModeId = (typeof remoteModeIds)[number];

export type RemoteSettings = {
  navigationRing: boolean;
  mode: RemoteModeId;
};

export const defaultRemoteSettings: RemoteSettings = {
  navigationRing: true,
  mode: "standard"
};

export function normalizeRemoteSettings(value: Partial<RemoteSettings> & { youtubeMode?: unknown } = {}): RemoteSettings {
  return {
    navigationRing: typeof value.navigationRing === "boolean" ? value.navigationRing : defaultRemoteSettings.navigationRing,
    mode: normalizeRemoteMode(value.mode, value.youtubeMode)
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

export function resolveRemoteSettings(stored: string | null, hostDefaultMode?: RemoteModeId): { settings: RemoteSettings; isStored: boolean } {
  if (!stored) {
    return {
      isStored: false,
      settings: { ...defaultRemoteSettings, mode: hostDefaultMode ?? defaultRemoteSettings.mode }
    };
  }

  try {
    return { isStored: true, settings: normalizeRemoteSettings(JSON.parse(stored)) };
  } catch {
    return {
      isStored: false,
      settings: { ...defaultRemoteSettings, mode: hostDefaultMode ?? defaultRemoteSettings.mode }
    };
  }
}
