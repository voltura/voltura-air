import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { test } from "node:test";
import { runInNewContext } from "node:vm";

const source = readFileSync(new URL("../../apps/mobile-web/public/sw.js", import.meta.url), "utf8");
const origin = "https://example.test";
const stable = "voltura-air:scope:%2Fair%2Fapp%2F:current";

function createWorker(caches, fetch = () => Promise.reject(new Error("Offline")), bootAssets = []) {
  const listeners = new Map();
  const self = {
    location: new URL(`${origin}/air/app/sw.js?build=current`),
    clients: { claim() {} },
    skipWaiting() {},
    addEventListener: (name, callback) => listeners.set(name, callback),
  };
  const worker = source.replace("/* offline shell assets */ []", JSON.stringify(bootAssets));
  runInNewContext(worker, {
    self,
    URL,
    Response,
    fetch,
    caches: { has: async () => false, ...caches },
  });
  return listeners;
}

test("mobile service worker installs the compiled scripts and stylesheet with its scoped shell", async () => {
  let installed;
  let cachePaths;
  const listeners = createWorker(
    {
      open: async (name) => {
        assert.equal(name, stable);
        return {
          addAll: async (paths) => {
            cachePaths = Array.from(paths);
          },
        };
      },
    },
    undefined,
    ["assets/index-1.js", "assets/runtime-1.js", "assets/index-1.css"],
  );
  listeners.get("install")({
    waitUntil: (promise) => {
      installed = promise;
    },
  });
  await installed;
  assert.ok(cachePaths.includes("/air/app/"));
  assert.deepEqual(cachePaths.slice(-3), [
    "/air/app/assets/index-1.js",
    "/air/app/assets/runtime-1.js",
    "/air/app/assets/index-1.css",
  ]);
});

test("mobile service worker retires older caches from its own app scope", async () => {
  const old = "voltura-air:scope:%2Fair%2Fapp%2F:old";
  const development = "voltura-air:scope:%2Fair%2Fdev-app%2F:current";
  const removals = [];
  const listeners = createWorker({
    keys: async () => [stable, old, development, "voltura-air-unscoped-legacy", "unrelated"],
    delete: async (name) => removals.push(name),
  });
  let activation;
  listeners.get("activate")({
    waitUntil: (promise) => {
      activation = promise;
    },
  });
  await activation;
  assert.deepEqual(removals, [old]);
});

test("offline mobile navigations use the active shell and leave other paths alone", async () => {
  const lookups = [];
  const listeners = createWorker({
    match: async (path, { cacheName }) => {
      lookups.push([path, cacheName]);
      return new Response("Offline shell");
    },
  });
  let response;
  const navigate = (path) =>
    listeners.get("fetch")({
      request: {
        url: `${origin}${path}`,
        method: "GET",
        mode: "navigate",
        destination: "document",
      },
      waitUntil() {},
      respondWith: (promise) => {
        response = promise;
      },
    });
  navigate("/air/app/unrelated/");
  assert.equal(response, undefined);
  navigate("/air/app/?refresh=123");
  assert.equal(await (await response).text(), "Offline shell");
  assert.deepEqual(lookups, [["/air/app/", stable]]);
});

test("an offline compiled module can use a precache entry with different request headers", async () => {
  const lookups = [];
  const listeners = createWorker(
    {
      match: async (request, { cacheName, ignoreVary }) => {
        lookups.push([request.url, cacheName, ignoreVary]);
        return ignoreVary ? new Response("Startup module") : undefined;
      },
    },
    undefined,
    ["assets/startup-module.js"],
  );
  const request = (file) => {
    let response;
    listeners.get("fetch")({
      request: { url: `${origin}/air/app/assets/${file}`, method: "GET", destination: "script" },
      respondWith: (promise) => {
        response = promise;
      },
      waitUntil() {},
    });
    return response;
  };

  assert.equal(await (await request("startup-module.js")).text(), "Startup module");
  assert.equal((await request("other-module.js")).status, 503);
  assert.deepEqual(lookups, [
    [`${origin}/air/app/assets/startup-module.js`, stable, true],
    [`${origin}/air/app/assets/other-module.js`, stable, false],
  ]);
});

test("a live asset response does not wait for a pending or failed cache write", async () => {
  const { promise: pendingWrite, reject: failWrite } = Promise.withResolvers();
  const { promise: writeStarted, resolve: recordWrite } = Promise.withResolvers();
  const writes = [];
  const listeners = createWorker(
    {
      has: async () => true,
      open: async (name) => {
        assert.equal(name, stable);
        return {
          put: async (key, response) => {
            writes.push([key.url, await response.text()]);
            recordWrite();
            await pendingWrite;
          },
        };
      },
    },
    async () => new Response("Live asset"),
  );
  let response;
  let cacheWork;
  listeners.get("fetch")({
    request: {
      url: `${origin}/air/app/assets/lazy.js`,
      method: "GET",
      destination: "script",
    },
    respondWith: (promise) => {
      response = promise;
    },
    waitUntil: (promise) => {
      cacheWork = promise;
    },
  });

  assert.equal(await (await response).text(), "Live asset");
  assert.ok(cacheWork);
  await writeStarted;
  failWrite(new Error("Cache unavailable"));
  await cacheWork;
  assert.deepEqual(writes, [[`${origin}/air/app/assets/lazy.js`, "Live asset"]]);
});

test("an online navigation to a newer build leaves the installed offline shell intact", async () => {
  let online = true;
  let cachedShell = "Shell with build A imports";
  const listeners = createWorker(
    {
      has: async () => true,
      open: async () => ({
        put: async (_key, response) => {
          cachedShell = await response.text();
        },
      }),
      match: async () => new Response(cachedShell),
    },
    async () => {
      if (!online) throw new Error("Offline");
      return new Response("Shell with build B imports");
    },
  );
  const navigate = async () => {
    let response;
    let cacheWork;
    listeners.get("fetch")({
      request: { url: `${origin}/air/app/`, method: "GET", mode: "navigate" },
      respondWith: (promise) => {
        response = promise;
      },
      waitUntil: (promise) => {
        cacheWork = promise;
      },
    });
    await cacheWork;
    return (await response).text();
  };
  assert.equal(await navigate(), "Shell with build B imports");
  online = false;
  assert.equal(await navigate(), "Shell with build A imports");
});

test("a late asset response uses its original cache handle after activation removes the name", async () => {
  const network = Promise.withResolvers();
  let cacheExists = true;
  let acquiredCache = false;
  let reopenedCache = false;
  const listener = createWorker(
    {
      has: async () => cacheExists,
      open: async () => {
        reopenedCache ||= !cacheExists;
        acquiredCache = true;
        return { put: async () => {} };
      },
    },
    () => network.promise,
  ).get("fetch");
  let cacheWork;
  listener({
    request: { url: `${origin}/air/app/assets/lazy.js`, method: "GET", destination: "script" },
    respondWith() {},
    waitUntil: (promise) => {
      cacheWork = promise;
    },
  });
  await new Promise((resolve) => setImmediate(resolve));
  assert.equal(acquiredCache, true);
  cacheExists = false;
  network.resolve(new Response("Late asset"));
  await cacheWork;
  assert.equal(reopenedCache, false);
});
