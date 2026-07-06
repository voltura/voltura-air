import { describe, expect, it } from "vitest";
import { defaultRemoteSettings, normalizeRemoteSettings, resolveRemoteSettings } from "./remoteSettings";

describe("normalizeRemoteSettings", () => {
  it("defaults the navigation ring on for old stored settings", () => {
    expect(normalizeRemoteSettings({})).toEqual(defaultRemoteSettings);
    expect(defaultRemoteSettings).toEqual({
      navigationRing: true,
      mode: "standard",
      openYoutube: true,
      startKodi: true
    });
  });

  it("preserves disabled navigation ring settings", () => {
    expect(normalizeRemoteSettings({ navigationRing: false }).navigationRing).toBe(false);
  });

  it("preserves stored remote mode settings", () => {
    expect(normalizeRemoteSettings({ mode: "kodi" }).mode).toBe("kodi");
  });

  it("preserves local launch action settings", () => {
    expect(normalizeRemoteSettings({ openYoutube: false, startKodi: false })).toMatchObject({
      openYoutube: false,
      startKodi: false
    });
  });

  it("migrates enabled legacy YouTube mode settings", () => {
    expect(normalizeRemoteSettings({ youtubeMode: true }).mode).toBe("youtube");
  });

  it("falls back to standard mode for unknown stored modes", () => {
    expect(normalizeRemoteSettings({ mode: "plex" as never }).mode).toBe("standard");
  });

  it("uses the host default when there is no stored local remote override", () => {
    expect(resolveRemoteSettings(null, "kodi")).toEqual({
      isStored: false,
      settings: {
        navigationRing: true,
        mode: "kodi",
        openYoutube: true,
        startKodi: true
      }
    });
  });

  it("keeps the stored local remote mode over the host default", () => {
    expect(resolveRemoteSettings(JSON.stringify({ navigationRing: false, mode: "youtube" }), "kodi")).toEqual({
      isStored: true,
      settings: {
        navigationRing: false,
        mode: "youtube",
        openYoutube: true,
        startKodi: true
      }
    });
  });
});
