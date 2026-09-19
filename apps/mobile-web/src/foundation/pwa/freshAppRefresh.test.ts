import { describe, expect, it, vi } from "vitest";
import {
  clearChunkLoadUpdateNotice,
  hasChunkLoadUpdateNotice,
  recoverFromChunkLoadError,
  refreshAfterChunkLoadError,
  refreshWithFreshAppUrl,
  type FreshAppRefreshEnvironment,
  type FreshAppRefreshResult,
} from "./freshAppRefresh";

function createEnvironment(
  overrides: Partial<FreshAppRefreshEnvironment> = {},
): FreshAppRefreshEnvironment {
  return {
    createFreshUrl: vi.fn(() => "https://phone.local/?refresh=1"),
    reload: vi.fn(),
    replace: vi.fn(),
    ...overrides,
  };
}

describe("refreshWithFreshAppUrl", () => {
  it.each([
    [
      "service-worker lookup",
      { getRegistrations: vi.fn(() => Promise.reject(new Error("lookup"))) },
    ],
    [
      "service-worker unregister",
      {
        getRegistrations: vi.fn(() =>
          Promise.resolve([
            {
              unregister: () => {
                throw new Error("unregister");
              },
            },
          ]),
        ),
      },
    ],
    [
      "cache lookup",
      { getCacheNames: vi.fn(() => Promise.reject(new Error("keys"))), deleteCache: vi.fn() },
    ],
    [
      "cache deletion",
      {
        getCacheNames: vi.fn(() => Promise.resolve(["voltura-air-cache-a"])),
        deleteCache: () => {
          throw new Error("delete");
        },
      },
    ],
  ])("continues to navigation after a %s failure", async (_name, overrides) => {
    const environment = createEnvironment(overrides);

    const result = await refreshWithFreshAppUrl(environment);

    expect(result.navigationStarted).toBe(true);
    expect(result.navigationMethod).toBe("replace");
    expect(result.warnings).toHaveLength(1);
    expect(environment.replace).toHaveBeenCalledExactlyOnceWith("https://phone.local/?refresh=1");
  });

  it("leaves caches owned by other origin applications untouched", async () => {
    const deleteCache = vi.fn(() => true);
    const environment = createEnvironment({
      getCacheNames: vi.fn(() => Promise.resolve(["other-app", "voltura-air-old"])),
      deleteCache,
    });

    await refreshWithFreshAppUrl(environment);

    expect(deleteCache).toHaveBeenCalledExactlyOnceWith("voltura-air-old");
  });

  it("falls back to reload when fresh URL creation fails", async () => {
    const environment = createEnvironment({
      createFreshUrl: () => {
        throw new Error("URL");
      },
    });

    const result = await refreshWithFreshAppUrl(environment);

    expect(result.navigationMethod).toBe("reload");
    expect(environment.replace).not.toHaveBeenCalled();
    expect(environment.reload).toHaveBeenCalledOnce();
  });

  it("falls back to reload when replacement throws", async () => {
    const environment = createEnvironment({
      replace: () => {
        throw new Error("replace");
      },
    });

    const result = await refreshWithFreshAppUrl(environment);

    expect(result.navigationMethod).toBe("reload");
    expect(environment.reload).toHaveBeenCalledOnce();
  });

  it("returns a handled failure when neither navigation method can start", async () => {
    const environment = createEnvironment({
      reload: () => {
        throw new Error("reload");
      },
      replace: () => {
        throw new Error("replace");
      },
    });

    await expect(refreshWithFreshAppUrl(environment)).resolves.toMatchObject({
      navigationStarted: false,
      navigationMethod: null,
    });
  });

  it("commits the caller's guard immediately before navigation", async () => {
    const order: string[] = [];
    const environment = createEnvironment({
      replace: () => {
        order.push("navigate");
      },
    });

    await refreshWithFreshAppUrl(environment, () => {
      order.push("guard");
    });

    expect(order).toEqual(["guard", "navigate"]);
  });
});

describe("refreshAfterChunkLoadError", () => {
  it("navigates with a build guard without unregistering workers or deleting caches", async () => {
    const unregister = vi.fn();
    const deleteCache = vi.fn();
    const environment = createEnvironment({
      createFreshUrl: vi.fn((refreshValue) => `https://phone.local/?refresh=${refreshValue}`),
      deleteCache,
      getCacheNames: vi.fn(() => Promise.resolve(["voltura-air-old"])),
      getRegistrations: vi.fn(() => Promise.resolve([{ unregister }])),
    });

    const result = await refreshAfterChunkLoadError("build-a", environment);

    expect(result.navigationStarted).toBe(true);
    expect(environment.replace).toHaveBeenCalledExactlyOnceWith(
      "https://phone.local/?refresh=build-a",
    );
    expect(unregister).not.toHaveBeenCalled();
    expect(deleteCache).not.toHaveBeenCalled();
  });
});

const successfulRefresh: FreshAppRefreshResult = {
  navigationStarted: true,
  navigationMethod: "replace",
  warnings: [],
};

