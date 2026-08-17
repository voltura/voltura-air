const webBuildId = new URL(self.location.href).searchParams.get("build") || "dev";
const cacheName = `voltura-air-${webBuildId}`;
const appBase = new URL("./", self.location.href).pathname;
const shellFiles = [
  "",
  "manifest.webmanifest",
  "icon.svg",
  "apple-touch-icon.png",
  "icons/icon-192.png",
  "icons/icon-512.png",
  "icons/icon-maskable-192.png",
  "icons/icon-maskable-512.png"
].map((path) => `${appBase}${path}`);

self.addEventListener("install", (event) => {
  event.waitUntil(caches.open(cacheName).then((cache) => cache.addAll(shellFiles)));
  self.skipWaiting();
});

self.addEventListener("activate", (event) => {
  event.waitUntil(
    caches
      .keys()
      .then((keys) => Promise.all(keys.filter((key) => key.startsWith("voltura-air-") && key !== cacheName).map((key) => caches.delete(key))))
      .then(() => self.clients.claim())
  );
});

self.addEventListener("fetch", (event) => {
  if (event.request.method !== "GET") {
    return;
  }

  const isNavigationRequest = event.request.mode === "navigate";
  const requestUrl = new URL(event.request.url);
  const cacheableStaticRequest = requestUrl.origin === self.location.origin &&
    ["script", "style", "image", "font", "manifest"].includes(event.request.destination);

  event.respondWith(
    fetch(event.request)
      .then(async (response) => {
        if (response.ok && (isNavigationRequest || cacheableStaticRequest)) {
          const copy = response.clone();
          const cacheKey = isNavigationRequest ? appBase : event.request;
          try {
            const cache = await caches.open(cacheName);
            await cache.put(cacheKey, copy);
          } catch {
            // A failed cache write must not fail the live network response.
          }
        }
        return response;
      })
      .catch(async () => {
        if (!isNavigationRequest) {
          const cached = await caches.match(event.request);
          if (cached) {
            return cached;
          }
        }

        if (isNavigationRequest) {
          const shell = await caches.match(appBase);
          if (shell) {
            return shell;
          }
        }

        return new Response("", { status: 503, statusText: "Service Unavailable" });
      })
  );
});
