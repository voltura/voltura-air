import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { TextTransferMode } from "./TextTransferMode";

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

describe("TextTransferMode", () => {
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
});
