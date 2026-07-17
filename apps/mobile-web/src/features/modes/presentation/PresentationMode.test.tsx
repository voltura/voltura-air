import { act, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { PresentationMode } from "./PresentationMode";

const defaultProps = {
  capability: { canControl: true },
  connected: true,
  pending: null,
  result: null,
  onCommand: vi.fn()
} as const;

describe("PresentationMode", () => {
  afterEach(() => {
    vi.useRealTimers();
    Reflect.deleteProperty(navigator, "vibrate");
  });

  it("uses target-specific controls and hides shortcuts that are unsafe for the selected target", () => {
    const onCommand = vi.fn();
    render(<PresentationMode {...defaultProps} onCommand={onCommand} />);

    fireEvent.click(screen.getByRole("button", { name: "Next" }));
    expect(onCommand).toHaveBeenLastCalledWith("powerpoint", "next");
    expect(screen.getByRole("button", { name: "Start slideshow" })).toBeTruthy();

    fireEvent.click(screen.getByRole("button", { name: "Change presentation mode (PowerPoint)" }));
    fireEvent.click(screen.getByRole("menuitemradio", { name: "Google Slides" }));
    expect(screen.queryByRole("button", { name: "Start slideshow" })).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "Laser pointer" }));
    expect(onCommand).toHaveBeenLastCalledWith("google-slides", "pointer");

    fireEvent.click(screen.getByRole("button", { name: "Change presentation mode (Google Slides)" }));
    fireEvent.click(screen.getByRole("menuitemradio", { name: "PDF / browser" }));
    expect(screen.queryByRole("button", { name: "Blackout" })).toBeNull();
    expect(screen.queryByRole("button", { name: "Laser pointer" })).toBeNull();
    expect(screen.getByRole("button", { name: "End slideshow" })).toBeTruthy();
  });

  it("uses the Remote Power blackout action", () => {
    const onPowerAction = vi.fn();
    const onCommand = vi.fn();
    render(<PresentationMode {...defaultProps} onCommand={onCommand} onPowerAction={onPowerAction} />);

    fireEvent.click(screen.getByRole("button", { name: "Blackout" }));

    expect(onPowerAction).toHaveBeenCalledExactlyOnceWith("blackoutDisplay");
    expect(onCommand).not.toHaveBeenCalled();
  });

  it("collapses the active target into a header selector and restores the full target row on reselection", () => {
    render(<PresentationMode {...defaultProps} />);

    const powerpointSelector = screen.getByRole("button", { name: "Change presentation mode (PowerPoint)" });
    fireEvent.click(powerpointSelector);
    expect(screen.getByRole("menu", { name: "Change presentation mode" })).toBeTruthy();

    fireEvent.click(screen.getByRole("menuitemradio", { name: "Google Slides" }));
    expect(screen.queryByRole("button", { name: "Start slideshow" })).toBeNull();
    expect(screen.getByRole("button", { name: "Change presentation mode (Google Slides)" })).toBeTruthy();

    expect(screen.getByRole("button", { name: "Change presentation mode (Google Slides)" })).toBeTruthy();
  });

  it("blocks controls for denied permission and while one command is pending", () => {
    const view = render(<PresentationMode {...defaultProps} capability={{ canControl: false }} />);
    expect(screen.getByRole("alert").textContent).toContain("blocked by the host");
    expect(screen.getByRole<HTMLButtonElement>("button", { name: "Next" }).disabled).toBe(true);

    view.rerender(<PresentationMode
      {...defaultProps}
      pending={{ operationId: "operation-a", target: "powerpoint", action: "next" }}
    />);
    expect(screen.getByRole<HTMLButtonElement>("button", { name: "Previous" }).disabled).toBe(true);
  });

  it("starts, pauses, and resets the local elapsed timer", async () => {
    vi.useFakeTimers();
    render(<PresentationMode {...defaultProps} />);

    fireEvent.click(screen.getByRole("button", { name: "Start" }));
    expect(screen.queryByRole("button", { name: "Start" })).toBeNull();
    expect(screen.getByRole("button", { name: "Pause" })).toBeTruthy();
    await act(() => vi.advanceTimersByTime(61_000));
    expect(screen.getByLabelText("Elapsed presentation time").textContent).toBe("01:01");

    fireEvent.click(screen.getByRole("button", { name: "Pause" }));
    await act(() => vi.advanceTimersByTime(5_000));
    expect(screen.getByLabelText("Presentation time during break").textContent).toBe("01:01");
    expect(screen.getByLabelText("Elapsed break time").textContent).toBe("00:05");
    expect(screen.getByRole("button", { name: "Resume" })).toBeTruthy();

    fireEvent.click(screen.getByRole("button", { name: "Reset" }));
    expect(screen.getByLabelText("Elapsed presentation time").textContent).toBe("00:00");
    expect(screen.getByRole("button", { name: "Start" })).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Pause" })).toBeNull();
    expect(screen.getByRole<HTMLButtonElement>("button", { name: "Reset" }).disabled).toBe(true);
    expect(screen.queryByText("Timer ready.")).toBeNull();
  });

  it("uses feature-detected vibration with visible milestone alternatives", async () => {
    vi.useFakeTimers();
    const vibrate = vi.fn(() => true);
    Object.defineProperty(navigator, "vibrate", { configurable: true, value: vibrate });
    render(<PresentationMode {...defaultProps} />);

    fireEvent.change(screen.getByLabelText("Planned duration"), { target: { value: "10" } });
    fireEvent.click(screen.getByRole("checkbox", { name: /Vibrate at 5 minutes/ }));
    fireEvent.click(screen.getByRole("button", { name: "Start" }));
    await act(() => vi.advanceTimersByTime(5 * 60 * 1000));

    expect(screen.getByText("5 minutes remaining.")).toBeTruthy();
    expect(vibrate).toHaveBeenCalledWith([160, 100, 160]);

    await act(() => vi.advanceTimersByTime(5 * 60 * 1000));
    expect(screen.getByText("Planned time elapsed.")).toBeTruthy();
    expect(vibrate).toHaveBeenCalledWith([300, 150, 300]);
  });
});
