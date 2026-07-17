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
});