describe("recoverFromChunkLoadError", () => {
  it("suppresses a failed dynamic import and performs one fresh refresh per build", async () => {
    const storage = createMemoryStorage();
    const refresh = vi.fn(() => Promise.resolve(successfulRefresh));
    const firstEvent = new Event("vite:preloadError", { cancelable: true });

    await expect(
      recoverFromChunkLoadError(firstEvent, { buildId: "build-a", refresh, storage }),
    ).resolves.toBe(true);

    expect(firstEvent.defaultPrevented).toBe(true);
    expect(refresh).toHaveBeenCalledOnce();

    const repeatedEvent = new Event("vite:preloadError", { cancelable: true });
    await expect(
      recoverFromChunkLoadError(repeatedEvent, { buildId: "build-a", refresh, storage }),
    ).resolves.toBe(false);

    expect(repeatedEvent.defaultPrevented).toBe(false);
    expect(refresh).toHaveBeenCalledOnce();
  });

  it("allows a later retry when fresh navigation could not start", async () => {
    const storage = createMemoryStorage();
    const failedRefresh: FreshAppRefreshResult = {
      navigationStarted: false,
      navigationMethod: null,
      warnings: ["failed"],
    };
    const refresh = vi
      .fn<() => Promise<FreshAppRefreshResult>>()
      .mockResolvedValueOnce(failedRefresh)
      .mockResolvedValueOnce(successfulRefresh);

    await recoverFromChunkLoadError(new Event("vite:preloadError", { cancelable: true }), {
      buildId: "build-a",
      refresh,
      storage,
    });
    await recoverFromChunkLoadError(new Event("vite:preloadError", { cancelable: true }), {
      buildId: "build-a",
      refresh,
      storage,
    });

    expect(refresh).toHaveBeenCalledTimes(2);
  });

  it("uses an in-memory loop guard when session storage is blocked", async () => {
    const storage = {
      getItem: () => {
        throw new Error("blocked");
      },
      removeItem: () => {
        throw new Error("blocked");
      },
      setItem: () => {
        throw new Error("blocked");
      },
    };
    const refresh = vi.fn(() => Promise.resolve(successfulRefresh));

    await recoverFromChunkLoadError(new Event("vite:preloadError", { cancelable: true }), {
      buildId: "blocked-storage-build",
      refresh,
      storage,
    });
    await recoverFromChunkLoadError(new Event("vite:preloadError", { cancelable: true }), {
      buildId: "blocked-storage-build",
      refresh,
      storage,
    });

    expect(refresh).toHaveBeenCalledOnce();
  });

  it("uses the refresh URL as a loop guard across page navigation", async () => {
    const originalUrl = window.location.href;
    window.history.replaceState(null, "", "/?refresh=build-a");
    const refresh = vi.fn(() => Promise.resolve(successfulRefresh));

    try {
      const event = new Event("vite:preloadError", { cancelable: true });
      await expect(recoverFromChunkLoadError(event, { buildId: "build-a", refresh })).resolves.toBe(
        false,
      );

      expect(event.defaultPrevented).toBe(false);
      expect(refresh).not.toHaveBeenCalled();
    } finally {
      window.history.replaceState(null, "", originalUrl);
    }
  });

  it("retains a requested update notice only after fresh navigation starts", async () => {
    const storage = createMemoryStorage();

    await recoverFromChunkLoadError(new Event("vite:preloadError", { cancelable: true }), {
      buildId: "build-a",
      refresh: () => Promise.resolve(successfulRefresh),
      showUpdateNotice: true,
      storage,
    });

    expect(hasChunkLoadUpdateNotice(storage, "build-b")).toBe(true);
    clearChunkLoadUpdateNotice(storage);
    expect(hasChunkLoadUpdateNotice(storage, "build-b")).toBe(false);

    const failedRefresh: FreshAppRefreshResult = {
      navigationStarted: false,
      navigationMethod: null,
      warnings: ["failed"],
    };
    await recoverFromChunkLoadError(new Event("vite:preloadError", { cancelable: true }), {
      buildId: "build-b",
      refresh: () => Promise.resolve(failedRefresh),
      showUpdateNotice: true,
      storage,
    });

    expect(hasChunkLoadUpdateNotice(storage, "build-c")).toBe(false);
  });

  it("does not claim an update when navigation reloads the same build", async () => {
    const storage = createMemoryStorage();

    await recoverFromChunkLoadError(new Event("vite:preloadError", { cancelable: true }), {
      buildId: "build-a",
      refresh: () => Promise.resolve(successfulRefresh),
      showUpdateNotice: true,
      storage,
    });

    expect(hasChunkLoadUpdateNotice(storage, "build-a")).toBe(false);
    expect(hasChunkLoadUpdateNotice(storage, "build-b")).toBe(false);
  });
});

function createMemoryStorage(): Pick<Storage, "getItem" | "removeItem" | "setItem"> {
  const values = new Map<string, string>();
  return {
    getItem: (key) => values.get(key) ?? null,
    removeItem: (key) => {
      values.delete(key);
    },
    setItem: (key, value) => {
      values.set(key, value);
    },
  };
}
