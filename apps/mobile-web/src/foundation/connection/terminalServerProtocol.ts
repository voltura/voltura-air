import type { TerminalServerMessage } from "../protocol/messages";

export function parseTerminalServerMessage(data: unknown): TerminalServerMessage | null {
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
  if (value.type === "terminal.offer") {
    return exact(value, [
      "type",
      "operationId",
      "terminalId",
      "columns",
      "rows",
      "acknowledgedOffset",
      "offerSdp",
      "hostSignature",
      "iceServers",
      "turnExpiresAt",
    ]) &&
      id(value.operationId) &&
      terminalId(value.terminalId) &&
      integer(value.columns, 10, 500) &&
      integer(value.rows, 5, 300) &&
      offset(value.acknowledgedOffset) &&
      text(value.offerSdp, 32 * 1024) &&
      text(value.hostSignature, 128) &&
      optional(value, "iceServers", ice) &&
      optional(value, "turnExpiresAt", (item) => text(item, 40))
      ? (value as unknown as TerminalServerMessage)
      : null;
  }
  if (value.type === "terminal.start.result" || value.type === "terminal.attach.result") {
    return exact(value, ["type", "operationId", "succeeded", "code", "message", "terminalId"]) &&
      result(value) &&
      id(value.operationId) &&
      optional(value, "terminalId", terminalId) &&
      (value.succeeded !== true || terminalId(value.terminalId))
      ? (value as unknown as TerminalServerMessage)
      : null;
  }
  if (value.type === "terminal.answer.result" || value.type === "terminal.stop.result") {
    return exact(value, ["type", "operationId", "succeeded", "code", "message"]) &&
      result(value) &&
      id(value.operationId)
      ? (value as unknown as TerminalServerMessage)
      : null;
  }
  if (value.type === "terminal.ended") {
    return exact(value, ["type", "terminalId", "reason"]) &&
      terminalId(value.terminalId) &&
      text(value.reason, 80)
      ? (value as unknown as TerminalServerMessage)
      : null;
  }
  if (value.type === "terminal.status") {
    return exact(value, ["type", "terminalId", "state", "acknowledgedOffset"]) &&
      terminalId(value.terminalId) &&
      (value.state === "connecting" || value.state === "active" || value.state === "detached") &&
      offset(value.acknowledgedOffset)
      ? (value as unknown as TerminalServerMessage)
      : null;
  }
  return null;
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
function terminalId(value: unknown): value is string {
  return typeof value === "string" && /^[a-f0-9]{32}$/u.test(value);
}
function integer(value: unknown, minimum: number, maximum: number): value is number {
  return Number.isInteger(value) && (value as number) >= minimum && (value as number) <= maximum;
}
function offset(value: unknown): value is number {
  return Number.isSafeInteger(value) && (value as number) >= 0;
}
function optional(
  value: Record<string, unknown>,
  name: string,
  validate: (item: unknown) => boolean,
): boolean {
  return !(name in value) || value[name] === null || validate(value[name]);
}
function exact(value: Record<string, unknown>, names: string[]): boolean {
  return Object.keys(value).every((name) => names.includes(name));
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
