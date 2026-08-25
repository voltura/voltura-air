import type { FileTransferServerMessage } from "../protocol/messages";

export function parseFileTransferServerMessage(data: unknown): FileTransferServerMessage | null {
  if (typeof data !== "string" || data.length > 96 * 1024) {return null;}
  let value: unknown;
  try {value = JSON.parse(data);} catch {return null;}
  if (!record(value) || typeof value.type !== "string") {return null;}
  const frame = value;
  if (value.type === "file.transfer.offer") {
    return exact(frame, ["type", "transferId", "direction", "fileName", "declaredSize", "offerSdp", "hostSignature", "iceServers", "turnExpiresAt", "relayUsageBytes", "relayUsageCheckedAt"]) &&
      transfer(frame) && text(frame.fileName, 255) && size(frame.declaredSize) && text(frame.offerSdp, 32 * 1024) && text(frame.hostSignature, 128) &&
      optional(frame, "iceServers", ice) && optional(frame, "turnExpiresAt", (item) => text(item, 40)) &&
      optional(frame, "relayUsageBytes", size) && optional(frame, "relayUsageCheckedAt", (item) => text(item, 40)) ? accepted(value) : null;
  }
  if (value.type === "file.transfer.status") {
    return exact(frame, ["type", "transferId", "direction", "state", "bytesCompleted", "bytesTotal"]) && transfer(frame) &&
      ["queued", "connecting", "transferring"].includes(String(frame.state)) && size(frame.bytesCompleted) && size(frame.bytesTotal) &&
      frame.bytesCompleted <= frame.bytesTotal ? accepted(value) : null;
  }
  if (value.type === "file.transfer.result") {
    return exact(frame, ["type", "transferId", "direction", "succeeded", "code", "message", "fileName", "declaredSize", "jobId"]) &&
      transfer(frame) && result(frame) && text(frame.fileName, 255) && size(frame.declaredSize) && optional(frame, "jobId", id) ? accepted(value) : null;
  }
  if (value.type === "file.transfer.start.result") {
    if (!exact(frame, ["type", "operationId", "succeeded", "code", "message", "transferId", "job"]) || !result(frame) || !id(frame.operationId) ||
      !optional(frame, "transferId", id) || !optional(frame, "job", fileJob)) {return null;}
    const succeeded = frame.succeeded === true;
    return succeeded === (id(frame.transferId)) && (succeeded || !("job" in frame) || frame.job === null) ? accepted(value) : null;
  }
  if (value.type === "file.transfer.answer.result" || value.type === "file.transfer.cancel.result") {
    return exact(frame, ["type", "operationId", "succeeded", "code", "message"]) && result(frame) && id(frame.operationId) ? accepted(value) : null;
  }
  return null;
}

function accepted(value: unknown): FileTransferServerMessage {return value as FileTransferServerMessage;}
function record(value: unknown): value is Record<string, unknown> {return typeof value === "object" && value !== null && !Array.isArray(value);}
function text(value: unknown, maximum: number): value is string {return typeof value === "string" && value.length <= maximum && value.trim().length > 0;}
function id(value: unknown): value is string {return text(value, 64) && /^[A-Za-z0-9-]+$/u.test(value);}
function size(value: unknown): value is number {return typeof value === "number" && Number.isSafeInteger(value) && value >= 0;}
function optional(value: Record<string, unknown>, name: string, validate: (item: unknown) => boolean): boolean {return !(name in value) || value[name] === null || validate(value[name]);}
function exact(value: Record<string, unknown>, names: string[]): boolean {return Object.keys(value).every((name) => names.includes(name));}
function transfer(value: Record<string, unknown>): boolean {return id(value.transferId) && (value.direction === "download" || value.direction === "upload");}
function result(value: Record<string, unknown>): boolean {
  return typeof value.succeeded === "boolean" && text(value.message, 240) && optional(value, "code", (item) => text(item, 80));
}
function fileJob(value: unknown): boolean {
  return record(value) && text(value.jobId, 512) && ["copy", "move", "paste", "delete", "rename", "upload"].includes(String(value.operation)) &&
    ["queued", "preparing", "running", "paused", "needs-attention", "canceling", "completed", "failed", "canceled", "interrupted"].includes(String(value.state)) &&
    ["queuePosition", "itemsCompleted", "itemsTotal", "bytesCompleted", "bytesTotal"].every((name) => typeof value[name] === "number" && Number.isFinite(value[name]) && value[name] >= 0) &&
    typeof value.canPause === "boolean" && typeof value.canResume === "boolean" && typeof value.canCancel === "boolean";
}
function ice(value: unknown): boolean {
  return Array.isArray(value) && value.length > 0 && value.length <= 2 && value.every((server) => record(server) && exact(server, ["urls", "username", "credential"]) &&
    text(server.username, 512) && text(server.credential, 512) && Array.isArray(server.urls) && server.urls.length > 0 && server.urls.length <= 4 && server.urls.every((url) => {
      if (typeof url !== "string" || url.length > 512) {return false;}
      const match = /^(?:turn|turns):[A-Za-z0-9.-]+(?::([0-9]{1,5}))?(?:\?transport=(?:tcp|udp))?$/u.exec(url);
      if (!match) {return false;}
      return match[1] === undefined || Number(match[1]) >= 1 && Number(match[1]) <= 65_535;
    }));
}
