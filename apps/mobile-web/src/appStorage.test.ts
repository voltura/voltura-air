import { beforeEach, describe, expect, it, vi } from "vitest";
import { defaultAppSettings } from "./appSettings";
import {
  appSettingsKey,
  getAutoRefreshSessionKey,
  loadAppSettings,
  loadLiveKeyboardDefault,
  loadThemeMode,
  resolveTheme,
  saveThemeMode
} from "./appStorage";

function createStorage(): Storage {
  const items = new Map<string, string>();
  return {
    get length() {
      return items.size;
    },
    clear: () => { items.clear(); },
    getItem: (key: string) => items.get(key) ?? null,
    key: (index: number) => Array.from(items.keys())[index] ?? null,
    removeItem: (key: string) => {
      items.delete(key);
    },
    setItem: (key: string, value: string) => {
      items.set(key, String(value));
    }
  };
}

beforeEach(() => {
  vi.stubGlobal("localStorage", createStorage());
});

describe("appStorage", () => {
  it("defaults the live keyboard to enabled and preserves an explicit preference", () => {
    expect(loadLiveKeyboardDefault()).toBe(true);

    localStorage.setItem("voltura-air.liveKeyboard", "false");
    expect(loadLiveKeyboardDefault()).toBe(false);
  });

  it("loads valid app settings and falls back for invalid stored JSON", () => {
    localStorage.setItem(appSettingsKey("client-a", "pc-a"), JSON.stringify({ autoRefresh: false }));
    expect(loadAppSettings("client-a", "pc-a")).toEqual({
      autoRefresh: false,
      clearTextAfterSending: true,
      fourthMode: "dictation"
    });

    localStorage.setItem(appSettingsKey("client-a", "pc-a"), "{not-json");
    expect(loadAppSettings("client-a", "pc-a")).toEqual(defaultAppSettings);
  });

  it("keeps theme persistence and resolution in one place", () => {
    expect(loadThemeMode()).toBe("system");
    saveThemeMode("dark");

    expect(loadThemeMode()).toBe("dark");
    expect(resolveTheme("system", true)).toBe("dark");
    expect(resolveTheme("system", false)).toBe("light");
    expect(resolveTheme("light", true)).toBe("light");
  });

  it("scopes auto refresh by web build ID", () => {
    expect(getAutoRefreshSessionKey("client-a", "pc-a", "build-a")).toBe("voltura-air.autoRefresh.client-a.pc-a.build.build-a");
  });
});
