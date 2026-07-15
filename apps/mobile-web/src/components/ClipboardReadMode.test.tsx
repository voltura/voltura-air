import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { ClipboardReadMode } from "./ClipboardReadMode";

describe("ClipboardReadMode", () => {
  it("fetches only when the user presses the button and preserves manual-copy behavior", () => {
    const onGetText = vi.fn();
    render(<ClipboardReadMode clientId="client-a" permission pending={false} result={null} text="" onGetText={onGetText} onLoadSnippet={vi.fn()} />);

    expect(onGetText).not.toHaveBeenCalled();
    expect(screen.getByLabelText("Text from PC")).toHaveProperty("readOnly", true);
    fireEvent.click(screen.getByRole("button", { name: "Get text from PC" }));
    expect(onGetText).toHaveBeenCalledOnce();
    expect(screen.getByRole("button", { name: "Show snippets" })).toHaveProperty("disabled", false);
  });

  it("explains when the host has blocked clipboard access", () => {
    render(<ClipboardReadMode clientId="client-a" permission={false} pending={false} result={null} text="Existing text" onGetText={vi.fn()} onLoadSnippet={vi.fn()} />);

    expect(screen.getByRole("alert").textContent).toContain("blocked by the host");
    expect(screen.getByRole("button", { name: "Get text from PC" })).toHaveProperty("disabled", true);
    expect(screen.getByLabelText("Text from PC")).toHaveProperty("value", "Existing text");
    expect(screen.getByRole("button", { name: "Show snippets" })).toHaveProperty("disabled", false);
  });

  it("shows the existing snippets control when requested", () => {
    render(<ClipboardReadMode clientId="client-a" permission pending={false} result={null} text="Fetched text" onGetText={vi.fn()} onLoadSnippet={vi.fn()} />);

    fireEvent.click(screen.getByRole("button", { name: "Show snippets" }));

    expect(screen.getByText("Saved snippets")).toBeTruthy();
    expect(screen.getByText("Saved snippets").closest("details")).toHaveProperty("open", true);
    expect(screen.getByRole("button", { name: "Hide snippets" })).toBeTruthy();
  });
});
