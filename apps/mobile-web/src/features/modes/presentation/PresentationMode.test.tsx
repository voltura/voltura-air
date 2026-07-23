import { act, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { PresentationMode } from "./PresentationMode";

const defaultProps = {
  audioState: null,
  blackoutAvailable: true,
  capability: { canControl: true, canSaveReports: true, laserPointerActive: false },
  connected: true,
  pending: null,
  pendingPowerAction: null,
  result: null,
  onCommand: vi.fn(),
  onMute: vi.fn(),
  onVolumeDown: vi.fn(),
  onVolumeUp: vi.fn(),
  renderTrackpad: () => null
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
    expect(onCommand).toHaveBeenLastCalledWith("google-slides", "pointer", true);

    fireEvent.click(screen.getByRole("button", { name: "Change presentation mode (Google Slides)" }));
    fireEvent.click(screen.getByRole("menuitemradio", { name: "PDF / browser" }));
    expect(screen.queryByRole("button", { name: "Blackout" })).toBeNull();
    expect(screen.getByRole("button", { name: "Laser pointer" })).toBeTruthy();
    expect(screen.getByRole("button", { name: "End slideshow" })).toBeTruthy();
  });

  it("reflects host laser state and disables an owned laser when Presentation unmounts", () => {
    const onCommand = vi.fn();
    const view = render(
      <PresentationMode
        {...defaultProps}
        capability={{ ...defaultProps.capability, laserPointerActive: true }}
        onCommand={onCommand}
      />
    );

    expect(screen.getByRole("button", { name: "Laser pointer" }).getAttribute("aria-pressed")).toBe("true");

    view.unmount();

    expect(onCommand).toHaveBeenLastCalledWith("powerpoint", "pointer", false);
  });

  it("uses the Remote Power blackout action", () => {
    const onPowerAction = vi.fn();
    const onCommand = vi.fn();
    render(<PresentationMode {...defaultProps} onCommand={onCommand} onPowerAction={onPowerAction} />);

    fireEvent.click(screen.getByRole("button", { name: "Blackout" }));

    expect(onPowerAction).toHaveBeenCalledExactlyOnceWith("blackoutDisplay");
    expect(onCommand).not.toHaveBeenCalled();
  });

  it("blocks Blackout with an accessible reason when the host denies it", () => {
    const onPowerAction = vi.fn();
    render(<PresentationMode {...defaultProps} blackoutAvailable={false} onPowerAction={onPowerAction} />);

    const blackout = screen.getByRole<HTMLButtonElement>("button", { name: "Blackout" });
    expect(blackout.disabled).toBe(true);
    expect(screen.getByRole("alert").textContent).toContain("Blackout is disabled by the host");
    fireEvent.click(blackout);
    expect(onPowerAction).not.toHaveBeenCalled();
  });

  it("blocks Blackout for presentation denial or any pending power action", () => {
    const onPowerAction = vi.fn();
    const view = render(<PresentationMode {...defaultProps} capability={{ ...defaultProps.capability, canControl: false }} onPowerAction={onPowerAction} />);
    expect(screen.getByRole<HTMLButtonElement>("button", { name: "Blackout" }).disabled).toBe(true);

    view.rerender(<PresentationMode {...defaultProps} pendingPowerAction="lock" onPowerAction={onPowerAction} />);
    const blackout = screen.getByRole<HTMLButtonElement>("button", { name: "Blackout" });
    expect(blackout.disabled).toBe(true);
    fireEvent.click(blackout);
    expect(onPowerAction).not.toHaveBeenCalled();
  });

  it("reacts to Blackout capability changes while mounted", () => {
    const onPowerAction = vi.fn();
    const view = render(<PresentationMode {...defaultProps} blackoutAvailable={false} onPowerAction={onPowerAction} />);
    expect(screen.getByRole<HTMLButtonElement>("button", { name: "Blackout" }).disabled).toBe(true);

    view.rerender(<PresentationMode {...defaultProps} blackoutAvailable onPowerAction={onPowerAction} />);
    const blackout = screen.getByRole<HTMLButtonElement>("button", { name: "Blackout" });
    expect(blackout.disabled).toBe(false);
    fireEvent.click(blackout);
    expect(onPowerAction).toHaveBeenCalledExactlyOnceWith("blackoutDisplay");
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
    const view = render(<PresentationMode {...defaultProps} capability={{ ...defaultProps.capability, canControl: false }} />);
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
    expect(screen.getByLabelText("Elapsed break time").textContent).toBe("00:05");
    expect(screen.getByRole("button", { name: /Presentation session 1: 01:01, followed by break 1: 00:05/ })).toBeTruthy();
    expect(screen.getByRole("button", { name: "Resume" })).toBeTruthy();

    fireEvent.click(screen.getByRole("button", { name: "Reset" }));
    fireEvent.click(screen.getByRole("button", { name: "Reset without saving" }));
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
