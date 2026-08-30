import type { AppsServerMessage } from "../protocol/messages";

const opaqueIdPattern = /^[a-f0-9]{32}$/u;

export function parseAppsServerMessage(data: unknown): AppsServerMessage | null {
  if (typeof data !== "string" || data.length > 96 * 1024) {
    return null;
  }

  let value: unknown;
  try {
    value = JSON.parse(data);
  } catch {
    return null;
  }
  if (!record(value) || typeof value.type !== "string") {
    return null;
  }

  if (value.type === "apps.list.result") {
    if (
      !exact(value, [
        "type",
        "operationId",
        "succeeded",
        "code",
        "message",
        "revision",
        "windows",
      ]) ||
      !result(value) ||
      !id(value.operationId) ||
      !Object.hasOwn(value, "windows") ||
      !Array.isArray(value.windows) ||
      value.windows.length > 48 ||
      !value.windows.every(windowSummary)
    ) {
      return null;
    }

    const ids = value.windows.map((window) => (window as Record<string, unknown>).windowId);
    const successShape = opaqueId(value.revision) && ids.length === new Set(ids).size;
    const failureShape = value.revision === undefined && value.windows.length === 0;
    return value.succeeded === true
      ? successShape
        ? (value as unknown as AppsServerMessage)
        : null
      : failureShape
        ? (value as unknown as AppsServerMessage)
        : null;
  }

  if (value.type === "apps.activate.result" || value.type === "apps.close.result") {
    return exact(value, ["type", "operationId", "windowId", "succeeded", "code", "message"]) &&
      result(value) &&
      id(value.operationId) &&
      opaqueId(value.windowId)
      ? (value as unknown as AppsServerMessage)
      : null;
  }

  if (value.type === "apps.preview.offer") {
    return exact(value, [
      "type",
      "operationId",
      "previewId",
      "offerSdp",
      "hostSignature",
      "iceServers",
      "turnExpiresAt",
    ]) &&
      id(value.operationId) &&
      opaqueId(value.previewId) &&
      text(value.offerSdp, 32 * 1024) &&
      text(value.hostSignature, 128) &&
      optional(value, "iceServers", ice) &&
      optional(value, "turnExpiresAt", (item) => text(item, 40))
      ? (value as unknown as AppsServerMessage)
      : null;
  }

  if (value.type === "apps.preview.answer.result") {
    return exact(value, ["type", "operationId", "succeeded", "code", "message"]) &&
      result(value) &&
      id(value.operationId)
      ? (value as unknown as AppsServerMessage)
      : null;
  }

  if (value.type === "apps.preview.ended") {
    return exact(value, ["type", "previewId", "reason", "message"]) &&
      opaqueId(value.previewId) &&
      text(value.reason, 80) &&
      text(value.message, 240)
      ? (value as unknown as AppsServerMessage)
      : null;
  }

  return null;
}

function windowSummary(value: unknown): boolean {
  return (
    record(value) &&
    exact(value, [
      "windowId",
      "title",
      "applicationName",
      "active",
      "minimized",
      "maximizeSupported",
      "previewSupported",
    ]) &&
    opaqueId(value.windowId) &&
    text(value.title, 256) &&
    text(value.applicationName, 128) &&
    ["active", "minimized", "maximizeSupported", "previewSupported"].every(
      (name) => typeof value[name] === "boolean",
    )
  );
}

function record(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function text(value: unknown, maximum: number): value is string {
  return typeof value === "string" && value.length > 0 && value.length <= maximum;
}

function id(value: unknown): value is string {
  return text(value, 64) && /^[A-Za-z0-9-]+$/u.test(value);
}

function opaqueId(value: unknown): value is string {
  return typeof value === "string" && opaqueIdPattern.test(value);
}

function exact(value: Record<string, unknown>, names: string[]): boolean {
  return Object.keys(value).every((name) => names.includes(name));
}

function optional(
  value: Record<string, unknown>,
  name: string,
  validate: (item: unknown) => boolean,
): boolean {
  return !(name in value) || value[name] === null || validate(value[name]);
}

function result(value: Record<string, unknown>): boolean {
  return (
    typeof value.succeeded === "boolean" &&
    text(value.message, 240) &&
    optional(value, "code", (item) => text(item, 80))
  );
}

function ice(value: unknown): boolean {
  return (
    Array.isArray(value) &&
    value.length > 0 &&
    value.length <= 2 &&
    value.every(
      (server) =>
        record(server) &&
        exact(server, ["urls", "username", "credential"]) &&
        text(server.username, 512) &&
        text(server.credential, 512) &&
        Array.isArray(server.urls) &&
        server.urls.length > 0 &&
        server.urls.length <= 4 &&
        server.urls.every(
          (url) => typeof url === "string" && url.length <= 512 && /^(?:turn|turns):/u.test(url),
        ),
    )
  );
}
