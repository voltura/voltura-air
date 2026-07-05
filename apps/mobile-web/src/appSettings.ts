export type AppSettings = {
  autoRefresh: boolean;
};

export const defaultAppSettings: AppSettings = {
  autoRefresh: true
};

export function normalizeAppSettings(value: Partial<AppSettings>): AppSettings {
  return {
    autoRefresh: typeof value.autoRefresh === "boolean" ? value.autoRefresh : defaultAppSettings.autoRefresh
  };
}
