import { beforeEach, describe, expect, it, vi } from "vitest";
import { loadTextSnippets, saveTextSnippets, textSnippetsKey } from "./textSnippets";

beforeEach(() => {
  const items = new Map<string, string>();
  vi.stubGlobal("localStorage", {
    getItem: (key: string) => items.get(key) ?? null,
    setItem: (key: string, value: string) => items.set(key, value)
  });
});

describe("text snippets", () => {
  it("round trips valid local snippets", () => {
    const snippets = [{ id: "snippet-1", name: "Email", text: "hello@example.com" }];
    saveTextSnippets("client-a", snippets);
    expect(loadTextSnippets("client-a")).toEqual(snippets);
  });

  it("drops malformed, duplicate, and overlong entries", () => {
    localStorage.setItem(textSnippetsKey("client-a"), JSON.stringify([
      { id: "snippet-1", name: "Valid", text: "hello" },
      { id: "snippet-1", name: "Duplicate", text: "ignored" },
      { id: "bad id", name: "Invalid", text: "ignored" },
      { id: "snippet-2", name: "Too long", text: "x".repeat(4097) }
    ]));
    expect(loadTextSnippets("client-a")).toEqual([{ id: "snippet-1", name: "Valid", text: "hello" }]);
  });
});
