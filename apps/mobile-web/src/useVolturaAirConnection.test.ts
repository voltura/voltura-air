import { describe, expect, it } from "vitest";
import { shouldClearStoredSecretForRejection } from "./useVolturaAirConnection";

describe("shouldClearStoredSecretForRejection", () => {
  it("keeps reconnect secrets for token and protocol-shape pairing failures", () => {
    expect(shouldClearStoredSecretForRejection("stale-token")).toBe(false);
    expect(shouldClearStoredSecretForRejection("expired-token")).toBe(false);
    expect(shouldClearStoredSecretForRejection("invalid-token")).toBe(false);
    expect(shouldClearStoredSecretForRejection("missing-token")).toBe(false);
    expect(shouldClearStoredSecretForRejection("rate-limited")).toBe(false);
    expect(shouldClearStoredSecretForRejection("invalid-message")).toBe(false);
  });

  it("clears reconnect secrets only when the host says the credential was revoked", () => {
    expect(shouldClearStoredSecretForRejection("device-revoked")).toBe(true);
    expect(shouldClearStoredSecretForRejection("secret-revoked")).toBe(true);
  });
});
