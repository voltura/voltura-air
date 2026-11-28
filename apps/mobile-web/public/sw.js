const webBuildId = new URL(self.location.href).searchParams.get("build") || "dev";
const appBase = new URL("./", self.location.href).pathname;
const cachePrefix = `voltura-air:scope:${encodeURIComponent(appBase)}:`;
const cacheName = `${cachePrefix}${webBuildId}`;
const bootAssets = /* offline shell assets */ [];
const bootAssetPaths = new Set(bootAssets.map((path) => `${appBase}${path}`));
const shellFiles = [
  "",
  "manifest.webmanifest",
  "icon.svg",
  "apple-touch-icon.png",
  "icons/icon-192.png",
  "icons/icon-512.png",
  "icons/icon-maskable-192.png",
  "icons/icon-maskable-512.png",
]
  .map((path) => `${appBase}${path}`)
  .concat([...bootAssetPaths]);

self.addEventListener("install", (event) => {
  event.waitUntil(
    caches
      .open(cacheName)
      .then((cache) => cache.addAll(shellFiles))
      .then(() => self.skipWaiting()),
  );
});

self.addEventListener("activate", (event) => {
  event.waitUntil(
    caches
      .keys()
      .then((keys) =>
        Promise.allSettled(
          keys
            .filter((key) => key.startsWith(cachePrefix) && key !== cacheName)
            .map((key) => caches.delete(key)),
        ),
      )
      .catch(() => {})
      .then(() => self.clients.claim()),
  );
});

self.addEventListener("fetch", (event) => {
  if (event.request.method !== "GET") {
    return;
  }

  const requestUrl = new URL(event.request.url);
  if (requestUrl.origin !== self.location.origin || !requestUrl.pathname.startsWith(appBase)) {
    return;
  }

  const isShellNavigation = event.request.mode === "navigate" && requestUrl.pathname === appBase;
  const cacheableStaticRequest = ["script", "style", "image", "font", "manifest"].includes(
    event.request.destination,
  );
  if (!isShellNavigation && !cacheableStaticRequest) {
    return;
  }

  const cacheKey = isShellNavigation ? appBase : event.request;
  const isBootAsset = bootAssetPaths.has(requestUrl.pathname);
  // The installed HTML and its hashed imports belong to the same build.
  const assetCache =
    !isShellNavigation && !isBootAsset
      ? caches
          .has(cacheName)
          .then((exists) => (exists ? caches.open(cacheName) : undefined))
          .catch(() => undefined)
      : undefined;
  const networkResponse = fetch(event.request);
  if (assetCache) {
    // Acquire the cache when the fetch starts, before waiting for the network.
    event.waitUntil(
      networkResponse
        .then(async (response) => {
          if (response.ok && !response.redirected) {
            const copy = response.clone();
            const cache = await assetCache;
            await cache?.put(cacheKey, copy);
          }
        })
        .catch(() => {}),
    );
  }
  event.respondWith(
    networkResponse.catch(async () => {
      try {
        const cached = await caches.match(cacheKey, {
          cacheName,
          // Precache requests and module loading can send different headers to the same asset.
          ignoreVary: isBootAsset,
        });
        if (cached) return cached;
      } catch {
        // A disabled or unavailable Cache API also gets the offline response.
      }

      return new Response("", { status: 503, statusText: "Service Unavailable" });
    }),
  );
});
