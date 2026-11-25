import type { TrackpadSettings } from "./gestures";

const clickVibrationDurationMs = 30;

export function supportsHapticFeedback(): boolean {
  return typeof navigator.vibrate === "function";
}

export function triggerHapticFeedback(settings: TrackpadSettings): boolean {
  if (!settings.hapticFeedback || !supportsHapticFeedback()) {
    return false;
  }
  try {
    return navigator.vibrate(clickVibrationDurationMs);
  } catch {
    return false;
  }
}
