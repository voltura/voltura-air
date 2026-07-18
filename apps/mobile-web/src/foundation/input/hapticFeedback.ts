import type { TrackpadSettings } from "./gestures";

const clickVibrationDurationMs = 30;

export function supportsHapticFeedback(): boolean {
  return typeof navigator.vibrate === "function";
}

export function triggerHapticFeedback(settings: TrackpadSettings): boolean {
  return settings.hapticFeedback && supportsHapticFeedback()
    ? navigator.vibrate(clickVibrationDurationMs)
    : false;
}
