interface RegistrationLike {
  unregister: () => boolean | Promise<boolean>;
}

export interface FreshAppRefreshEnvironment {
  createFreshUrl: () => string;
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

export async function refreshWithFreshAppUrl(
  environment: FreshAppRefreshEnvironment = createBrowserEnvironment(),
  beforeNavigate: () => void = () => { /* Optional session guard. */ }
): Promise<FreshAppRefreshResult> {
  const warnings: string[] = [];

  if (environment.getRegistrations) {
    try {
      const registrations = await environment.getRegistrations();
      const results = await Promise.allSettled(registrations.map(async (registration) => registration.unregister()));
      addRejectedWarnings(results, "service worker unregister", warnings);
    } catch (error) {
      warnings.push(formatWarning("service worker lookup", error));
    }
  }

  if (environment.getCacheNames && environment.deleteCache) {
    try {
      const cacheNames = await environment.getCacheNames();
      const results = await Promise.allSettled(cacheNames.map(async (cacheName) => environment.deleteCache?.(cacheName)));
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
    createFreshUrl: () => {
      const freshUrl = new URL(window.location.href);
      freshUrl.searchParams.delete("t");
      freshUrl.searchParams.set("refresh", Date.now().toString());
      return freshUrl.toString();
    },
    deleteCache: "caches" in window ? (name) => window.caches.delete(name) : undefined,
    getCacheNames: "caches" in window ? () => window.caches.keys() : undefined,
    getRegistrations: "serviceWorker" in navigator ? async () => navigator.serviceWorker.getRegistrations() : undefined,
    reload: () => { window.location.reload(); },
    replace: (url) => { window.location.replace(url); }
  };
}

function addRejectedWarnings(results: PromiseSettledResult<unknown>[], operation: string, warnings: string[]): void {
  for (const result of results) {
    if (result.status === "rejected") {
      warnings.push(formatWarning(operation, result.reason));
    }
  }
}

function formatWarning(operation: string, error: unknown): string {
  return `${operation} failed: ${error instanceof Error ? error.message : String(error)}`;
}
