import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { useState } from "react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { canCopyTextToClipboard, copyTextToClipboard } from "../../../foundation/diagnostics/mobileDiagnostics";
import type { AppToastMessage } from "../../../ui/feedback/AppToast";
import { ClipboardReadMode } from "./ClipboardReadMode";

vi.mock("../../../foundation/diagnostics/mobileDiagnostics", () => ({
  canCopyTextToClipboard: vi.fn(),
  copyTextToClipboard: vi.fn()
}));

function ClipboardReadHarness({ onCopyFeedback = vi.fn() }: { onCopyFeedback?: (feedback: AppToastMessage) => void }) {
  const [text, setText] = useState("Fetched text");
  return <ClipboardReadMode clientId="client-a" permission pending={false} result={null} text={text} onCopyFeedback={onCopyFeedback} onGetText={vi.fn()} onLoadSnippet={vi.fn()} onTextChange={setText} />;
}

describe("ClipboardReadMode", () => {
  beforeEach(() => {
    vi.mocked(canCopyTextToClipboard).mockReturnValue(true);
    vi.mocked(copyTextToClipboard).mockResolvedValue("copied");
  });

  it("fetches only when the user presses the button and preserves manual-copy behavior", () => {
    const onGetText = vi.fn();
    render(<ClipboardReadMode clientId="client-a" permission pending={false} result={null} text="" onCopyFeedback={vi.fn()} onGetText={onGetText} onLoadSnippet={vi.fn()} onTextChange={vi.fn()} />);

    expect(onGetText).not.toHaveBeenCalled();
    expect(screen.getByLabelText("Text from PC")).toHaveProperty("readOnly", true);
    fireEvent.click(screen.getByRole("button", { name: "Get text from PC" }));
    expect(onGetText).toHaveBeenCalledOnce();
    expect(screen.getByRole("button", { name: "Show snippets" })).toHaveProperty("disabled", false);
  });

  it("explains when the host has blocked clipboard access", () => {
    render(<ClipboardReadMode clientId="client-a" permission={false} pending={false} result={null} text="Existing text" onCopyFeedback={vi.fn()} onGetText={vi.fn()} onLoadSnippet={vi.fn()} onTextChange={vi.fn()} />);

    expect(screen.getByRole("alert").textContent).toContain("blocked by the host");
    expect(screen.getByRole("button", { name: "Get text from PC" })).toHaveProperty("disabled", true);
    expect(screen.getByLabelText("Text from PC")).toHaveProperty("value", "Existing text");
    expect(screen.getByRole("button", { name: "Show snippets" })).toHaveProperty("disabled", false);
  });

  it("shows the existing snippets control when requested", () => {
    render(<ClipboardReadMode clientId="client-a" permission pending={false} result={null} text="Fetched text" onCopyFeedback={vi.fn()} onGetText={vi.fn()} onLoadSnippet={vi.fn()} onTextChange={vi.fn()} />);

    fireEvent.click(screen.getByRole("button", { name: "Show snippets" }));

    expect(screen.getByText("Saved snippets")).toBeTruthy();
    expect(screen.getByText("Saved snippets").closest("details")).toHaveProperty("open", true);
    expect(screen.getByRole("button", { name: "Hide snippets" })).toBeTruthy();
  });

  it("moves the guidance into the standard information dialog", () => {
    render(<ClipboardReadMode clientId="client-a" permission pending={false} result={null} text="" onCopyFeedback={vi.fn()} onGetText={vi.fn()} onLoadSnippet={vi.fn()} onTextChange={vi.fn()} />);

    expect(screen.queryByText(/Press the button to fetch/)).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "About Get text from PC" }));

    expect(screen.getByRole("dialog").textContent).toContain("Voltura Air writes to this device's clipboard only when you choose Copy.");
    expect(screen.getByRole("button", { name: "OK" })).toBeTruthy();
  });

  it("clears and selects all fetched text", () => {
    render(<ClipboardReadHarness />);
    const textArea = screen.getByLabelText("Text from PC") as HTMLTextAreaElement;

    fireEvent.click(screen.getByRole("button", { name: "Select All" }));
    expect(document.activeElement).toBe(textArea);
    expect(textArea.selectionStart).toBe(0);
    expect(textArea.selectionEnd).toBe(textArea.value.length);

    fireEvent.click(screen.getByRole("button", { name: "Clear All" }));
    expect(textArea.value).toBe("");
    expect(screen.getByRole("button", { name: "Clear All" })).toHaveProperty("disabled", true);
    expect(screen.getByRole("button", { name: "Select All" })).toHaveProperty("disabled", true);
  });

  it("cuts and copies only the selected text", async () => {
    const onCopyFeedback = vi.fn();
    render(<ClipboardReadHarness onCopyFeedback={onCopyFeedback} />);
    const textArea = screen.getByLabelText("Text from PC") as HTMLTextAreaElement;

    textArea.setSelectionRange(0, 7);
    fireEvent.select(textArea);
    fireEvent.click(screen.getByRole("button", { name: "Copy" }));

    await waitFor(() => {
      expect(copyTextToClipboard).toHaveBeenCalledWith("Fetched");
    });
    expect(onCopyFeedback).toHaveBeenCalledWith({ message: "Selected text copied.", tone: "success" });

    fireEvent.click(screen.getByRole("button", { name: "Cut" }));
    expect(textArea.value).toBe(" text");
  });

  it("does not show Copy when clipboard writing is unavailable", () => {
    vi.mocked(canCopyTextToClipboard).mockReturnValue(false);
    render(<ClipboardReadHarness />);

    expect(screen.queryByRole("button", { name: "Copy" })).toBeNull();
    expect(screen.getByRole("button", { name: "Cut" })).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "About Get text from PC" }));
    expect(screen.getByRole("dialog").textContent).toContain("Voltura Air does not write to this device's clipboard.");
  });

  it("reports a detected clipboard-path failure and keeps Copy available for retry", async () => {
    vi.mocked(copyTextToClipboard).mockResolvedValue("manual");
    const onCopyFeedback = vi.fn();
    render(<ClipboardReadHarness onCopyFeedback={onCopyFeedback} />);
    const textArea = screen.getByLabelText("Text from PC") as HTMLTextAreaElement;

    textArea.setSelectionRange(0, 7);
    fireEvent.select(textArea);
    fireEvent.click(screen.getByRole("button", { name: "Copy" }));

    await waitFor(() => {
      expect(onCopyFeedback).toHaveBeenCalledWith({
        message: "Could not copy automatically. Try Copy again or use your browser's copy action.",
        tone: "error"
      });
    });
    expect(screen.getByRole("button", { name: "Copy" })).toBeTruthy();
  });
});
