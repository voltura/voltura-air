import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { copyTextToClipboard } from "../../mobileDiagnostics";
import type * as PairingFeedbackModule from "../../pairingFeedback";
import { PairingStatus } from "./PairingStatus";

vi.mock("../../mobileDiagnostics", () => ({
  copyTextToClipboard: vi.fn()
}));

vi.mock("../../pairingFeedback", async (importOriginal) => {
  const actual = await importOriginal<typeof PairingFeedbackModule>();
  return {
    ...actual,
    buildPairingDiagnostics: vi.fn(() => "redacted diagnostics")
  };
});

describe("PairingStatus", () => {
  it("keeps keyboard focus inside blocking connection feedback", () => {
    render(
      <PairingStatus
        activePcUnavailable
        message="PC is not available"
        onPrimaryAction={vi.fn()}
      />
    );

    const heading = screen.getByRole("heading", { name: "PC not available" });
    expect(heading).toBe(document.activeElement);

    fireEvent.keyDown(heading, { key: "Tab" });
    expect(screen.getByRole("button", { name: "Try reconnect" })).toBe(document.activeElement);
  });

  it("shows copied diagnostics as a toast when the selected PC is unavailable", async () => {
    vi.mocked(copyTextToClipboard).mockResolvedValueOnce("copied");

    render(
      <PairingStatus
        activePcUnavailable
        message="PC is not available"
        onPrimaryAction={vi.fn()}
      />
    );

    fireEvent.click(screen.getByRole("button", { name: "Copy diagnostics" }));

    await waitFor(() => {
      expect(document.querySelector(".app-toast.success")?.textContent).toBe("Diagnostics copied.");
    });
    expect(screen.queryByText("Could not copy automatically. Select the diagnostics below and copy manually.")).toBeNull();
  });
});
