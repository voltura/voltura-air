import { act, renderHook, waitFor } from "@testing-library/react";
import { StrictMode } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { usePersistentStorage } from "./usePersistentStorage";

afterEach(() => {
  vi.unstubAllGlobals();
  window.localStorage.clear();
});

describe("persistent browser storage", () => {
  it.each(["granted", "prompt", "denied"])(
    "automatically requests on other browsers only for granted permission (%s)",
    async (state) => {
      const persist = vi.fn().mockResolvedValue(true);
      const query = vi.fn().mockResolvedValue({ state });
      vi.stubGlobal("navigator", {
        permissions: { query },
        storage: { persisted: () => Promise.resolve(false), persist },
      });
      const view = renderHook(() => usePersistentStorage(), { wrapper: StrictMode });
      await waitFor(() =>
        expect(view.result.current.storageProtection).toBe(
          state === "granted" ? "enabled" : "available",
        ),
      );
      expect(query).toHaveBeenCalledExactlyOnceWith({ name: "persistent-storage" });
      expect(persist).toHaveBeenCalledTimes(state === "granted" ? 1 : 0);
    },
  );

  it("keeps the manual action when querying permission is unsupported", async () => {
    const persist = vi.fn().mockResolvedValue(true);
    vi.stubGlobal("navigator", {
      permissions: { query: vi.fn().mockRejectedValue(new TypeError("unsupported")) },
      storage: { persisted: () => Promise.resolve(false), persist },
    });
    const view = renderHook(() => usePersistentStorage());
    await waitFor(() => expect(view.result.current.storageProtection).toBe("available"));
    expect(persist).not.toHaveBeenCalled();
    await act(() => view.result.current.protectSavedData());
    expect(view.result.current.storageProtection).toBe("enabled");
  });

  it.each(["unmount", "manual request"])(
    "ignores a permission query completing after %s",
    async (action) => {
      let resolve!: (value: { state: string }) => void;
      const persist = vi.fn().mockResolvedValue(true);
      const query = vi.fn(
        () =>
          new Promise((done) => {
            resolve = done;
          }),
      );
      vi.stubGlobal("navigator", {
        permissions: { query },
        storage: { persisted: () => Promise.resolve(false), persist },
      });
      const view = renderHook(() => usePersistentStorage());
      await waitFor(() => expect(query).toHaveBeenCalledOnce());
      if (action === "unmount") {
        view.unmount();
      } else {
        await act(() => view.result.current.protectSavedData());
      }
      await act(async () => {
        resolve({ state: "granted" });
        await Promise.resolve();
      });
      expect(persist).toHaveBeenCalledTimes(action === "unmount" ? 0 : 1);
    },
  );

  it("requests once automatically on iPhone Safari when storage is not protected", async () => {
    const persisted = vi.fn().mockResolvedValue(false);
    const persist = vi.fn().mockResolvedValue(true);
    vi.stubGlobal("navigator", {
      userAgent:
        "Mozilla/5.0 (iPhone; CPU iPhone OS 27_0 like Mac OS X) AppleWebKit/605.1.15 Version/27.0 Mobile/15E148 Safari/604.1",
      storage: { persisted, persist },
    });
    const view = renderHook(() => usePersistentStorage(), { wrapper: StrictMode });
    await waitFor(() => expect(view.result.current.storageProtection).toBe("enabled"));
    view.rerender();
    expect(persisted).toHaveBeenCalledOnce();
    expect(persist).toHaveBeenCalledOnce();
  });

  it("recognizes iPad desktop mode and keeps other iOS browsers on the explicit action", async () => {
    const persist = vi.fn().mockResolvedValue(true);
    vi.stubGlobal("navigator", {
      userAgent:
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 Version/27.0 Safari/605.1.15",
      platform: "MacIntel",
      maxTouchPoints: 5,
      storage: { persisted: () => Promise.resolve(false), persist },
    });
    const ipad = renderHook(() => usePersistentStorage());
    await waitFor(() => expect(ipad.result.current.storageProtection).toBe("enabled"));
    expect(persist).toHaveBeenCalledOnce();
    ipad.unmount();

    vi.stubGlobal("navigator", {
      userAgent:
        "Mozilla/5.0 (iPhone; CPU iPhone OS 27_0 like Mac OS X) AppleWebKit/605.1.15 CriOS/129.0 Mobile/15E148 Safari/604.1",
      storage: { persisted: () => Promise.resolve(false), persist },
    });
    const chrome = renderHook(() => usePersistentStorage());
    await waitFor(() => expect(chrome.result.current.storageProtection).toBe("available"));
    expect(persist).toHaveBeenCalledOnce();
  });

  it("allows manual retry after an automatic iOS request is declined", async () => {
    const persist = vi.fn().mockResolvedValueOnce(false).mockResolvedValueOnce(true);
    vi.stubGlobal("navigator", {
      userAgent:
        "Mozilla/5.0 (iPhone; CPU iPhone OS 27_0 like Mac OS X) AppleWebKit/605.1.15 Mobile/15E148",
      standalone: true,
      storage: { persisted: () => Promise.resolve(false), persist },
    });
    const view = renderHook(() => usePersistentStorage());
    await waitFor(() => expect(view.result.current.storageProtection).toBe("declined"));
    view.rerender();
    expect(persist).toHaveBeenCalledOnce();
    await act(() => view.result.current.protectSavedData());
    expect(view.result.current.storageProtection).toBe("enabled");
    expect(persist).toHaveBeenCalledTimes(2);
  });

  it("does not automatically request from an iOS embedded web view", async () => {
    const persist = vi.fn();
    vi.stubGlobal("navigator", {
      userAgent:
        "Mozilla/5.0 (iPhone; CPU iPhone OS 27_0 like Mac OS X) AppleWebKit/605.1.15 Mobile/15E148",
      storage: { persisted: () => Promise.resolve(false), persist },
    });
    const view = renderHook(() => usePersistentStorage());
    await waitFor(() => expect(view.result.current.storageProtection).toBe("available"));
    expect(persist).not.toHaveBeenCalled();
  });

  it("reports an automatic iOS request failure without retrying on rerender", async () => {
    const persist = vi.fn().mockRejectedValue(new Error("blocked"));
    vi.stubGlobal("navigator", {
      userAgent:
        "Mozilla/5.0 (iPhone; CPU iPhone OS 27_0 like Mac OS X) AppleWebKit/605.1.15 Version/27.0 Mobile/15E148 Safari/604.1",
      storage: { persisted: () => Promise.resolve(false), persist },
    });
    const view = renderHook(() => usePersistentStorage(), { wrapper: StrictMode });
    await waitFor(() => expect(view.result.current.storageProtection).toBe("failed"));
    view.rerender();
    expect(persist).toHaveBeenCalledOnce();
    view.unmount();
    const reopened = renderHook(() => usePersistentStorage());
    await waitFor(() => expect(reopened.result.current.storageProtection).toBe("available"));
    expect(persist).toHaveBeenCalledOnce();
  });

  it("checks once through effect replay and requests only from the explicit action", async () => {
    const persisted = vi.fn().mockResolvedValue(false);
    const persist = vi.fn().mockResolvedValue(true);
    vi.stubGlobal("navigator", { storage: { persisted, persist } });
    const view = renderHook(() => usePersistentStorage(), { wrapper: StrictMode });
    await act(() => Promise.resolve());
    view.rerender();
    expect(persisted).toHaveBeenCalledOnce();
    expect(persist).not.toHaveBeenCalled();
    expect(view.result.current.storageProtection).toBe("available");
    await act(() => view.result.current.protectSavedData());
    expect(view.result.current.storageProtection).toBe("enabled");
    await act(() => view.result.current.protectSavedData());
    expect(persist).toHaveBeenCalledOnce();
  });

  it("recognizes existing protection without requesting again", async () => {
    const persist = vi.fn();
    vi.stubGlobal("navigator", {
      userAgent:
        "Mozilla/5.0 (iPhone; CPU iPhone OS 27_0 like Mac OS X) AppleWebKit/605.1.15 Version/27.0 Mobile/15E148 Safari/604.1",
      storage: { persisted: () => Promise.resolve(true), persist },
    });
    const view = renderHook(() => usePersistentStorage());
    await act(() => Promise.resolve());
    expect(view.result.current.storageProtection).toBe("enabled");
    await act(() => view.result.current.protectSavedData());
    expect(persist).not.toHaveBeenCalled();
  });

  it("reports unavailable APIs without throwing", async () => {
    vi.stubGlobal("navigator", {});
    const view = renderHook(() => usePersistentStorage());
    expect(view.result.current.storageProtection).toBe("unavailable");
    await act(() => view.result.current.protectSavedData());
    expect(view.result.current.storageProtection).toBe("unavailable");
  });

  it("allows deliberate retry after a denied or failed request", async () => {
    const persist = vi
      .fn()
      .mockResolvedValueOnce(false)
      .mockRejectedValueOnce(new Error("blocked"))
      .mockImplementationOnce(() => {
        throw new Error("blocked");
      })
      .mockResolvedValueOnce(true);
    vi.stubGlobal("navigator", { storage: { persisted: () => Promise.resolve(false), persist } });
    const view = renderHook(() => usePersistentStorage());
    await act(() => Promise.resolve());
    for (const expected of ["declined", "failed", "failed", "enabled"]) {
      await act(() => view.result.current.protectSavedData());
      expect(view.result.current.storageProtection).toBe(expected);
    }
    expect(persist).toHaveBeenCalledTimes(4);
  });

  it("offers an explicit request when the initial status check throws", async () => {
    const persist = vi.fn().mockResolvedValue(true);
    vi.stubGlobal("navigator", {
      storage: {
        persisted: () => {
          throw new Error("blocked");
        },
        persist,
      },
    });
    const view = renderHook(() => usePersistentStorage());
    await act(() => Promise.resolve());
    expect(view.result.current.storageProtection).toBe("failed");
    expect(persist).not.toHaveBeenCalled();
    await act(() => view.result.current.protectSavedData());
    expect(view.result.current.storageProtection).toBe("enabled");
  });

  it("deduplicates requests, preserves activation, and ignores a late initial check", async () => {
    let resolveCheck!: (value: boolean) => void;
    let resolveRequest!: (value: boolean) => void;
    const persist = vi.fn(
      () =>
        new Promise<boolean>((resolve) => {
          resolveRequest = resolve;
        }),
    );
    vi.stubGlobal("navigator", {
      storage: {
        persisted: () =>
          new Promise<boolean>((resolve) => {
            resolveCheck = resolve;
          }),
        persist,
      },
    });
    const view = renderHook(() => usePersistentStorage());
    await act(() => Promise.resolve());
    let first!: Promise<void>;
    act(() => {
      first = view.result.current.protectSavedData();
      expect(persist).toHaveBeenCalledOnce();
      void view.result.current.protectSavedData();
    });
    expect(view.result.current.storageProtection).toBe("requesting");
    expect(persist).toHaveBeenCalledOnce();
    await act(async () => {
      resolveRequest(true);
      await first;
    });
    await act(async () => {
      resolveCheck(false);
      await Promise.resolve();
    });
    expect(view.result.current.storageProtection).toBe("enabled");
  });

  it("does not update or start another request after unmount", async () => {
    let resolve!: (value: boolean) => void;
    const persist = vi.fn(
      () =>
        new Promise<boolean>((done) => {
          resolve = done;
        }),
    );
    vi.stubGlobal("navigator", { storage: { persisted: () => Promise.resolve(false), persist } });
    const view = renderHook(() => usePersistentStorage());
    await act(() => Promise.resolve());
    let pending!: Promise<void>;
    act(() => {
      pending = view.result.current.protectSavedData();
    });
    const action = view.result.current.protectSavedData;
    view.unmount();
    await act(async () => {
      resolve(true);
      await pending;
      await action();
    });
    expect(persist).toHaveBeenCalledOnce();
  });
});
