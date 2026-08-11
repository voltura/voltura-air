import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { ErrorDialog } from "./ErrorDialog";

describe("ErrorDialog", () => {
  it("shows the complete error and diagnostic code with both dismissal controls", () => {
    const onClose = vi.fn();
    render(
      <ErrorDialog
        code="VAIR-PAIR-HOST-PROOF-INVALID"
        isOpen
        message="PC identity check failed. Scan a fresh QR code from the PC."
        onClose={onClose}
        title="Connection issue"
      />
    );

    const dialog = screen.getByRole("dialog", { name: "Connection issue" });
    expect(dialog.textContent).toContain("PC identity check failed. Scan a fresh QR code from the PC.");
    expect(dialog.textContent).toContain("Diagnostic code: VAIR-PAIR-HOST-PROOF-INVALID");
    expect(dialog.querySelector(".info-dialog-error-icon")).not.toBeNull();
    expect(screen.getByRole("button", { name: "Close Connection issue" })).not.toBeNull();

    fireEvent.click(screen.getByRole("button", { name: "OK" }));
    expect(onClose).toHaveBeenCalledTimes(1);
  });
});
