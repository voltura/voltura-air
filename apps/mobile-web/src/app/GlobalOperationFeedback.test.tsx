import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { GlobalOperationFeedback } from "./GlobalOperationFeedback";

describe("GlobalOperationFeedback", () => {
  it("renders only one toast and gives direct interaction feedback priority", () => {
    render(
      <GlobalOperationFeedback
        appLaunchResult={null}
        clipboardReadResult={null}
        pendingAppLaunchId="preset.browser"
        pendingClipboardRead
        pendingTextTransfer
        powerPointRefreshResult={null}
        presentationResult={null}
        presentationSessionResult={null}
        tab="trackpad"
        textTransferResult={null}
        transientFeedback={{ message: "Selected text copied.", tone: "success" }}
      />
    );

    expect(screen.getAllByRole("status")).toHaveLength(1);
    expect(screen.getByRole("status").textContent).toBe("Selected text copied.");
    expect(screen.queryByText("Getting text from PC…")).toBeNull();
  });

  it("prioritizes clipboard progress when no interaction feedback is active", () => {
    render(
      <GlobalOperationFeedback
        appLaunchResult={null}
        clipboardReadResult={null}
        pendingAppLaunchId="preset.browser"
        pendingClipboardRead
        pendingTextTransfer
        powerPointRefreshResult={null}
        presentationResult={null}
        presentationSessionResult={null}
        tab="trackpad"
        textTransferResult={null}
        transientFeedback={null}
      />
    );

    expect(screen.getAllByRole("status")).toHaveLength(1);
    expect(screen.getByRole("status").textContent).toBe("Getting text from PC…");
  });

  it("shows presentation failures as the shared error toast", () => {
    render(
      <GlobalOperationFeedback
        appLaunchResult={null}
        clipboardReadResult={null}
        pendingAppLaunchId={null}
        pendingClipboardRead={false}
        pendingTextTransfer={false}
        powerPointRefreshResult={null}
        presentationResult={{
          type: "presentation.command.result",
          operationId: "activate-1",
          target: "powerpoint",
          action: "activate",
          succeeded: false,
          message: "PowerPoint is open, but Voltura Air could not bring its window forward.",
          laserPointerActive: false
        }}
        presentationSessionResult={null}
        tab="presentation"
        textTransferResult={null}
        transientFeedback={null}
      />
    );

    expect(screen.getByRole("alert").textContent).toBe(
      "Bring PowerPoint forward failed. PowerPoint is open, but Voltura Air could not bring its window forward.");
  });

  it("shows session and refresh failures through the shared error toast", () => {
    const { rerender } = render(
      <GlobalOperationFeedback
        appLaunchResult={null}
        clipboardReadResult={null}
        pendingAppLaunchId={null}
        pendingClipboardRead={false}
        pendingTextTransfer={false}
        powerPointRefreshResult={null}
        presentationResult={null}
        presentationSessionResult={{
          type: "presentation.session.result",
          operationId: "break-1",
          action: "break",
          succeeded: false,
          message: "The PC did not confirm the session change."
        }}
        tab="presentation"
        textTransferResult={null}
        transientFeedback={null}
      />
    );

    expect(screen.getByRole("alert").textContent).toBe(
      "Change break failed. The PC did not confirm the session change.");

    rerender(
      <GlobalOperationFeedback
        appLaunchResult={null}
        clipboardReadResult={null}
        pendingAppLaunchId={null}
        pendingClipboardRead={false}
        pendingTextTransfer={false}
        powerPointRefreshResult={{
          type: "presentation.powerpoint.refresh.result",
          operationId: "refresh-1",
          succeeded: false,
          message: "The PC did not confirm the refresh.",
          state: "unavailable",
          presentations: []
        }}
        presentationResult={null}
        presentationSessionResult={null}
        tab="presentation"
        textTransferResult={null}
        transientFeedback={null}
      />
    );

    expect(screen.getByRole("alert").textContent).toBe(
      "Refresh PowerPoint failed. The PC did not confirm the refresh.");
  });
});
