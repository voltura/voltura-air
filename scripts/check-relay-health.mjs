import { readFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const defaultConfigurationPath = path.join(repositoryRoot, "apps", "windows-host", "relay-service.json");
const maximumResponseBytes = 4 * 1024;

export function validateRelayConfiguration(value) {
  if (!value || typeof value !== "object" || Array.isArray(value) ||
      Object.keys(value).sort().join("|") !== "httpsBase|serviceId|supportsTurn" ||
      typeof value.serviceId !== "string" || !/^[a-z0-9][a-z0-9-]{0,63}$/u.test(value.serviceId) ||
      typeof value.httpsBase !== "string" || value.httpsBase.length > 512 ||
      value.supportsTurn !== true) {
    throw new Error("The production relay configuration is invalid.");
  }

  const base = new URL(value.httpsBase);
  if (base.protocol !== "https:" || base.username || base.password || base.search || base.hash) {
    throw new Error("The production relay must use a credential-free HTTPS address.");
  }
  return { serviceId: value.serviceId, base };
}

export function validateRelayHealth(value, expectedServiceId) {
  if (!value || typeof value !== "object" || Array.isArray(value) ||
      Object.keys(value).sort().join("|") !== "protocol|service|status" ||
      value.service !== expectedServiceId || value.protocol !== 1 || value.status !== "ok") {
    throw new Error("The deployed relay returned an unexpected health response.");
  }
}

async function readBoundedText(response) {
  const declaredLength = Number(response.headers.get("content-length"));
  if (Number.isFinite(declaredLength) && declaredLength > maximumResponseBytes) {
    throw new Error("The deployed relay health response was too large.");
  }
  if (!response.body) {
    return "";
  }

  const reader = response.body.getReader();
  const chunks = [];
  let length = 0;
  try {
    while (true) {
      const { done, value } = await reader.read();
      if (done) break;
      length += value.byteLength;
      if (length > maximumResponseBytes) {
        await reader.cancel();
        throw new Error("The deployed relay health response was too large.");
      }
      chunks.push(value);
    }
  } finally {
    reader.releaseLock();
  }

  const bytes = new Uint8Array(length);
  let offset = 0;
  for (const chunk of chunks) {
    bytes.set(chunk, offset);
    offset += chunk.byteLength;
  }
  return new TextDecoder("utf-8", { fatal: true }).decode(bytes);
}

export async function checkRelayHealth({
  configurationPath = defaultConfigurationPath,
  fetchImplementation = globalThis.fetch,
  timeoutMs = 30_000
} = {}) {
  const configuration = validateRelayConfiguration(JSON.parse(await readFile(configurationPath, "utf8")));
  const healthUrl = new URL(configuration.base);
  healthUrl.pathname = `${healthUrl.pathname.replace(/\/$/u, "")}/v1/health`;

  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), timeoutMs);
  try {
    const response = await fetchImplementation(healthUrl, {
      cache: "no-store",
      headers: { Accept: "application/json" },
      redirect: "error",
      signal: controller.signal
    });
    if (!response.ok) {
      throw new Error(`The deployed relay health endpoint returned HTTP ${response.status}.`);
    }
    if (response.headers.get("content-type")?.split(";", 1)[0]?.trim().toLowerCase() !== "application/json") {
      throw new Error("The deployed relay health endpoint did not return JSON.");
    }
    validateRelayHealth(JSON.parse(await readBoundedText(response)), configuration.serviceId);
    return healthUrl.href;
  } finally {
    clearTimeout(timeout);
  }
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  checkRelayHealth().then((url) => {
    console.log(`Relay health verified: ${url}`);
  }).catch((error) => {
    console.error(error instanceof Error ? error.message : String(error));
    process.exitCode = 1;
  });
}
