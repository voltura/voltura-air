export interface PairingLink {
  pairToken: string;
  pcUrl: string;
}

export type ManualConnectionTarget =
  | { kind: "host"; pcUrl: string }
  | { kind: "pairing"; pairToken: string; pcUrl: string };

export type ManualConnectionValidation =
  | { valid: true; target: ManualConnectionTarget }
  | { valid: false; message: string };

const pairingTokenPattern = /^[A-Za-z0-9_-]{32}$/;
const versionPattern = /^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$/;
const pairingPath = "/pair";
const relayRoutePattern = /^[A-Za-z0-9_-]{22}$/;
const safeDefaultPcUrl = "http://127.0.0.1";
const invalidHostMessage = "Enter a host with a valid port, for example 192.168.1.50:51395.";
const invalidPairingLinkMessage = "Enter the complete pairing link shown by Voltura Air on the PC.";

export function parsePairingLink(source: string): PairingLink | null {
  const trimmedSource = source.trim();
  if (!trimmedSource) {
    return null;
  }

  try {
    const url = new URL(trimmedSource);
    if (!isHttpUrl(url) || hasCredentials(url)) {
      return null;
    }

    const relay = parseRelayPairingLink(url);
    if (relay) {return relay;}
    if ((url.pathname !== pairingPath && url.pathname !== `${pairingPath}/`) || url.hash) {return null;}

    const tokens = url.searchParams.getAll("t");
    const versions = url.searchParams.getAll("v");
    if (tokens.length !== 1 || !pairingTokenPattern.test(tokens[0]!) ||
        versions.length !== 1 || !versionPattern.test(versions[0]!)) {
      return null;
    }

    for (const key of url.searchParams.keys()) {
      if (key !== "t" && key !== "v" && key !== "h") {
        return null;
      }
    }

    const pcUrl = resolvePairingPcUrl(url);
    return pcUrl ? { pairToken: tokens[0]!, pcUrl } : null;
  } catch {
    return null;
  }
}

function parseRelayPairingLink(url: URL): PairingLink | null {
  if (url.protocol !== "https:" || url.hostname !== "voltura.se") {return null;}
  const shortRoute = /^\/a\/([A-Za-z0-9_-]{22})\/?$/u.exec(url.pathname)?.[1];
  const hostedRoute = url.pathname === "/air/app/" ? url.searchParams.get("r") : null;
  const route = shortRoute ?? hostedRoute;
  const token = url.hash.startsWith("#") ? url.hash.slice(1) : "";
  const versions = url.searchParams.getAll("v");
  if (!route || !relayRoutePattern.test(route) || !pairingTokenPattern.test(token) || versions.length !== 1 ||
      !versionPattern.test(versions[0]!)) {return null;}
  const allowed = shortRoute ? new Set(["v"]) : new Set(["r", "v", "e"]);
  if ([...url.searchParams.keys()].some((key) => !allowed.has(key))) {return null;}
  const endpointValues = url.searchParams.getAll("e");
  if (endpointValues.length > 1 || (shortRoute && endpointValues.length !== 0)) {return null;}
  const endpoint = endpointValues[0] ? decodeRelayEndpoint(endpointValues[0]) : null;
  if (endpointValues.length === 1 && !endpoint) {return null;}
  const endpointParameter = endpoint ? `?e=${encodeURIComponent(encodeRelayEndpoint(endpoint))}` : "";
  return { pairToken: token, pcUrl: `https://voltura.se/a/${route}${endpointParameter}` };
}

function decodeRelayEndpoint(value: string): string | null {
  try {
    if (!/^[A-Za-z0-9_-]{1,683}$/u.test(value)) {return null;}
    const normalized = value.replace(/-/gu, "+").replace(/_/gu, "/").padEnd(Math.ceil(value.length / 4) * 4, "=");
    const bytes = Uint8Array.from(atob(normalized), (character) => character.charCodeAt(0));
    const endpoint = new URL(new TextDecoder("utf-8", { fatal: true }).decode(bytes));
    return endpoint.protocol === "https:" && !endpoint.username && !endpoint.password && endpoint.pathname === "/" &&
      !endpoint.search && !endpoint.hash && endpoint.toString().length <= 512 ? endpoint.origin : null;
  } catch {
    return null;
  }
}

