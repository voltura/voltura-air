import type { AiAssistantServerMessage } from "../protocol/messages";

export function parseAiAssistantServerMessage(data: unknown): AiAssistantServerMessage | null {
  if (typeof data !== "string" || data.length > 40 * 1024) {
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

  if (
    [
      "ai.assistant.open.result",
      "ai.assistant.ask.result",
      "ai.assistant.reset.result",
      "ai.assistant.close.result",
    ].includes(value.type)
  ) {
    return exact(value, ["type", "operationId", "succeeded", "code", "message"]) &&
      id(value.operationId) &&
      typeof value.succeeded === "boolean" &&
      text(value.message, 240) &&
      optional(value, "code", (candidate) => text(candidate, 80))
      ? (value as unknown as AiAssistantServerMessage)
      : null;
  }
  if (value.type === "ai.assistant.message") {
    return exact(value, [
      "type",
      "sequence",
      "messageId",
      "chunkIndex",
      "finalChunk",
      "sender",
      "text",
    ]) &&
      Number.isSafeInteger(value.sequence) &&
      (value.sequence as number) > 0 &&
      messageId(value.messageId) &&
      Number.isSafeInteger(value.chunkIndex) &&
      (value.chunkIndex as number) >= 0 &&
      (value.chunkIndex as number) < 8 &&
      typeof value.finalChunk === "boolean" &&
      (value.sender === "user" || value.sender === "assistant") &&
      text(value.text, 4 * 1024)
      ? (value as unknown as AiAssistantServerMessage)
      : null;
  }
  if (value.type === "ai.assistant.state") {
    return exact(value, ["type", "state", "message"]) &&
      ["ready", "working", "failed"].includes(String(value.state)) &&
      optional(value, "message", (candidate) => text(candidate, 240))
      ? (value as unknown as AiAssistantServerMessage)
      : null;
  }
  if (value.type === "ai.assistant.snapshot.start") {
    return exact(value, ["type"]) ? (value as unknown as AiAssistantServerMessage) : null;
  }
  if (value.type === "ai.assistant.snapshot.complete") {
    return exact(value, ["type", "messageCount"]) &&
      Number.isInteger(value.messageCount) &&
      (value.messageCount as number) >= 0 &&
      (value.messageCount as number) <= 32
      ? (value as unknown as AiAssistantServerMessage)
      : null;
  }
  if (value.type === "ai.assistant.closed") {
    return exact(value, ["type", "reason"]) && text(value.reason, 80)
      ? (value as unknown as AiAssistantServerMessage)
      : null;
  }
  return null;
}

function record(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}
function exact(value: Record<string, unknown>, fields: string[]): boolean {
  return Object.keys(value).every((field) => fields.includes(field));
}
function text(value: unknown, maximum: number): value is string {
  return typeof value === "string" && value.length > 0 && value.length <= maximum;
}
function id(value: unknown): value is string {
  return text(value, 64) && /^[A-Za-z0-9-]+$/u.test(value);
}
function messageId(value: unknown): value is string {
  return typeof value === "string" && /^[A-Fa-f0-9]{64}$/u.test(value);
}
function optional(
  value: Record<string, unknown>,
  field: string,
  validate: (candidate: unknown) => boolean,
): boolean {
  return !(field in value) || value[field] === null || validate(value[field]);
}
