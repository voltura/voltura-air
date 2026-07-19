import { StrictMode } from "react";
import { act, renderHook } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { useAppNavigation } from "./useAppNavigation";

const trackpadSettings = {
  enableSplitMode: false,
  splitShowModeButtons: true,
  splitShowStatusRow: true
};

const originalInnerWidth = Object.getOwnPropertyDescriptor(window, "innerWidth");
const originalInnerHeight = Object.getOwnPropertyDescriptor(window, "innerHeight");
const originalMaxTouchPoints = Object.getOwnPropertyDescriptor(navigator, "maxTouchPoints");
const originalScreenWidth = Object.getOwnPropertyDescriptor(screen, "width");
const originalScreenHeight = Object.getOwnPropertyDescriptor(screen, "height");
const originalScreenOrientation = Object.getOwnPropertyDescriptor(screen, "orientation");

function restoreProperty(target: object, key: PropertyKey, descriptor: PropertyDescriptor | undefined) {
  if (descriptor) {
    Object.defineProperty(target, key, descriptor);
  } else {
    Reflect.deleteProperty(target, key);
  }
}

afterEach(() => {
  restoreProperty(window, "innerWidth", originalInnerWidth);
  restoreProperty(window, "innerHeight", originalInnerHeight);
  restoreProperty(navigator, "maxTouchPoints", originalMaxTouchPoints);
  restoreProperty(screen, "width", originalScreenWidth);
  restoreProperty(screen, "height", originalScreenHeight);
  restoreProperty(screen, "orientation", originalScreenOrientation);
  vi.restoreAllMocks();
});

function configureTouchScreen(width: number, height: number, orientationType: string): EventTarget {
  const orientation = new EventTarget();
  Object.defineProperty(orientation, "type", { configurable: true, value: orientationType });
  Object.defineProperty(navigator, "maxTouchPoints", { configurable: true, value: 1 });
  Object.defineProperty(screen, "width", { configurable: true, value: width });
  Object.defineProperty(screen, "height", { configurable: true, value: height });
  Object.defineProperty(screen, "orientation", { configurable: true, value: orientation });
  Object.defineProperty(window, "innerWidth", { configurable: true, value: width });
  Object.defineProperty(window, "innerHeight", { configurable: true, value: height });
  return orientation;
}

function renderNavigation(onEnterRemote: () => void, strict = false) {
  return renderHook(() => useAppNavigation({
    fourthMode: "dictation",
    isPaired: true,
    onEnterRemote,
    presentationAvailable: true,
    supportsGestureDebug: false,
    trackpadSettings
  }), strict ? { wrapper: StrictMode } : undefined);
}

describe("useAppNavigation remote entry ownership", () => {
  it("runs remote entry only for actual transitions while preserving active-tab collapse", () => {
    const onEnterRemote = vi.fn();
    const { result } = renderNavigation(onEnterRemote);

    act(() => { result.current.selectModeTab("remote"); });
    expect(onEnterRemote).toHaveBeenCalledTimes(1);
    expect(result.current.tab).toBe("remote");

    act(() => { result.current.selectModeTab("remote"); });
    expect(onEnterRemote).toHaveBeenCalledTimes(1);
    expect(result.current.isBottomModeNavigationVisible).toBe(false);
  });

  it("runs entry again after navigating away and back", () => {
    const onEnterRemote = vi.fn();
    const { result } = renderNavigation(onEnterRemote);

    act(() => { result.current.selectModeTab("remote"); });
    act(() => { result.current.selectModeTab("trackpad"); });
    act(() => { result.current.selectModeTab("remote"); });

    expect(onEnterRemote).toHaveBeenCalledTimes(2);
  });

  it("allows a settings-owned mode transition without also running entry behavior", () => {
    const onEnterRemote = vi.fn();
    const { result } = renderNavigation(onEnterRemote);

    act(() => { result.current.selectModeTab("remote", "settings"); });

    expect(result.current.tab).toBe("remote");
    expect(onEnterRemote).not.toHaveBeenCalled();
  });

  it("does not run entry from Strict Mode renders or rerenders", () => {
    const onEnterRemote = vi.fn();
    const { rerender } = renderNavigation(onEnterRemote, true);

    rerender();
    rerender();

    expect(onEnterRemote).not.toHaveBeenCalled();
  });
});

describe("useAppNavigation split orientation", () => {
  it("keeps Split mode off when a portrait touch screen only loses viewport height", () => {
    configureTouchScreen(800, 1200, "portrait-primary");
    const { result } = renderHook(() => useAppNavigation({
      fourthMode: "dictation",
      isPaired: true,
      onEnterRemote: vi.fn(),
      presentationAvailable: true,
      supportsGestureDebug: false,
      trackpadSettings: { ...trackpadSettings, enableSplitMode: true }
    }));
    expect(result.current.shouldShowSplitMode).toBe(false);

    Object.defineProperty(window, "innerHeight", { configurable: true, value: 500 });
    act(() => { window.dispatchEvent(new Event("resize")); });

    expect(result.current.shouldShowSplitMode).toBe(false);
  });

  it("updates Split mode on an actual touch-screen orientation event", () => {
    const orientation = configureTouchScreen(800, 1200, "portrait-primary");
    const { result } = renderHook(() => useAppNavigation({
      fourthMode: "dictation",
      isPaired: true,
      onEnterRemote: vi.fn(),
      presentationAvailable: true,
      supportsGestureDebug: false,
      trackpadSettings: { ...trackpadSettings, enableSplitMode: true }
    }));

    Object.defineProperty(orientation, "type", { configurable: true, value: "landscape-primary" });
    Object.defineProperty(screen, "width", { configurable: true, value: 1200 });
    Object.defineProperty(screen, "height", { configurable: true, value: 800 });
    Object.defineProperty(window, "innerWidth", { configurable: true, value: 1200 });
    Object.defineProperty(window, "innerHeight", { configurable: true, value: 800 });
    act(() => { orientation.dispatchEvent(new Event("change")); });

    expect(result.current.shouldShowSplitMode).toBe(true);
  });

  it("removes resize and orientation listeners on unmount", () => {
    const orientation = configureTouchScreen(800, 1200, "portrait-primary");
    const orientationAdd = vi.spyOn(orientation, "addEventListener");
    const orientationRemove = vi.spyOn(orientation, "removeEventListener");
    const windowAdd = vi.spyOn(window, "addEventListener");
    const windowRemove = vi.spyOn(window, "removeEventListener");
    const { unmount } = renderHook(() => useAppNavigation({
      fourthMode: "dictation",
      isPaired: true,
      onEnterRemote: vi.fn(),
      presentationAvailable: true,
      supportsGestureDebug: false,
      trackpadSettings
    }));
    const resizeListener = windowAdd.mock.calls.find(([type]) => type === "resize")?.[1];
    const orientationListener = orientationAdd.mock.calls.find(([type]) => type === "change")?.[1];

    unmount();

    expect(windowRemove).toHaveBeenCalledWith("resize", resizeListener);
    expect(orientationRemove).toHaveBeenCalledWith("change", orientationListener);
  });
});
