import { describe, expect, it } from "vitest";
import { createLocalId } from "./localId";

describe("createLocalId", () => {
  it("creates protocol-safe IDs without requiring randomUUID", () => {
    const first = createLocalId();
    const second = createLocalId();

    expect(first).toMatch(/^[a-z0-9-]+$/);
    expect(second).not.toBe(first);
  });
});