function encodeRelayEndpoint(value: string): string {
  const binary = String.fromCharCode(...new TextEncoder().encode(value));
  return btoa(binary).replace(/\+/gu, "-").replace(/\//gu, "_").replace(/=+$/u, "");
}

export function hasPairingTokenParameter(source: string): boolean {
  try {
    const url = new URL(source);
    return url.searchParams.has("t") || (url.hostname === "voltura.se" && url.hash.length > 1 &&
      (url.pathname.startsWith("/a/") || url.pathname === "/air/app/"));
  } catch {
    return new URLSearchParams(source).has("t");
  }
}

export function validateManualConnectionInput(value: string, fallbackUrl: string): ManualConnectionValidation {
  const trimmed = value.trim();
  if (!trimmed) {
    return invalid("Enter a host and port, port number, or Voltura Air pairing link.");
  }

  if (/^\d{1,5}$/.test(trimmed)) {
    const port = Number.parseInt(trimmed, 10);
    try {
      const fallback = new URL(fallbackUrl);
      if (!isHttpUrl(fallback) || port <= 0 || port > 65535) {
        return invalid(invalidHostMessage);
      }

      fallback.port = String(port);
      return validHost(fallback.origin);
    } catch {
      return invalid(invalidHostMessage);
    }
  }

  try {
    const hasScheme = /^[a-z][a-z0-9+.-]*:\/\//i.test(trimmed);
    const url = new URL(hasScheme ? trimmed : `http://${trimmed}`);
    if (!isHttpUrl(url)) {
      return invalid("Only HTTP and HTTPS host addresses are supported.");
    }
    if (hasCredentials(url)) {
      return invalid("Host addresses cannot include a user name or password.");
    }

    const hasPairingParameters = ["t", "v", "h"].some((name) => url.searchParams.has(name));
    if (hasPairingParameters) {
      const pairingLink = hasScheme ? parsePairingLink(trimmed) : null;
      return pairingLink
        ? { valid: true, target: { kind: "pairing", ...pairingLink } }
        : invalid(invalidPairingLinkMessage);
    }

    if (url.pathname !== "/" || url.search || url.hash) {
      return invalid("Host addresses cannot include a path, query, or fragment.");
    }
    if (!url.port) {
      return invalid(invalidHostMessage);
    }

    return validHost(url.origin);
  } catch {
    return invalid(invalidHostMessage);
  }
}

export function parsePcUrl(source: unknown, fallbackPcUrl: unknown): string {
  const normalizedFallback = normalizePcUrl(fallbackPcUrl) ?? safeDefaultPcUrl;
  if (typeof source !== "string") {
    return normalizedFallback;
  }

  const trimmedSource = source.trim();
  if (!trimmedSource) {
    return normalizedFallback;
  }

  try {
    const url = new URL(trimmedSource);
    return getPcUrl(url.searchParams, normalizePcUrl(url.origin) ?? normalizedFallback);
  } catch {
    return getPcUrl(new URLSearchParams(trimmedSource), normalizedFallback);
  }
}

export function normalizePcUrl(value: unknown): string | null {
  if (typeof value !== "string") {
    return null;
  }

  const trimmed = value.trim();
  if (!trimmed) {
    return null;
  }

  try {
    const url = new URL(trimmed);
    return isHttpUrl(url) && !hasCredentials(url) && Boolean(url.hostname) && url.origin !== "null"
      ? url.origin
      : null;
  } catch {
    return null;
  }
}

function resolvePairingPcUrl(url: URL): string | null {
  const hostHints = url.searchParams.getAll("h");
  if (hostHints.length > 1) {
    return null;
  }

  const hostHint = hostHints[0];
  if (hostHint && /^\d{1,5}$/.test(hostHint)) {
    const port = Number.parseInt(hostHint, 10);
    if (port <= 0 || port > 65535) {
      return null;
    }

    const resolved = new URL(url.origin);
    resolved.port = String(port);
    return resolved.origin;
  }

  try {
    const host = new URL(hostHint ?? url.origin);
    return isHttpUrl(host) && !hasCredentials(host) && host.pathname === "/" && !host.search && !host.hash && Boolean(host.port)
      ? host.origin
      : null;
  } catch {
    return null;
  }
}

function getPcUrl(parameters: URLSearchParams, fallbackPcUrl: string): string {
  const hostHints = parameters.getAll("h");
  if (hostHints.length !== 1) {
    return fallbackPcUrl;
  }

  const hostHint = hostHints[0]!.trim();
  if (/^\d{1,5}$/.test(hostHint)) {
    const port = Number.parseInt(hostHint, 10);
    if (port > 0 && port <= 65535) {
      try {
        const fallback = new URL(fallbackPcUrl);
        fallback.port = String(port);
        return fallback.origin;
      } catch {
        return fallbackPcUrl;
      }
    }
  }

  return normalizePcUrl(hostHint) ?? fallbackPcUrl;
}

function isHttpUrl(url: URL): boolean {
  return url.protocol === "http:" || url.protocol === "https:";
}

function hasCredentials(url: URL): boolean {
  return Boolean(url.username || url.password);
}

function invalid(message: string): ManualConnectionValidation {
  return { valid: false, message };
}

function validHost(pcUrl: string): ManualConnectionValidation {
  return { valid: true, target: { kind: "host", pcUrl } };
}
