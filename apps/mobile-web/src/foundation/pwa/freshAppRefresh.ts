interface RegistrationLike {
  unregister: () => boolean | Promise<boolean>;
}

export interface FreshAppRefreshEnvironment {
  createFreshUrl: (refreshValue?: string) => string;
  deleteCache?: ((name: string) => boolean | Promise<boolean>) | undefined;
  getCacheNames?: (() => Promise<string[]>) | undefined;
  getRegistrations?: (() => Promise<readonly RegistrationLike[]>) | undefined;
  reload: () => void;
  replace: (url: string) => void;
}

export interface FreshAppRefreshResult {
  navigationStarted: boolean;
  navigationMethod: "replace" | "reload" | null;
  warnings: string[];
}

interface ChunkLoadRecoveryOptions {
  buildId?: string;
  refresh?: () => Promise<FreshAppRefreshResult>;
  showUpdateNotice?: boolean;
  storage?: Pick<Storage, "getItem" | "removeItem" | "setItem">;
}

const chunkLoadRefreshGuardPrefix = "voltura-air.chunkLoadRefresh.build";
const chunkLoadUpdateNoticeKey = "voltura-air.chunkLoadRefresh.updateNotice";
const inMemoryRefreshGuards = new Set<string>();

export async function recoverFromChunkLoadError(
  event: Event,
  {
    buildId = __WEB_BUILD_ID__,
    refresh = () => refreshAfterChunkLoadError(buildId),
    showUpdateNotice = false,
    storage,
  }: ChunkLoadRecoveryOptions = {},
): Promise<boolean> {
  const resolvedStorage = resolveSessionStorage(storage);
  const refreshKey = `${chunkLoadRefreshGuardPrefix}.${buildId}`;
  if (hasRefreshGuard(resolvedStorage, refreshKey) || hasRefreshUrlGuard(buildId)) {
    return false;
  }

  event.preventDefault();
  setRefreshGuard(resolvedStorage, refreshKey);
  if (showUpdateNotice) {
    setUpdateNoticeBuildId(resolvedStorage, buildId);
  }

  try {
    const result = await refresh();
    if (!result.navigationStarted) {
      clearRefreshGuard(resolvedStorage, refreshKey);
      if (showUpdateNotice) {
        clearUpdateNoticeBuildId(resolvedStorage);
      }
    }
    return result.navigationStarted;
  } catch {
    clearRefreshGuard(resolvedStorage, refreshKey);
    if (showUpdateNotice) {
      clearUpdateNoticeBuildId(resolvedStorage);
    }
    return false;
  }
}

export function refreshAfterChunkLoadError(
  buildId: string,
  environment: FreshAppRefreshEnvironment = createBrowserEnvironment(),
): Promise<FreshAppRefreshResult> {
  const warnings: string[] = [];
  try {
    const freshUrl = environment.createFreshUrl(buildId);
    environment.replace(freshUrl);
    return Promise.resolve({ navigationStarted: true, navigationMethod: "replace", warnings });
  } catch (error) {
    warnings.push(formatWarning("chunk recovery navigation", error));
    return Promise.resolve({ navigationStarted: false, navigationMethod: null, warnings });
  }
}

export function hasChunkLoadUpdateNotice(
  storage?: Pick<Storage, "getItem" | "removeItem">,
  currentBuildId = __WEB_BUILD_ID__,
): boolean {
  const resolvedStorage = resolveSessionStorage(storage);
  const failedBuildId = getUpdateNoticeBuildId(resolvedStorage);
  if (failedBuildId === undefined) {
    return false;
  }
  if (failedBuildId === currentBuildId) {
    clearUpdateNoticeBuildId(resolvedStorage);
    return false;
  }
  return true;
}

export function clearChunkLoadUpdateNotice(storage?: Pick<Storage, "removeItem">): void {
  clearRefreshGuard(resolveSessionStorage(storage), chunkLoadUpdateNoticeKey);
}

