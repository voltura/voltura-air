import assert from "node:assert/strict";
import { mkdtemp, rm, writeFile } from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import test from "node:test";

import { checkRelayHealth, validateRelayConfiguration, validateRelayHealth } from "../../scripts/check-relay-health.mjs";

test("production relay health verifies the configured HTTPS service", async () => {
  const directory = await mkdtemp(path.join(os.tmpdir(), "voltura-relay-health-"));
  const configurationPath = path.join(directory, "relay-service.json");
  await writeFile(configurationPath, JSON.stringify({
    serviceId: "voltura-cloud-v1",
    httpsBase: "https://relay.example.com/base",
    supportsTurn: true
  }));
  let request;
  try {
    const healthUrl = await checkRelayHealth({
      configurationPath,
      fetchImplementation: async (url, options) => {
        request = { url: url.href, options };
        return new Response(JSON.stringify({ service: "voltura-cloud-v1", protocol: 1, status: "ok" }), {
          headers: { "Content-Type": "application/json" }
        });
      },
      timeoutMs: 1_000
    });

    assert.equal(healthUrl, "https://relay.example.com/base/v1/health");
    assert.equal(request.url, healthUrl);
    assert.equal(request.options.redirect, "error");
    assert.equal(request.options.signal.aborted, false);
  } finally {
    await rm(directory, { recursive: true, force: true });
  }
});

test("production relay health rejects unsafe configuration and wrong service identity", () => {
  assert.throws(() => validateRelayConfiguration({
    serviceId: "voltura-cloud-v1",
    httpsBase: "http://relay.example.com",
    supportsTurn: true
  }), /HTTPS/u);
  assert.throws(() => validateRelayHealth(
    { service: "another-service", protocol: 1, status: "ok" },
    "voltura-cloud-v1"
  ), /unexpected health/u);
});

test("production relay health bounds the response body", async () => {
  const directory = await mkdtemp(path.join(os.tmpdir(), "voltura-relay-health-"));
  const configurationPath = path.join(directory, "relay-service.json");
  await writeFile(configurationPath, JSON.stringify({
    serviceId: "voltura-cloud-v1",
    httpsBase: "https://relay.example.com",
    supportsTurn: true
  }));
  try {
    await assert.rejects(() => checkRelayHealth({
      configurationPath,
      fetchImplementation: async () => new Response("x".repeat(4_097), {
        headers: { "Content-Type": "application/json" }
      }),
      timeoutMs: 1_000
    }), /too large/u);
  } finally {
    await rm(directory, { recursive: true, force: true });
  }
});

test("production relay health cancels a stalled request", async () => {
  const directory = await mkdtemp(path.join(os.tmpdir(), "voltura-relay-health-"));
  const configurationPath = path.join(directory, "relay-service.json");
  await writeFile(configurationPath, JSON.stringify({
    serviceId: "voltura-cloud-v1",
    httpsBase: "https://relay.example.com",
    supportsTurn: true
  }));
  try {
    await assert.rejects(() => checkRelayHealth({
      configurationPath,
      fetchImplementation: async (_, options) => new Promise((_, reject) => {
        options.signal.addEventListener("abort", () => reject(options.signal.reason), { once: true });
      }),
      timeoutMs: 10
    }), /abort|timeout/iu);
  } finally {
    await rm(directory, { recursive: true, force: true });
  }
});
