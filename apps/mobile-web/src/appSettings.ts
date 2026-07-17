import type { FourthMode } from "./appModeTabs";

export type AppSettings = {
  autoRefresh: boolean;
  clearTextAfterSending: boolean;
  fourthMode: FourthMode;
};

export const defaultAppSettings: AppSettings = {
  autoRefresh: true,
  clearTextAfterSending: true,
  fourthMode: "dictation"
};

export function normalizeAppSettings(value: Partial<AppSettings>): AppSettings {
  return {
    autoRefresh: typeof value.autoRefresh === "boolean" ? value.autoRefresh : defaultAppSettings.autoRefresh,
    clearTextAfterSending: typeof value.clearTextAfterSending === "boolean" ? value.clearTextAfterSending : defaultAppSettings.clearTextAfterSending,
    fourthMode: value.fourthMode === "presentation" || value.fourthMode === "text-transfer" || value.fourthMode === "clipboard-read" ? value.fourthMode : "dictation"
  };
}
