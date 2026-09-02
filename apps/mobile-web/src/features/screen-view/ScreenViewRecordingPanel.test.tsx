import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { ScreenViewRecordingPanel } from "./ScreenViewRecordingPanel";

describe("ScreenViewRecordingPanel", () => {
  it("shows the bounded elapsed time and whether sound is included", () => {
    render(
      <ScreenViewRecordingPanel
        presentation={{
          phase: "recording",
          fileName: "Voltura Air - Screen recording.mp4",
          message: "Recording…",
          elapsedMs: 72_900,
          includesSound: true,
        }}
        onDiscard={vi.fn()}
        onSave={vi.fn()}
      />,
    );

    expect(screen.getByText("Video with sound · 1:12 / 5:00")).toBeTruthy();
    expect(screen.getByRole("progressbar").getAttribute("max")).toBe("300000");
  });

  it("offers only Save/Share and Discard after finalization", () => {
    const onDiscard = vi.fn();
    const onSave = vi.fn();
    render(
      <ScreenViewRecordingPanel
        presentation={{
          phase: "ready",
          fileName: "Voltura Air - Screen recording.webm",
          message: "Recording ready to save or share.",
          elapsedMs: 15_000,
          includesSound: false,
        }}
        onDiscard={onDiscard}
        onSave={onSave}
      />,
    );

    fireEvent.click(screen.getByRole("button", { name: "Save / Share" }));
    fireEvent.click(screen.getByRole("button", { name: "Discard recording" }));
    expect(onSave).toHaveBeenCalledOnce();
    expect(onDiscard).toHaveBeenCalledOnce();
    expect(screen.queryByRole("progressbar")).toBeNull();
  });
});
