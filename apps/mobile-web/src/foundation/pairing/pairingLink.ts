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
const invalidHostMessage = "Enter a host with a valid port, for example 192.168.1.50:51395.";
const invalidPairingLinkMessage = "Enter the complete pairing link shown by Voltura Air on the PC.";

export function parsePairingLink(source: string): PairingLink | null {
  const trimmedSource = source.trim();
  if (!trimmedSource) {
    return null;
  }

  try {
    const url = new URL(trimmedSource);
    if (!isHttpUrl(url) || hasCredentials(url) || url.pathname !== pairingPath || url.hash) {
      return null;
    }

    const tokens = url.searchParams.getAll("t");
    const versions = url.searchParams.getAll("v");
    if (tokens.length !== 1 || !pairingTokenPattern.test(tokens[0]!) ||
        versions.length !== 1 || !versionPattern.test(versions[0]!)) {
      return null;
    }

    const pcUrl = resolvePairingPcUrl(url);
    return pcUrl ? { pairToken: tokens[0]!, pcUrl } : null;
  } catch {
    return null;
  }
}

export function hasPairingTokenParameter(source: string): boolean {
  try {
    return new URL(source).searchParams.has("t");
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

export function parsePcUrl(source: string, fallbackPcUrl: string): string {
  const trimmedSource = source.trim();
  if (!trimmedSource) {
    return normalizePcUrl(fallbackPcUrl, fallbackPcUrl);
  }

  try {
    const url = new URL(trimmedSource);
    return getPcUrl(url.searchParams, url.origin);
  } catch {
    return getPcUrl(new URLSearchParams(trimmedSource), fallbackPcUrl);
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
  return normalizePcUrl(parameters.get("h") ?? fallbackPcUrl, fallbackPcUrl);
}

function normalizePcUrl(pcUrl: string, fallbackPcUrl: string): string {
  if (/^\d{1,5}$/.test(pcUrl)) {
    const port = Number.parseInt(pcUrl, 10);
    if (port > 0 && port <= 65535) {
      const fallback = new URL(fallbackPcUrl);
      fallback.port = String(port);
      return fallback.origin;
    }
  }

  return new URL(pcUrl).origin;
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
