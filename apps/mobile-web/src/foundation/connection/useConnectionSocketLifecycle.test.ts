import { describe, expect, it } from "vitest";
import { getConnectionTimeoutMs, getLazyProtocolMessageType } from "./useConnectionSocketLifecycle";

describe("getConnectionTimeoutMs", () => {
  it("keeps direct connections at three seconds", () => {
    expect(getConnectionTimeoutMs(undefined)).toBe(3000);
  });

  it("allows relay connections ten seconds", () => {
    expect(getConnectionTimeoutMs("relay")).toBe(10000);
  });
});

describe("getLazyProtocolMessageType", () => {
  it("routes by the top-level type instead of protocol names inside message text", () => {
    expect(
      getLazyProtocolMessageType(
        JSON.stringify({
          type: "ai.assistant.message",
          text: 'Use "terminal.start" or "file.transfer.open" only when requested.',
        }),
      ),
    ).toBe("ai.assistant.message");
  });

  it("rejects malformed and non-object frames", () => {
    expect(getLazyProtocolMessageType("not json")).toBeNull();
    expect(getLazyProtocolMessageType(JSON.stringify(["ai.assistant.message"]))).toBeNull();
  });
});
