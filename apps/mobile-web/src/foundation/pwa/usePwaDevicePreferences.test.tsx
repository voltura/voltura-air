import { act, renderHook } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { loadKeepDeviceScreenOn, saveKeepDeviceScreenOn } from "../settings/appStorage";
import type { ConnectionState } from "../connection/connectionTypes";
import { usePwaLifecycle } from "./usePwaLifecycle";

beforeEach(() => {
  localStorage.clear();
  saveKeepDeviceScreenOn(false);
  vi.stubGlobal("matchMedia", () => ({ matches: false }));
  Object.defineProperty(document, "visibilityState", { configurable: true, value: "visible" });
});
afterEach(() => {
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

function options(state: ConnectionState = "paired", pcId = "pc-a") {
  return {
    activePc: { id: pcId, name: "PC", customName: false, url: "https://pc.local" },
    autoRefresh: false,
    clientId: "device-a",
    hostStatus: null,
    state,
  };
}

describe("device screen preference", () => {
  it("defaults missing and invalid values to off without changing existing settings", () => {
    localStorage.removeItem("voltura-air.keepDeviceScreenOn");
    expect(loadKeepDeviceScreenOn()).toBe(false);
    for (const invalid of ["TRUE", "1", "{}", "null", "false"]) {
      localStorage.setItem("voltura-air.keepDeviceScreenOn", invalid);
      expect(loadKeepDeviceScreenOn()).toBe(false);
    }
    const existing = '{"autoRefresh":false}';
    localStorage.setItem("voltura-air.appSettings.device-a.pc-a", existing);
    saveKeepDeviceScreenOn(true);
    expect(loadKeepDeviceScreenOn()).toBe(true);
    expect(localStorage.getItem("voltura-air.appSettings.device-a.pc-a")).toBe(existing);
  });

  it("persists across PCs and remounts, but locks only while paired", async () => {
    const locks: { release: ReturnType<typeof vi.fn> }[] = [];
    const request = vi.fn(() => {
      const lock = { release: vi.fn(() => Promise.resolve()) };
      locks.push(lock);
      return Promise.resolve(lock);
    });
    vi.stubGlobal("navigator", { wakeLock: { request } });
    const view = renderHook(({ state, pc }) => usePwaLifecycle(options(state, pc)), {
      initialProps: { state: "disconnected" as ConnectionState, pc: "pc-a" },
    });
    expect(view.result.current.keepDeviceScreenOn).toBe(false);
    act(() => view.result.current.setKeepDeviceScreenOn(true));
    expect(request).not.toHaveBeenCalled();
    await act(async () => {
      view.rerender({ state: "paired", pc: "pc-a" });
      await Promise.resolve();
    });
    expect(request).toHaveBeenCalledOnce();
    view.rerender({ state: "paired", pc: "pc-b" });
    expect(view.result.current.keepDeviceScreenOn).toBe(true);
    expect(request).toHaveBeenCalledOnce();
    view.rerender({ state: "disconnected", pc: "pc-b" });
    expect(locks[0]!.release).toHaveBeenCalledOnce();
    view.unmount();
    const next = renderHook(() => usePwaLifecycle(options()));
    await act(() => Promise.resolve());
    expect(next.result.current.keepDeviceScreenOn).toBe(true);
    expect(request).toHaveBeenCalledTimes(2);
    next.unmount();
    expect(locks[1]!.release).toHaveBeenCalledOnce();
  });

  it("remains usable when browser storage writes are blocked", () => {
    const view = renderHook(() => usePwaLifecycle(options()));
    vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => {
      throw new Error("blocked");
    });
    act(() => view.result.current.setKeepDeviceScreenOn(true));
    expect(view.result.current.keepDeviceScreenOn).toBe(true);
    expect(loadKeepDeviceScreenOn()).toBe(true);
    view.unmount();
  });
});
