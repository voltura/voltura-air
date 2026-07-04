import { getPcDisplayName } from "./pcDisplayName";
import { getWebSocketUrl, type PcProfile } from "./pcProfiles";
import type { HostStatusMetadata } from "./protocol";

type MobileDiagnosticsInput = {
  activePc: PcProfile | null;
  connectionState: string;
  lastErrorCode?: string | null;
  lastErrorMessage?: string | null;
  message: string;
  pairedPcCount: number;
  hostStatus?: HostStatusMetadata | null;
};

const sensitiveQueryKeys = new Set(["t", "token", "pairtoken", "pair-token", "secret", "secrethash", "secret-hash", "hash", "d", "device", "deviceid", "device-id"]);
const sensitiveObjectKeyPattern = /(token|secret|hash|clientid|client-id|deviceid|device-id)/i;

export function buildMobileDiagnostics(input: MobileDiagnosticsInput): string {
  const activePcUrl = parseUrl(input.activePc?.url ?? null);
  const fallbackWebSocketUrl = input.activePc ? getWebSocketUrl(input.activePc) : null;
  const currentWebSocketUrl = input.hostStatus?.webSocketUrl ?? fallbackWebSocketUrl;
  const diagnostics = redactSensitiveValues({
    product: "Voltura Air",
    hostVersion: input.hostStatus?.hostVersion ?? null,
    webClientVersion: __APP_VERSION__,
    pcName: input.hostStatus?.pcName ?? (input.activePc ? getPcDisplayName(input.activePc) : null),
    selectedAdapterName: input.hostStatus?.selectedAdapterName ?? null,
    selectedIp: input.hostStatus?.selectedIp ?? activePcUrl?.hostname ?? null,
    selectedPort: input.hostStatus?.selectedPort ?? (activePcUrl?.port ? Number.parseInt(activePcUrl.port, 10) : defaultPortForProtocol(activePcUrl?.protocol)),
    activePcUrl: activePcUrl ? sanitizeUrl(activePcUrl.toString()) : null,
    currentWebSocketUrl: currentWebSocketUrl ? sanitizeUrl(currentWebSocketUrl) : null,
    pairingState: input.connectionState,
    lastErrorCode: input.lastErrorCode ?? null,
    lastErrorMessage: input.lastErrorMessage ?? null,
    pairedPcCount: input.pairedPcCount,
    browserUserAgent: navigator.userAgent,
    pageUrl: sanitizeUrl(safeLocationHref()),
    displayMode: getDisplayMode(),
    timestamp: new Date().toISOString()
  });

  return JSON.stringify(diagnostics, null, 2);
}


export async function copyTextToClipboard(value: string): Promise<"copied" | "manual"> {
  if (navigator.clipboard?.writeText && window.isSecureContext) {
    try {
      await navigator.clipboard.writeText(value);
      return "copied";
    } catch {
    }
  }

  if (tryLegacyCopy(value)) {
    return "copied";
  }

  return "manual";
}

function tryLegacyCopy(value: string): boolean {
  if (typeof document.execCommand !== "function") {
    return false;
  }

  const textarea = document.createElement("textarea");
  textarea.value = value;
  textarea.setAttribute("readonly", "true");
  textarea.style.position = "fixed";
  textarea.style.top = "0";
  textarea.style.left = "-9999px";
  textarea.style.opacity = "0";
  textarea.style.pointerEvents = "none";
  document.body.appendChild(textarea);

  try {
    textarea.focus();
    textarea.select();
    textarea.setSelectionRange(0, textarea.value.length);
    return document.execCommand("copy");
  } catch {
    return false;
  } finally {
    document.body.removeChild(textarea);
  }
}

export function sanitizeUrl(value: string): string {
  try {
    const url = new URL(value);
    for (const key of Array.from(url.searchParams.keys())) {
      if (isSensitiveQueryKey(key)) {
        url.searchParams.set(key, "[redacted]");
      }
    }

    url.username = "";
    url.password = "";
    return url.toString();
  } catch {
    return redactSensitiveText(value);
  }
}

function parseUrl(value: string | null): URL | null {
  if (!value) {
    return null;
  }

  try {
    return new URL(value);
  } catch {
    return null;
  }
}

function defaultPortForProtocol(protocol: string | undefined): number | null {
  if (protocol === "http:") {
    return 80;
  }

  if (protocol === "https:") {
    return 443;
  }

  return null;
}

function safeLocationHref(): string {
  try {
    return window.location.href;
  } catch {
    return "";
  }
}

function getDisplayMode(): "browser" | "installed" | "unknown" {
  if (window.matchMedia("(display-mode: standalone)").matches || window.navigator.standalone === true) {
    return "installed";
  }

  if (window.matchMedia("(display-mode: browser)").matches) {
    return "browser";
  }

  return "unknown";
}

function isSensitiveQueryKey(key: string): boolean {
  return sensitiveQueryKeys.has(key.toLowerCase());
}

function redactSensitiveValues(value: unknown, key = ""): unknown {
  if (sensitiveObjectKeyPattern.test(key)) {
    return "[redacted]";
  }

  if (typeof value === "string") {
    return redactSensitiveText(value);
  }

  if (Array.isArray(value)) {
    return value.map((item) => redactSensitiveValues(item));
  }

  if (value && typeof value === "object") {
    return Object.fromEntries(Object.entries(value).map(([entryKey, entryValue]) => [entryKey, redactSensitiveValues(entryValue, entryKey)]));
  }

  return value;
}

function redactSensitiveText(value: string): string {
  return value
    .replace(/([?&](?:t|token|pairToken|secret|secretHash|hash|d)=)[^&#]+/gi, "$1[redacted]")
    .replace(/("(?:pairToken|token|secret|secretHash|hash|clientId|deviceId)"\s*:\s*")[^"]+(")/gi, "$1[redacted]$2");
}
