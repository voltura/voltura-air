const webBuildId = new URL(self.location.href).searchParams.get("build") || "dev";
const appBase = new URL("./", self.location.href).pathname;
const cachePrefix = `voltura-air:scope:${encodeURIComponent(appBase)}:`;
const cacheName = `${cachePrefix}${webBuildId}`;
const shellFiles = [
  "",
  "manifest.webmanifest",
  "icon.svg",
  "apple-touch-icon.png",
  "icons/icon-192.png",
  "icons/icon-512.png",
  "icons/icon-maskable-192.png",
  "icons/icon-maskable-512.png",
].map((path) => `${appBase}${path}`);

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

  event.respondWith(
    fetch(event.request)
      .then(async (response) => {
        if (response.ok && !response.redirected) {
          try {
            const copy = response.clone();
            const cacheKey = isShellNavigation ? appBase : event.request;
            const cache = await caches.open(cacheName);
            await cache.put(cacheKey, copy);
          } catch {
            // A failed cache write must not fail the live network response.
          }
        }
        return response;
      })
      .catch(async () => {
        try {
          const cached = await caches.match(isShellNavigation ? appBase : event.request, {
            cacheName,
          });
          if (cached) return cached;
        } catch {
          // A disabled or unavailable Cache API also gets the offline response.
        }

        return new Response("", { status: 503, statusText: "Service Unavailable" });
      }),
  );
});
