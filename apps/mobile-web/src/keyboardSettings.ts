export type KeyboardSettings = {
  showFunctionKeys: boolean;
  showControlKeys: boolean;
  showArrowKeys: boolean;
  showSleepButton: boolean;
};

export const defaultKeyboardSettings: KeyboardSettings = {
  showFunctionKeys: false,
  showControlKeys: true,
  showArrowKeys: true,
  showSleepButton: true
};

export function normalizeKeyboardSettings(value: Partial<KeyboardSettings>): KeyboardSettings {
  return {
    showFunctionKeys: typeof value.showFunctionKeys === "boolean" ? value.showFunctionKeys : defaultKeyboardSettings.showFunctionKeys,
    showControlKeys: typeof value.showControlKeys === "boolean" ? value.showControlKeys : defaultKeyboardSettings.showControlKeys,
    showArrowKeys: typeof value.showArrowKeys === "boolean" ? value.showArrowKeys : defaultKeyboardSettings.showArrowKeys,
    showSleepButton: typeof value.showSleepButton === "boolean" ? value.showSleepButton : defaultKeyboardSettings.showSleepButton
  };
}
