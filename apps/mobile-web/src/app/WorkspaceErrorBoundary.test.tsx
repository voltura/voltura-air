import { fireEvent, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { WorkspaceErrorBoundary } from "./WorkspaceErrorBoundary";

function BrokenWorkspace(): never {
  throw new Error("Workspace failed");
}

describe("WorkspaceErrorBoundary", () => {
  beforeEach(() => {
    vi.spyOn(console, "error").mockImplementation(() => undefined);
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("replaces a failed workspace with an actionable visible state", () => {
    const onBack = vi.fn();
    render(
      <WorkspaceErrorBoundary featureName="Screen" onBack={onBack}>
        <BrokenWorkspace />
      </WorkspaceErrorBoundary>,
    );

    expect(screen.getByRole("alert").textContent).toContain("Screen could not open");
    fireEvent.click(screen.getByRole("button", { name: "Back" }));
    expect(onBack).toHaveBeenCalledOnce();
  });
});
