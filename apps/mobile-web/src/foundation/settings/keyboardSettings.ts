export interface KeyboardSettings {
  showFunctionKeys: boolean;
  showControlKeys: boolean;
  showArrowKeys: boolean;
  showSleepButton: boolean;
}

export const defaultKeyboardSettings: KeyboardSettings = {
  showFunctionKeys: false,
  showControlKeys: true,
  showArrowKeys: true,
  showSleepButton: true
};

export function normalizeKeyboardSettings(value: unknown): KeyboardSettings {
  const candidate = typeof value === "object" && value !== null
    ? value as Partial<Record<keyof KeyboardSettings, unknown>>
    : {};
  return {
    showFunctionKeys: typeof candidate.showFunctionKeys === "boolean" ? candidate.showFunctionKeys : defaultKeyboardSettings.showFunctionKeys,
    showControlKeys: typeof candidate.showControlKeys === "boolean" ? candidate.showControlKeys : defaultKeyboardSettings.showControlKeys,
    showArrowKeys: typeof candidate.showArrowKeys === "boolean" ? candidate.showArrowKeys : defaultKeyboardSettings.showArrowKeys,
    showSleepButton: typeof candidate.showSleepButton === "boolean" ? candidate.showSleepButton : defaultKeyboardSettings.showSleepButton
  };
}
