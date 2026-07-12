const webBuildId = new URL(self.location.href).searchParams.get("build") || "dev";
const cacheName = `voltura-air-${webBuildId}`;
const shellFiles = ["/", "/manifest.webmanifest", "/icon.svg", "/apple-touch-icon.png"];

self.addEventListener("install", (event) => {
  event.waitUntil(caches.open(cacheName).then((cache) => cache.addAll(shellFiles)));
  self.skipWaiting();
});

self.addEventListener("activate", (event) => {
  event.waitUntil(
    caches
      .keys()
      .then((keys) => Promise.all(keys.filter((key) => key !== cacheName).map((key) => caches.delete(key))))
      .then(() => self.clients.claim())
  );
});

self.addEventListener("fetch", (event) => {
  if (event.request.method !== "GET") {
    return;
  }

  const isNavigationRequest = event.request.mode === "navigate";

  event.respondWith(
    fetch(event.request)
      .then((response) => {
        if (response.ok) {
          const copy = response.clone();
          const cacheKey = isNavigationRequest ? "/" : event.request;
          caches.open(cacheName).then((cache) => cache.put(cacheKey, copy));
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
          const shell = await caches.match("/");
          if (shell) {
            return shell;
          }
        }

        return new Response("", { status: 503, statusText: "Service Unavailable" });
      })
  );
});
