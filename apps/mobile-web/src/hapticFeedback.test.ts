import { afterEach, describe, expect, it, vi } from "vitest";
import { defaultTrackpadSettings } from "./gestures";
import { supportsHapticFeedback, triggerHapticFeedback } from "./hapticFeedback";

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("haptic feedback", () => {
  it("uses a noticeable click vibration when enabled", () => {
    const vibrate = vi.fn(() => true);
    vi.stubGlobal("navigator", { vibrate });

    expect(supportsHapticFeedback()).toBe(true);
    expect(triggerHapticFeedback({ ...defaultTrackpadSettings, hapticFeedback: true })).toBe(true);
    expect(vibrate).toHaveBeenCalledExactlyOnceWith(30);
  });

  it("does not vibrate when the setting is disabled", () => {
    const vibrate = vi.fn(() => true);
    vi.stubGlobal("navigator", { vibrate });

    expect(triggerHapticFeedback(defaultTrackpadSettings)).toBe(false);
    expect(vibrate).not.toHaveBeenCalled();
  });

  it("reports unsupported browsers without throwing", () => {
    vi.stubGlobal("navigator", {});

    expect(supportsHapticFeedback()).toBe(false);
    expect(triggerHapticFeedback({ ...defaultTrackpadSettings, hapticFeedback: true })).toBe(false);
  });
});
