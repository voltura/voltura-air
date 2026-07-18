import { describe, expect, it } from "vitest";
import { validateUrlDraft } from "./urlOpenValidation";

describe("validateUrlDraft", () => {
  it.each([
    ["example.com", "https://example.com/"],
    [" example.com/page?q=test ", "https://example.com/page?q=test"],
    ["https://example.com", "https://example.com/"],
    ["http://192.168.1.1", "http://192.168.1.1/"],
    ["localhost:3000/path", "https://localhost:3000/path"],
    ["router", "https://router/"],
    ["[::1]", "https://[::1]/"]
  ])("accepts %s without requiring a dotted host", (value, normalizedUrl) => {
    expect(validateUrlDraft(value)).toEqual({ valid: true, normalizedUrl });
  });

  it.each([
    ["", "Enter a web address."],
    ["https://", "Enter a valid web address."],
    ["not a valid host", "Enter a valid web address."],
    ["javascript:alert(1)", "Use an HTTP or HTTPS web address."],
    ["data:text/plain,hello", "Use an HTTP or HTTPS web address."],
    ["mailto:user@example.com", "Use an HTTP or HTTPS web address."],
    ["file:///C:/Windows", "Use an HTTP or HTTPS web address."],
    ["https://example.com/\u0000hidden", "Enter a valid web address."]
  ])("rejects %s before it can be sent", (value, message) => {
    expect(validateUrlDraft(value)).toEqual({ valid: false, message });
  });

  it("rejects addresses over the host limit", () => {
    expect(validateUrlDraft(`https://${"a".repeat(2_048)}`)).toEqual({
      valid: false,
      message: "The web address is too long."
    });
  });
});
