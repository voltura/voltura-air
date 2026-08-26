import { describe, expect, it } from "vitest";
import { parseServerMessage } from "./connectionProtocol";

describe("Files capability compatibility", () => {
  it("accepts omission and a Boolean transfer flag while rejecting malformed values", () => {
    const capability = {
      canBrowse: true,
      canModify: true,
      hidesProtectedSystemItems: true,
      maxPageSize: 100,
    };
    expect(
      parseServerMessage(
        JSON.stringify({
          type: "status",
          connected: true,
          capabilities: { fileManager: capability },
        }),
      ),
    ).not.toBeNull();
    expect(
      parseServerMessage(
        JSON.stringify({
          type: "status",
          connected: true,
          capabilities: { fileManager: { ...capability, canTransfer: true } },
        }),
      ),
    ).not.toBeNull();
    expect(
      parseServerMessage(
        JSON.stringify({
          type: "status",
          connected: true,
          capabilities: { fileManager: { ...capability, canTransfer: "yes" } },
        }),
      ),
    ).toBeNull();
  });
});
