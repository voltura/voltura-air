import { useEffect, useState } from "react";
import { loadThemeMode, resolveTheme, saveThemeMode, type ThemeMode } from "../foundation/settings/appStorage";
import { uiThemeColors } from "../ui/tokens.g";

export function useAppTheme() {
  const [themeMode, setThemeMode] = useState<ThemeMode>(() => loadThemeMode());
  const [systemPrefersDark, setSystemPrefersDark] = useState(() => window.matchMedia("(prefers-color-scheme: dark)").matches);

  useEffect(() => {
    const mediaQuery = window.matchMedia("(prefers-color-scheme: dark)");
    const onChange = () => { setSystemPrefersDark(mediaQuery.matches); };
    mediaQuery.addEventListener("change", onChange);
    return () => { mediaQuery.removeEventListener("change", onChange); };
  }, []);

  useEffect(() => {
    saveThemeMode(themeMode);
    if (themeMode === "system") {
      document.documentElement.removeAttribute("data-theme");
    } else {
      document.documentElement.dataset.theme = themeMode;
    }

    const resolvedTheme = resolveTheme(themeMode, systemPrefersDark);
    document.querySelector('meta[name="theme-color"]')?.setAttribute("content", uiThemeColors[resolvedTheme].background);
  }, [systemPrefersDark, themeMode]);

  return { setThemeMode, themeMode };
}
