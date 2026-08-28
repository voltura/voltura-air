import { describe, expect, it } from "vitest";
import { parseAiAssistantServerMessage } from "./aiAssistantServerProtocol";

describe("parseAiAssistantServerMessage", () => {
  it("accepts exact bounded assistant chunks", () => {
    const parsed = parseAiAssistantServerMessage(
      JSON.stringify({
        type: "ai.assistant.message",
        sequence: 4,
        messageId: "A".repeat(64),
        chunkIndex: 0,
        finalChunk: true,
        sender: "assistant",
        text: "Ready",
      }),
    );

    expect(parsed).toMatchObject({ type: "ai.assistant.message", text: "Ready" });
  });

  it("rejects unknown fields, oversized chunks, and invalid sequence data", () => {
    expect(
      parseAiAssistantServerMessage(
        JSON.stringify({
          type: "ai.assistant.message",
          sequence: 1,
          messageId: "A".repeat(64),
          chunkIndex: 0,
          finalChunk: true,
          sender: "assistant",
          text: "ok",
          unexpected: true,
        }),
      ),
    ).toBeNull();
    expect(
      parseAiAssistantServerMessage(
        JSON.stringify({
          type: "ai.assistant.message",
          sequence: 1,
          messageId: "A".repeat(64),
          chunkIndex: 0,
          finalChunk: true,
          sender: "assistant",
          text: "x".repeat(4 * 1024 + 1),
        }),
      ),
    ).toBeNull();
    expect(
      parseAiAssistantServerMessage(
        JSON.stringify({
          type: "ai.assistant.message",
          sequence: 0,
          messageId: "A".repeat(64),
          chunkIndex: -1,
          finalChunk: false,
          sender: "assistant",
          text: "ok",
        }),
      ),
    ).toBeNull();
    expect(
      parseAiAssistantServerMessage(
        JSON.stringify({
          type: "ai.assistant.message",
          sequence: 1,
          messageId: "message-1",
          chunkIndex: 0,
          finalChunk: true,
          sender: "assistant",
          text: "ok",
        }),
      ),
    ).toBeNull();
    expect(
      parseAiAssistantServerMessage(
        JSON.stringify({
          type: "ai.assistant.message",
          sequence: 1,
          messageId: "A".repeat(64),
          chunkIndex: 8,
          finalChunk: true,
          sender: "assistant",
          text: "ok",
        }),
      ),
    ).toBeNull();
  });
});