export async function refreshWithFreshAppUrl(
  environment: FreshAppRefreshEnvironment = createBrowserEnvironment(),
  beforeNavigate: () => void = () => {
    /* Optional session guard. */
  },
): Promise<FreshAppRefreshResult> {
  const warnings: string[] = [];

  if (environment.getRegistrations) {
    try {
      const registrations = await environment.getRegistrations();
      const results = await Promise.allSettled(
        registrations.map(async (registration) => registration.unregister()),
      );
      addRejectedWarnings(results, "service worker unregister", warnings);
    } catch (error) {
      warnings.push(formatWarning("service worker lookup", error));
    }
  }

  if (environment.getCacheNames && environment.deleteCache) {
    try {
      const cacheNames = await environment.getCacheNames();
      const ownedCacheNames = cacheNames.filter((cacheName) =>
        cacheName.startsWith("voltura-air-"),
      );
      const results = await Promise.allSettled(
        ownedCacheNames.map(async (cacheName) => environment.deleteCache?.(cacheName)),
      );
      addRejectedWarnings(results, "cache deletion", warnings);
    } catch (error) {
      warnings.push(formatWarning("cache lookup", error));
    }
  }

  let freshUrl: string | null = null;
  try {
    freshUrl = environment.createFreshUrl();
  } catch (error) {
    warnings.push(formatWarning("fresh URL creation", error));
  }

  beforeNavigate();
  if (freshUrl !== null) {
    try {
      environment.replace(freshUrl);
      return { navigationStarted: true, navigationMethod: "replace", warnings };
    } catch (error) {
      warnings.push(formatWarning("location replacement", error));
    }
  }

  try {
    environment.reload();
    return { navigationStarted: true, navigationMethod: "reload", warnings };
  } catch (error) {
    warnings.push(formatWarning("location reload", error));
    return { navigationStarted: false, navigationMethod: null, warnings };
  }
}

function createBrowserEnvironment(): FreshAppRefreshEnvironment {
  return {
    createFreshUrl: (refreshValue) => {
      const freshUrl = new URL(window.location.href);
      freshUrl.searchParams.delete("t");
      freshUrl.searchParams.set("refresh", refreshValue ?? Date.now().toString());
      return freshUrl.toString();
    },
    deleteCache: "caches" in window ? (name) => window.caches.delete(name) : undefined,
    getCacheNames: "caches" in window ? () => window.caches.keys() : undefined,
    getRegistrations:
      "serviceWorker" in navigator
        ? async () => navigator.serviceWorker.getRegistrations()
        : undefined,
    reload: () => {
      window.location.reload();
    },
    replace: (url) => {
      window.location.replace(url);
    },
  };
}

function addRejectedWarnings(
  results: PromiseSettledResult<unknown>[],
  operation: string,
  warnings: string[],
): void {
  for (const result of results) {
    if (result.status === "rejected") {
      warnings.push(formatWarning(operation, result.reason));
    }
  }
}

function formatWarning(operation: string, error: unknown): string {
  return `${operation} failed: ${error instanceof Error ? error.message : String(error)}`;
}

function resolveSessionStorage<T>(storage?: T): T | Storage | undefined {
  if (storage) {
    return storage;
  }
  try {
    return window.sessionStorage;
  } catch {
    return undefined;
  }
}

function hasRefreshGuard(storage: Pick<Storage, "getItem"> | undefined, key: string): boolean {
  try {
    return inMemoryRefreshGuards.has(key) || storage?.getItem(key) === "true";
  } catch {
    return inMemoryRefreshGuards.has(key);
  }
}

function hasRefreshUrlGuard(buildId: string): boolean {
  try {
    return new URL(window.location.href).searchParams.get("refresh") === buildId;
  } catch {
    return false;
  }
}

function setRefreshGuard(storage: Pick<Storage, "setItem"> | undefined, key: string): void {
  try {
    if (storage) {
      storage.setItem(key, "true");
      return;
    }
  } catch {
    // Fall back to memory when storage rejects the write.
  }
  inMemoryRefreshGuards.add(key);
}

function clearRefreshGuard(storage: Pick<Storage, "removeItem"> | undefined, key: string): void {
  inMemoryRefreshGuards.delete(key);
  try {
    storage?.removeItem(key);
  } catch {
    // The in-memory fallback is already clear.
  }
}

function getUpdateNoticeBuildId(storage: Pick<Storage, "getItem"> | undefined): string | undefined {
  try {
    return storage?.getItem(chunkLoadUpdateNoticeKey) ?? undefined;
  } catch {
    return undefined;
  }
}

function setUpdateNoticeBuildId(
  storage: Pick<Storage, "setItem"> | undefined,
  buildId: string,
): void {
  try {
    storage?.setItem(chunkLoadUpdateNoticeKey, buildId);
  } catch {
    // An in-memory notice would not survive the navigation it describes.
  }
}

function clearUpdateNoticeBuildId(storage: Pick<Storage, "removeItem"> | undefined): void {
  clearRefreshGuard(storage, chunkLoadUpdateNoticeKey);
}
