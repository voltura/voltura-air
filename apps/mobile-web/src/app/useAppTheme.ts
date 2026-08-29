import { useEffect, useState } from "react";
import {
  loadThemeMode,
  resolveTheme,
  saveThemeMode,
  type ThemeMode,
} from "../foundation/settings/appStorage";
import { uiThemeColors } from "../ui/tokens.g";

export function useAppTheme(hostAccentColor?: string | null, hostAccentColorSupported = false) {
  const [themeMode, setThemeMode] = useState<ThemeMode>(() => loadThemeMode());
  const [systemPrefersDark, setSystemPrefersDark] = useState(
    () => window.matchMedia("(prefers-color-scheme: dark)").matches,
  );

  useEffect(() => {
    const mediaQuery = window.matchMedia("(prefers-color-scheme: dark)");
    const onChange = () => {
      setSystemPrefersDark(mediaQuery.matches);
    };
    mediaQuery.addEventListener("change", onChange);
    return () => {
      mediaQuery.removeEventListener("change", onChange);
    };
  }, []);

  const accentColor = hostAccentColorSupported ? (hostAccentColor ?? null) : null;

  useEffect(() => {
    saveThemeMode(themeMode);
    if (themeMode === "system") {
      document.documentElement.removeAttribute("data-theme");
    } else {
      document.documentElement.dataset.theme = themeMode;
    }

    const resolvedTheme = resolveTheme(themeMode, systemPrefersDark);
    void import("../foundation/settings/accentColor").then(
      ({ applyAccentColor, applyCachedAccentColor }) => {
        if (hostAccentColorSupported) {
          applyAccentColor(accentColor, resolvedTheme);
        } else {
          applyCachedAccentColor(resolvedTheme);
        }
      },
    );
    document
      .querySelector('meta[name="theme-color"]')
      ?.setAttribute("content", uiThemeColors[resolvedTheme].background);
  }, [accentColor, hostAccentColorSupported, systemPrefersDark, themeMode]);

  return { accentColor, setThemeMode, themeMode };
}
