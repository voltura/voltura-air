import { describe, expect, it } from "vitest";
import { parseServerMessage } from "./connectionProtocol";
import { diagnosticsSnapshot } from "./serverFrameCatalog.testData";

describe("diagnostics protocol", () => {
  it("rejects fields outside the diagnostics allowlist", () => {
    expect(parseServerMessage(JSON.stringify({
      type: "diagnostics.get.result",
      operationId: "diagnostics-1",
      succeeded: true,
      message: "Diagnostics loaded.",
      snapshot: { ...diagnosticsSnapshot, userName: "not allowed" }
    }))).toBeNull();
  });
});
