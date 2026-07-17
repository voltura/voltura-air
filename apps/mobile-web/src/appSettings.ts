import type { FourthMode } from "./appModeTabs";

export interface AppSettings {
  autoRefresh: boolean;
  clearTextAfterSending: boolean;
  fourthMode: FourthMode;
}

export const defaultAppSettings: AppSettings = {
  autoRefresh: true,
  clearTextAfterSending: true,
  fourthMode: "dictation"
};

export function normalizeAppSettings(value: unknown): AppSettings {
  const candidate = typeof value === "object" && value !== null
    ? value as Partial<Record<keyof AppSettings, unknown>>
    : {};
  return {
    autoRefresh: typeof candidate.autoRefresh === "boolean" ? candidate.autoRefresh : defaultAppSettings.autoRefresh,
    clearTextAfterSending: typeof candidate.clearTextAfterSending === "boolean" ? candidate.clearTextAfterSending : defaultAppSettings.clearTextAfterSending,
    fourthMode: candidate.fourthMode === "presentation" || candidate.fourthMode === "text-transfer" || candidate.fourthMode === "clipboard-read" ? candidate.fourthMode : "dictation"
  };
}
