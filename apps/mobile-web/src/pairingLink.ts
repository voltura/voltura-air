export interface PairingLink {
  pairToken: string;
  pcUrl: string;
}

export function parsePairingLink(source: string, fallbackPcUrl: string): PairingLink | null {
  const trimmedSource = source.trim();
  if (!trimmedSource) {
    return null;
  }

  try {
    const url = new URL(trimmedSource);
    return getPairingLink(url.searchParams, url.origin);
  } catch {
    return getPairingLink(new URLSearchParams(trimmedSource), fallbackPcUrl);
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

function getPairingLink(parameters: URLSearchParams, fallbackPcUrl: string): PairingLink | null {
  const pairToken = parameters.get("t");
  if (!pairToken) {
    return null;
  }

  return {
    pairToken,
    pcUrl: getPcUrl(parameters, fallbackPcUrl)
  };
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
