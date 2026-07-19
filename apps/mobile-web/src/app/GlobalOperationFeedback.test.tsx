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
        tab="trackpad"
        textTransferResult={null}
        transientFeedback={null}
      />
    );

    expect(screen.getAllByRole("status")).toHaveLength(1);
    expect(screen.getByRole("status").textContent).toBe("Getting text from PC…");
  });
});
