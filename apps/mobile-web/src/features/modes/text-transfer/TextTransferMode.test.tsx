import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { StrictMode, useState } from "react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { canReadTextFromDeviceClipboard, readTextFromDeviceClipboard } from "../../../foundation/platform/deviceClipboard";
import { TextTransferMode } from "./TextTransferMode";

vi.mock("../../../foundation/platform/deviceClipboard", () => ({
  canReadTextFromDeviceClipboard: vi.fn(),
  readTextFromDeviceClipboard: vi.fn()
}));

const props = {
  clearAfterSending: false,
  clientId: "test-client",
  draft: "",
  leftHandedButtons: false,
  onClearAfterSendingChange: vi.fn(),
  onDraftChange: vi.fn(),
  onPointerButtonClick: vi.fn(),
  onTouchCancel: vi.fn(),
  onTouchEnd: vi.fn(),
  onTouchMove: vi.fn(),
  onTouchStart: vi.fn(),
  pending: false,
  requestTextTransfer: vi.fn(),
  result: null,
  supported: true,
  target: { mode: "configured" as const, displayName: "Microsoft Word", available: true }
};

function TextTransferHarness({ initialDraft = "", onDraftChange = vi.fn() }: { initialDraft?: string; onDraftChange?: (value: string) => void }) {
  const [draft, setDraft] = useState(initialDraft);
  return <TextTransferMode {...props} draft={draft} onDraftChange={(value) => { setDraft(value); onDraftChange(value); }} />;
}

