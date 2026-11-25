import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { test } from "node:test";
import { runInNewContext } from "node:vm";

const source = readFileSync(new URL("../../apps/mobile-web/public/sw.js", import.meta.url), "utf8");
const origin = "https://example.test";
const stable = "voltura-air:scope:%2Fair%2Fapp%2F:current";

function createWorker(caches, fetch = () => Promise.reject(new Error("Offline"))) {
  const listeners = new Map();
  const self = {
    location: new URL(`${origin}/air/app/sw.js?build=current`),
    clients: { claim() {} },
    addEventListener: (name, callback) => listeners.set(name, callback),
  };
  runInNewContext(source, { self, URL, Response, fetch, caches });
  return listeners;
}

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
