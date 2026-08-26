import { afterEach, describe, expect, it, vi } from "vitest";
import {
  canReadTextFromDeviceClipboard,
  canWriteDeferredTextToDeviceClipboard,
  canWriteTextToDeviceClipboard,
  readTextFromDeviceClipboard,
  writeDeferredTextToDeviceClipboard,
  writeTextToDeviceClipboard,
} from "./deviceClipboard";

const originalClipboard = navigator.clipboard;
const originalSecureContext = window.isSecureContext;
const originalClipboardItem = globalThis.ClipboardItem;

function setClipboard(value: Partial<Clipboard> | undefined) {
  Object.defineProperty(navigator, "clipboard", { configurable: true, value });
}

function setSecureContext(value: boolean) {
  Object.defineProperty(window, "isSecureContext", { configurable: true, value });
}

function setClipboardItem(value: typeof ClipboardItem | undefined) {
  Object.defineProperty(globalThis, "ClipboardItem", { configurable: true, value });
}

afterEach(() => {
  Object.defineProperty(navigator, "clipboard", { configurable: true, value: originalClipboard });
  Object.defineProperty(window, "isSecureContext", {
    configurable: true,
    value: originalSecureContext,
  });
  setClipboardItem(originalClipboardItem);
});

describe("deviceClipboard", () => {
  it("reads and writes text only in a supported secure context", async () => {
    const readText = vi.fn().mockResolvedValue("Phone text");
    const writeText = vi.fn().mockResolvedValue(undefined);
    setSecureContext(true);
    setClipboard({ readText, writeText });

    expect(canReadTextFromDeviceClipboard()).toBe(true);
    expect(canWriteTextToDeviceClipboard()).toBe(true);
    await expect(readTextFromDeviceClipboard()).resolves.toEqual({
      status: "success",
      text: "Phone text",
    });
    await expect(writeTextToDeviceClipboard("PC text")).resolves.toEqual({ status: "copied" });
    expect(writeText).toHaveBeenCalledExactlyOnceWith("PC text");
  });

  it("reports empty clipboard text", async () => {
    setSecureContext(true);
    setClipboard({ readText: vi.fn().mockResolvedValue("") });

    await expect(readTextFromDeviceClipboard()).resolves.toEqual({ status: "empty" });
  });

  it.each([false, true])(
    "reports unavailable when secure context is %s without full API support",
    async (secure) => {
      setSecureContext(secure);
      setClipboard(undefined);

      expect(canReadTextFromDeviceClipboard()).toBe(false);
      expect(canWriteTextToDeviceClipboard()).toBe(false);
      await expect(readTextFromDeviceClipboard()).resolves.toEqual({ status: "unavailable" });
      await expect(writeTextToDeviceClipboard("Text")).resolves.toEqual({ status: "unavailable" });
    },
  );

  it("does not expose clipboard methods from an insecure context", async () => {
    const readText = vi.fn().mockResolvedValue("Text");
    const writeText = vi.fn().mockResolvedValue(undefined);
    setSecureContext(false);
    setClipboard({ readText, writeText });

    expect(canReadTextFromDeviceClipboard()).toBe(false);
    expect(canWriteTextToDeviceClipboard()).toBe(false);
    await expect(readTextFromDeviceClipboard()).resolves.toEqual({ status: "unavailable" });
    await expect(writeTextToDeviceClipboard("Text")).resolves.toEqual({ status: "unavailable" });
    expect(readText).not.toHaveBeenCalled();
    expect(writeText).not.toHaveBeenCalled();
  });

  it("bounds permission denials and other failures", async () => {
    setSecureContext(true);
    setClipboard({
      readText: vi.fn().mockRejectedValue(new DOMException("Denied", "NotAllowedError")),
      writeText: vi.fn().mockRejectedValue(new Error("Unavailable")),
    });

    await expect(readTextFromDeviceClipboard()).resolves.toEqual({ status: "denied" });
    await expect(writeTextToDeviceClipboard("Text")).resolves.toEqual({ status: "failed" });
  });

  it("starts a deferred text write before its clipboard item data resolves", async () => {
    let suppliedData: Record<string, Promise<Blob>> | undefined;
    class TestClipboardItem {
      constructor(data: Record<string, Promise<Blob>>) {
        suppliedData = data;
      }
    }
    let resolveText: ((value: Blob) => void) | undefined;
    const text = new Promise<Blob>((resolve) => {
      resolveText = resolve;
    });
    const write = vi.fn().mockResolvedValue(undefined);
    setSecureContext(true);
    setClipboard({ write });
    setClipboardItem(TestClipboardItem as typeof ClipboardItem);

    expect(canWriteDeferredTextToDeviceClipboard()).toBe(true);
    const result = writeDeferredTextToDeviceClipboard(text);
    expect(write).toHaveBeenCalledOnce();
    expect(suppliedData?.["text/plain"]).toBe(text);

    resolveText?.(new Blob(["PC text"], { type: "text/plain" }));
    await expect(result).resolves.toEqual({ status: "copied" });
  });

  it("omits deferred writes without secure ClipboardItem support", async () => {
    setSecureContext(false);
    setClipboard({ write: vi.fn() });
    setClipboardItem(undefined);

    expect(canWriteDeferredTextToDeviceClipboard()).toBe(false);
    await expect(writeDeferredTextToDeviceClipboard(Promise.resolve(new Blob()))).resolves.toEqual({
      status: "unavailable",
    });
  });
});
