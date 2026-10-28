import { afterEach, describe, expect, it, vi } from "vitest";
import { copyTextToClipboard } from "./mobileDiagnostics";

const originalCommand = Object.getOwnPropertyDescriptor(document, "execCommand");
afterEach(() => {
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
  document.body.replaceChildren();
  if (originalCommand) {
    Object.defineProperty(document, "execCommand", originalCommand);
  } else {
    Reflect.deleteProperty(document, "execCommand");
  }
});

describe("legacy clipboard ownership", () => {
  it.each([true, false])(
    "restores input focus and selection after copying (success %s)",
    async (succeeded) => {
      vi.stubGlobal("navigator", {});
      const input = document.createElement("textarea");
      input.value = "sample text";
      document.body.append(input);
      input.focus();
      input.setSelectionRange(2, 6, "backward");
      Object.defineProperty(document, "execCommand", {
        configurable: true,
        value: () => {
          if (!succeeded) {
            throw new Error("unsupported");
          }
          return true;
        },
      });
      expect(await copyTextToClipboard("copied text")).toBe(succeeded ? "copied" : "manual");
      expect(document.activeElement).toBe(input);
      expect(input.selectionStart).toBe(2);
      expect(input.selectionEnd).toBe(6);
      expect(input.selectionDirection).toBe("backward");
      expect(document.querySelectorAll("textarea")).toHaveLength(1);
    },
  );

  it("stages the temporary selection inside an active modal", async () => {
    vi.stubGlobal("navigator", {});
    const dialog = document.createElement("dialog");
    dialog.setAttribute("open", "");
    const button = document.createElement("button");
    dialog.append(button);
    document.body.append(dialog);
    button.focus();
    Object.defineProperty(document, "execCommand", {
      configurable: true,
      value: () => dialog.contains(document.activeElement),
    });
    expect(await copyTextToClipboard("sample")).toBe("copied");
    expect(document.activeElement).toBe(button);
    expect(dialog.querySelector("textarea")).toBeNull();
  });
});
