import { act, renderHook } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { useOneShotHint } from "./useOneShotHint";

afterEach(() => {
  vi.useRealTimers();
});

describe("useOneShotHint", () => {
  it("shows once and ignores later show requests", () => {
    const { result } = renderHook(() => useOneShotHint());

    act(() => { result.current.showOnce(); });
    expect(result.current.open).toBe(true);
    expect(result.current.hasShown).toBe(true);

    act(() => { result.current.dismiss(); });
    expect(result.current.open).toBe(false);

    act(() => { result.current.showOnce(); });
    expect(result.current.open).toBe(false);
  });

  it("auto-dismisses and clears its timer", () => {
    vi.useFakeTimers();
    const clearTimeoutSpy = vi.spyOn(window, "clearTimeout");
    const { result, unmount } = renderHook(() => useOneShotHint({ autoHideMs: 4000 }));

    act(() => { result.current.showOnce(); });
    expect(result.current.open).toBe(true);

    act(() => { vi.advanceTimersByTime(4000); });
    expect(result.current.open).toBe(false);

    act(() => { result.current.showOnce(); });
    unmount();

    expect(clearTimeoutSpy).toHaveBeenCalled();
  });
});