describe("TextTransferMode", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(canReadTextFromDeviceClipboard).mockReturnValue(true);
    vi.mocked(readTextFromDeviceClipboard).mockResolvedValue({ status: "success", text: "Phone" });
  });

  it("keeps managed delivery guidance compact and opens its details in a modal", () => {
    render(<TextTransferMode {...props} />);

    expect(screen.getByText("The PC creates a new item or draft.")).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "About Managed destination" }));

    const dialog = screen.getByRole("dialog", { name: "Managed destination" });
    expect(dialog.textContent).toContain("verifies that the intended window is in the foreground");
  });

  it("clears only the unchanged draft submitted with clear enabled", () => {
    const onDraftChange = vi.fn();
    const requestTextTransfer = vi.fn(() => "op-a");
    const view = render(<TextTransferMode {...props} clearAfterSending draft="Draft A" onDraftChange={onDraftChange} requestTextTransfer={requestTextTransfer} />);
    fireEvent.click(screen.getByRole("button", { name: "Send text" }));

    view.rerender(<TextTransferMode {...props} clearAfterSending draft="Draft A" onDraftChange={onDraftChange} requestTextTransfer={requestTextTransfer} result={{ type: "text.send.result", operationId: "op-a", succeeded: true, message: "Sent" }} />);

    expect(onDraftChange).toHaveBeenCalledExactlyOnceWith("");
  });

  it("preserves a newer edit when an older submission succeeds", () => {
    const onDraftChange = vi.fn();
    const requestTextTransfer = vi.fn(() => "op-a");
    const view = render(<TextTransferMode {...props} clearAfterSending draft="Draft A" onDraftChange={onDraftChange} requestTextTransfer={requestTextTransfer} />);
    fireEvent.click(screen.getByRole("button", { name: "Send text" }));

    view.rerender(<TextTransferMode {...props} clearAfterSending draft="Draft B" onDraftChange={onDraftChange} requestTextTransfer={requestTextTransfer} />);
    view.rerender(<TextTransferMode {...props} clearAfterSending draft="Draft B" onDraftChange={onDraftChange} requestTextTransfer={requestTextTransfer} result={{ type: "text.send.result", operationId: "op-a", succeeded: true, message: "Sent" }} />);

    expect(onDraftChange).not.toHaveBeenCalled();
  });

  it("preserves text that was edited away from and back to the submitted value", () => {
    const onDraftChange = vi.fn();
    const requestTextTransfer = vi.fn(() => "op-a");
    const view = render(<TextTransferMode {...props} clearAfterSending draft="Draft A" onDraftChange={onDraftChange} requestTextTransfer={requestTextTransfer} />);
    fireEvent.click(screen.getByRole("button", { name: "Send text" }));

    view.rerender(<TextTransferMode {...props} clearAfterSending draft="Draft B" onDraftChange={onDraftChange} requestTextTransfer={requestTextTransfer} />);
    view.rerender(<TextTransferMode {...props} clearAfterSending draft="Draft A" onDraftChange={onDraftChange} requestTextTransfer={requestTextTransfer} />);
    view.rerender(<TextTransferMode {...props} clearAfterSending draft="Draft A" onDraftChange={onDraftChange} requestTextTransfer={requestTextTransfer} result={{ type: "text.send.result", operationId: "op-a", succeeded: true, message: "Sent" }} />);

    expect(onDraftChange).not.toHaveBeenCalled();
  });

  it.each([
    [{ type: "text.send.result" as const, operationId: "op-a", succeeded: false, message: "Failed" }, true],
    [{ type: "text.send.result" as const, operationId: "op-a", succeeded: false, code: "VAIR-TEXT-RESPONSE-TIMEOUT", message: "Timed out" }, true],
    [{ type: "text.send.result" as const, operationId: "op-a", succeeded: true, message: "Sent" }, false]
  ])("preserves the draft for failure, timeout, or disabled clear", (result, clearAfterSending) => {
    const onDraftChange = vi.fn();
    const requestTextTransfer = vi.fn(() => "op-a");
    const view = render(<TextTransferMode {...props} clearAfterSending={clearAfterSending} draft="Draft A" onDraftChange={onDraftChange} requestTextTransfer={requestTextTransfer} />);
    fireEvent.click(screen.getByRole("button", { name: "Send text" }));
    view.rerender(<TextTransferMode {...props} clearAfterSending={clearAfterSending} draft="Draft A" onDraftChange={onDraftChange} requestTextTransfer={requestTextTransfer} result={result} />);
    expect(onDraftChange).not.toHaveBeenCalled();
  });

  it("ignores an old result after a newer send and clears a matching result once", () => {
    const onDraftChange = vi.fn();
    const requestTextTransfer = vi.fn().mockReturnValueOnce("op-a").mockReturnValueOnce("op-b");
    const view = render(<TextTransferMode {...props} clearAfterSending draft="Draft A" onDraftChange={onDraftChange} requestTextTransfer={requestTextTransfer} />);
    fireEvent.click(screen.getByRole("button", { name: "Send text" }));
    view.rerender(<TextTransferMode {...props} clearAfterSending draft="Draft B" onDraftChange={onDraftChange} requestTextTransfer={requestTextTransfer} />);
    fireEvent.click(screen.getByRole("button", { name: "Send text" }));

    view.rerender(<TextTransferMode {...props} clearAfterSending draft="Draft B" onDraftChange={onDraftChange} requestTextTransfer={requestTextTransfer} result={{ type: "text.send.result", operationId: "op-a", succeeded: true, message: "Old" }} />);
    expect(onDraftChange).not.toHaveBeenCalled();
    view.rerender(<TextTransferMode {...props} clearAfterSending draft="Draft B" onDraftChange={onDraftChange} requestTextTransfer={requestTextTransfer} result={{ type: "text.send.result", operationId: "op-b", succeeded: true, message: "Current" }} />);
    expect(onDraftChange).toHaveBeenCalledExactlyOnceWith("");
    view.rerender(<TextTransferMode {...props} clearAfterSending draft="Draft B" onDraftChange={onDraftChange} requestTextTransfer={requestTextTransfer} result={null} />);
    view.rerender(<TextTransferMode {...props} clearAfterSending draft="Draft B" onDraftChange={onDraftChange} requestTextTransfer={requestTextTransfer} result={{ type: "text.send.result", operationId: "op-b", succeeded: true, message: "Duplicate" }} />);
    expect(onDraftChange).toHaveBeenCalledTimes(1);
  });

  it("reads the device clipboard only on activation and inserts at the retained selection", async () => {
    render(<TextTransferHarness initialDraft="Hello world" />);
    const editor = screen.getByLabelText("Text to send") as HTMLTextAreaElement;

    expect(readTextFromDeviceClipboard).not.toHaveBeenCalled();
    editor.setSelectionRange(6, 11);
    fireEvent.click(screen.getByRole("button", { name: "Paste from this device's clipboard" }));

    await waitFor(() => expect(editor.value).toBe("Hello Phone"));
    await waitFor(() => {
      expect(editor.selectionStart).toBe(11);
      expect(editor.selectionEnd).toBe(11);
    });
  });

  it.each([
    [0, 0, "PhoneHello"],
    [5, 5, "HelloPhone"]
  ])("inserts phone text at selection %i-%i", async (start, end, expected) => {
    render(<TextTransferHarness initialDraft="Hello" />);
    const editor = screen.getByLabelText("Text to send") as HTMLTextAreaElement;
    editor.setSelectionRange(start, end);

    fireEvent.click(screen.getByRole("button", { name: "Paste from this device's clipboard" }));

    await waitFor(() => expect(editor.value).toBe(expected));
  });

  it("accepts exactly the text limit and rejects an oversized paste without changing the draft", async () => {
    vi.mocked(readTextFromDeviceClipboard)
      .mockResolvedValueOnce({ status: "success", text: "x".repeat(4096) })
      .mockResolvedValueOnce({ status: "success", text: "y" });
    render(<TextTransferHarness />);
    const editor = screen.getByLabelText("Text to send") as HTMLTextAreaElement;

    fireEvent.click(screen.getByRole("button", { name: "Paste from this device's clipboard" }));
    await waitFor(() => expect(editor.value.length).toBe(4096));
    editor.setSelectionRange(4096, 4096);
    fireEvent.click(screen.getByRole("button", { name: "Paste from this device's clipboard" }));

    expect((await screen.findByRole("alert")).textContent).toContain("character limit");
    expect(editor.value.length).toBe(4096);
  });

  it.each([
    [{ status: "empty" as const }, "has no text"],
    [{ status: "denied" as const }, "did not allow"],
    [{ status: "failed" as const }, "Could not read"]
  ])("preserves the draft when clipboard reading fails", async (result, message) => {
    vi.mocked(readTextFromDeviceClipboard).mockResolvedValue(result);
    render(<TextTransferHarness initialDraft="Keep me" />);

    fireEvent.click(screen.getByRole("button", { name: "Paste from this device's clipboard" }));

    expect((await screen.findByRole("alert")).textContent).toContain(message);
    expect(screen.getByLabelText("Text to send")).toHaveProperty("value", "Keep me");
  });

  it("does not apply a stale clipboard result after the draft changes", async () => {
    let resolveRead: ((value: { status: "success"; text: string }) => void) | undefined;
    vi.mocked(readTextFromDeviceClipboard).mockReturnValue(new Promise((resolve) => { resolveRead = resolve; }));
    render(<TextTransferHarness initialDraft="Original" />);
    const editor = screen.getByLabelText("Text to send") as HTMLTextAreaElement;

    fireEvent.click(screen.getByRole("button", { name: "Paste from this device's clipboard" }));
    fireEvent.change(editor, { target: { value: "Edited" } });
    resolveRead?.({ status: "success", text: "Phone" });

    expect((await screen.findByRole("alert")).textContent).toContain("draft changed");
    expect(editor.value).toBe("Edited");
  });

  it("omits the device clipboard action when secure reading is unsupported", () => {
    vi.mocked(canReadTextFromDeviceClipboard).mockReturnValue(false);
    render(<TextTransferHarness />);

    expect(screen.queryByRole("button", { name: "Paste from this device's clipboard" })).toBeNull();
  });

  it("bounds duplicate activation while a clipboard read is pending", () => {
    vi.mocked(readTextFromDeviceClipboard).mockReturnValue(new Promise(() => undefined));
    render(<TextTransferHarness />);
    const pasteButton = screen.getByRole("button", { name: "Paste from this device's clipboard" });

    fireEvent.click(pasteButton);
    fireEvent.click(pasteButton);

    expect(readTextFromDeviceClipboard).toHaveBeenCalledOnce();
    expect(screen.getByRole("button", { name: "Reading this device's clipboard…" })).toHaveProperty("disabled", true);
  });

  it("ignores a clipboard result after unmount", async () => {
    let resolveRead: ((value: { status: "success"; text: string }) => void) | undefined;
    vi.mocked(readTextFromDeviceClipboard).mockReturnValue(new Promise((resolve) => { resolveRead = resolve; }));
    const onDraftChange = vi.fn();
    const view = render(<TextTransferHarness initialDraft="Original" onDraftChange={onDraftChange} />);

    fireEvent.click(screen.getByRole("button", { name: "Paste from this device's clipboard" }));
    view.unmount();
    resolveRead?.({ status: "success", text: "Phone" });
    await Promise.resolve();

    expect(onDraftChange).not.toHaveBeenCalled();
  });

  it("applies clipboard text after the StrictMode effect cycle", async () => {
    render(<StrictMode><TextTransferHarness initialDraft="Hello " /></StrictMode>);

    fireEvent.click(screen.getByRole("button", { name: "Paste from this device's clipboard" }));

    await waitFor(() => expect(screen.getByLabelText("Text to send")).toHaveProperty("value", "PhoneHello "));
  });
});
