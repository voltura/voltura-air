import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { ScreenViewRecordingPanel } from "./ScreenViewRecordingPanel";

describe("ScreenViewRecordingPanel", () => {
  it.each([
    ["Video with sound", "", 72_900, "1:12"],
    ["PC sound interrupted", "PC sound interrupted", 72_900, "1:12"],
    ["Preparing recording…", "Preparing recording…", 0, "0:00"],
  ] as const)(
    "shows the recording status and elapsed time for %s",
    (label, message, elapsedMs, elapsed) => {
      render(
        <ScreenViewRecordingPanel
          presentation={{
            phase: "recording",
            fileName: "Voltura Air - Screen recording.mp4",
            message,
            elapsedMs,
            includesSound: true,
          }}
          onDiscard={vi.fn()}
          onSave={vi.fn()}
        />,
      );

      expect(screen.getByText(`${label} · ${elapsed} / 5:00`)).toBeTruthy();
      expect(screen.getByRole("progressbar").getAttribute("max")).toBe("300000");
    },
  );

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
