import { getDefaultDeviceName } from "../clientEnvironment";

const clientIdKey = "voltura-air.clientId";
const clientIdQueryParam = "d";
export const deviceNameKey = "voltura-air.deviceName";
const deviceNameQueryParam = "n";

export function getOrCreateClientId(source: string): string {
  const existing = localStorage.getItem(clientIdKey);
  if (existing) {
    return existing;
  }

  const created = getClientIdFromAddress(source) ?? createClientId();
  localStorage.setItem(clientIdKey, created);
  return created;
}

export function getClientIdFromAddress(source: string): string | null {
  try {
    const url = new URL(source);
    return normalizeClientId(url.searchParams.get(clientIdQueryParam));
  } catch {
    return normalizeClientId(new URLSearchParams(source).get(clientIdQueryParam));
  }
}

export function hasPcUrlInAddress(source: string): boolean {
  try {
    const url = new URL(source);
    return url.searchParams.has("h");
  } catch {
    return new URLSearchParams(source).has("h");
  }
}

function normalizeClientId(value: string | null): string | null {
  const trimmed = value?.trim();
  if (!trimmed || trimmed.length < 8 || trimmed.length > 128 || !/^[a-zA-Z0-9._:-]+$/.test(trimmed)) {
    return null;
  }

  return trimmed;
}

function getDeviceNameFromAddress(source: string): string | null {
  try {
    const url = new URL(source);
    return normalizeDeviceNameInput(url.searchParams.get(deviceNameQueryParam));
  } catch {
    return normalizeDeviceNameInput(new URLSearchParams(source).get(deviceNameQueryParam));
  }
}

export function normalizeDeviceNameInput(value: string | null): string | null {
  const trimmed = value?.trim();
  return trimmed && trimmed.length <= 80 ? trimmed : null;
}

export function ensureClientMetadataInAddress(clientId: string, deviceName: string): void {
  try {
    const url = new URL(window.location.href);
    const normalizedDeviceName = deviceName.trim() || getDefaultDeviceName();
    if (url.searchParams.get(clientIdQueryParam) === clientId && url.searchParams.get(deviceNameQueryParam) === normalizedDeviceName) {
      return;
    }

    url.searchParams.set(clientIdQueryParam, clientId);
    url.searchParams.set(deviceNameQueryParam, normalizedDeviceName);
    window.history.replaceState(null, "", url);
  } catch {
  }
}

function createClientId(): string {
  if (crypto.randomUUID) {
    return crypto.randomUUID();
  }

  if (crypto.getRandomValues) {
    const bytes = new Uint8Array(16);
    crypto.getRandomValues(bytes);
    bytes[6] = (bytes[6] & 0x0f) | 0x40;
    bytes[8] = (bytes[8] & 0x3f) | 0x80;
    const hex = Array.from(bytes, (byte) => byte.toString(16).padStart(2, "0")).join("");
    return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
  }

  return `client-${Date.now().toString(36)}-${Math.random().toString(36).slice(2)}`;
}

export function loadDeviceName(source: string): string {
  const existing = normalizeDeviceNameInput(localStorage.getItem(deviceNameKey));
  if (existing) {
    return existing;
  }

  const fromAddress = getDeviceNameFromAddress(source);
  if (fromAddress) {
    localStorage.setItem(deviceNameKey, fromAddress);
    return fromAddress;
  }

  return getDefaultDeviceName();
}

export function clearPairTokenFromAddress(): void {
  const url = new URL(window.location.href);
  if (!url.searchParams.has("t")) {
    return;
  }

  url.searchParams.delete("t");
  window.history.replaceState(null, "", url);
}
