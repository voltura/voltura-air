import { act, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { uiDurations } from "../../../ui/tokens.g";
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

  it("uses verified PowerPoint state for expanded direct controls", () => {
    const onCommand = vi.fn();
    const onPowerPointRefresh = vi.fn();
    render(
      <PresentationMode
        {...defaultProps}
        activationRequestId={1}
        capability={{
          ...defaultProps.capability,
          powerPoint: {
            state: "ready",
            foregroundActivationSupported: true,
            presentations: [{
              runtimePresentationId: "presentation-1",
              name: "Quarterly update.pptx",
              state: "presenting",
              slideCount: 24,
              currentSlideIndex: 7,
              currentShowPosition: 7,
              slideShowState: "running"
            }],
            session: {
              state: "tracking",
              runtimePresentationId: "presentation-1",
              presentationName: "Quarterly update.pptx",
              ownerDeviceName: "Presenter phone",
              isOwner: true,
              startedAt: "2026-07-24T09:00:00.000+02:00",
              elapsedSeconds: 75,
              breakActive: false,
              breakElapsedSeconds: 0,
              currentSlideIndex: 7,
              slideCount: 24,
              slideShowState: "running"
            }
          }
        }}
        onCommand={onCommand}
        onPowerPointRefresh={onPowerPointRefresh}
      />
    );

    expect(screen.getByText("Slide 7 of 24 · Presenting")).toBeTruthy();
    expect(onCommand).toHaveBeenCalledWith(
      "powerpoint",
      "activate",
      { runtimePresentationId: "presentation-1" });
    fireEvent.click(screen.getByRole("button", { name: "Focus PPT" }));
    expect(onCommand).toHaveBeenLastCalledWith(
      "powerpoint",
      "activate",
      { runtimePresentationId: "presentation-1" });
    expect(screen.getByRole("button", { name: "Change" }))
      .toHaveProperty("disabled", false);
    expect(screen.getByText("01:15 · Slide 7 of 24")).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Timer" })).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "Next" }));
    expect(onCommand).toHaveBeenLastCalledWith(
      "powerpoint",
      "next",
      { runtimePresentationId: "presentation-1" });
    expect(screen.getByRole<HTMLButtonElement>(
      "button",
      { name: "Start from beginning" }).disabled).toBe(false);
    expect(screen.getByRole<HTMLButtonElement>(
      "button",
      { name: "Start from current" }).disabled).toBe(false);
    fireEvent.click(screen.getByRole("button", { name: "Start from beginning" }));
    expect(onCommand).toHaveBeenLastCalledWith(
      "powerpoint",
      "start",
      { runtimePresentationId: "presentation-1" });
    fireEvent.click(screen.getByRole("button", { name: "Start from current" }));
    expect(onCommand).toHaveBeenLastCalledWith(
      "powerpoint",
      "start-current",
      { runtimePresentationId: "presentation-1" });

    fireEvent.click(screen.getByRole("button", { name: "Go to slide" }));
    expect(screen.getByRole("dialog", { name: "Go to slide" }).closest(".presentation-mode"))
      .toBeNull();
    fireEvent.change(screen.getByRole("slider", { name: "Slide number" }), {
      target: { value: "12" }
    });
    fireEvent.pointerUp(screen.getByRole("slider", { name: "Slide number" }));
    expect(onCommand).toHaveBeenLastCalledWith(
      "powerpoint",
      "goto",
      { runtimePresentationId: "presentation-1", slideNumber: 12 });
    expect(screen.queryByRole("dialog", { name: "Go to slide" })).toBeNull();

    fireEvent.click(screen.getByRole("button", { name: "Go to slide" }));
    fireEvent.click(screen.getByRole("button", { name: "Close Go to slide" }));
    expect(screen.queryByRole("dialog", { name: "Go to slide" })).toBeNull();

    fireEvent.click(screen.getByRole("button", { name: "Change" }));
    fireEvent.click(screen.getByRole("button", { name: "Refresh" }));
    expect(onPowerPointRefresh).toHaveBeenCalledOnce();
  });

  it("foregrounds PowerPoint only for an explicit Presentation entry request", () => {
    const onCommand = vi.fn();
    const readyCapability = {
      ...defaultProps.capability,
      powerPoint: {
        state: "ready" as const,
        foregroundActivationSupported: true,
        presentations: [{
          runtimePresentationId: "presentation-1",
          name: "Quarterly update.pptx",
          state: "ready" as const,
          slideCount: 24,
          currentSlideIndex: null,
          currentShowPosition: null,
          slideShowState: "ready" as const
        }]
      }
    };
    const view = render(
      <PresentationMode
        {...defaultProps}
        capability={readyCapability}
        onCommand={onCommand}
      />
    );

    expect(onCommand).not.toHaveBeenCalled();

    view.rerender(
      <PresentationMode
        {...defaultProps}
        activationRequestId={1}
        capability={readyCapability}
        onCommand={onCommand}
      />
    );
    expect(onCommand).toHaveBeenCalledExactlyOnceWith(
      "powerpoint",
      "activate",
      { runtimePresentationId: "presentation-1" });

    view.rerender(
      <PresentationMode
        {...defaultProps}
        activationRequestId={1}
        capability={{
          ...readyCapability,
          powerPoint: {
            ...readyCapability.powerPoint,
            state: "busy"
          }
        }}
        onCommand={onCommand}
      />
    );
    view.rerender(
      <PresentationMode
        {...defaultProps}
        activationRequestId={1}
        capability={readyCapability}
        onCommand={onCommand}
      />
    );

    expect(onCommand).toHaveBeenCalledTimes(1);
  });

  it("enables PowerPoint controls only for the selected slideshow state", () => {
    const onCommand = vi.fn();
    render(
      <PresentationMode
        {...defaultProps}
        capability={{
          ...defaultProps.capability,
          powerPoint: {
            state: "ready",
            foregroundActivationSupported: true,
            presentations: [{
              runtimePresentationId: "presentation-1",
              name: "Quarterly update.pptx",
              state: "ready",
              slideCount: 24,
              currentSlideIndex: 3,
              currentShowPosition: null,
              slideShowState: "ready"
            }],
            session: {
              state: "pending-review",
              runtimePresentationId: "presentation-1",
              presentationName: "Quarterly update.pptx",
              ownerDeviceName: "Presenter phone",
              isOwner: true,
              startedAt: "2026-07-24T09:00:00.000+02:00",
              elapsedSeconds: 75,
              breakActive: false,
              breakElapsedSeconds: 0,
              currentSlideIndex: 3,
              slideCount: 24,
              slideShowState: "ready"
            }
          }
        }}
        onCommand={onCommand}
        onSessionCommand={vi.fn()}
      />
    );

    expect(screen.getByText("Session paused")).toBeTruthy();
    expect(screen.getByText("24 slides · Ready")).toBeTruthy();
    for (const name of [
      "Pause auto-play",
      "End slideshow",
      "Laser pointer"
    ]) {
      expect(screen.getByRole<HTMLButtonElement>("button", { name }).disabled).toBe(true);
    }
    for (const name of [
      "Start from beginning",
      "Start from current",
      "Previous",
      "Next",
      "Go to slide",
      "Black screen",
      "White screen",
      "Continue presentation"
    ]) {
      expect(screen.getByRole<HTMLButtonElement>("button", { name }).disabled).toBe(false);
    }

    fireEvent.click(screen.getByRole("button", { name: "Black screen" }));
    expect(onCommand).toHaveBeenLastCalledWith(
      "powerpoint",
      "black",
      { runtimePresentationId: "presentation-1" });
    fireEvent.click(screen.getByRole("button", { name: "White screen" }));
    expect(onCommand).toHaveBeenLastCalledWith(
      "powerpoint",
      "white",
      { runtimePresentationId: "presentation-1" });
    fireEvent.click(screen.getByRole("button", { name: "Start from beginning" }));
    expect(onCommand).toHaveBeenLastCalledWith(
      "powerpoint",
      "start",
      { runtimePresentationId: "presentation-1" });
    fireEvent.click(screen.getByRole("button", { name: "Start from current" }));
    expect(onCommand).toHaveBeenLastCalledWith(
      "powerpoint",
      "start-current",
      { runtimePresentationId: "presentation-1" });
    fireEvent.click(screen.getByRole("button", { name: "Continue presentation" }));
    expect(onCommand).toHaveBeenLastCalledWith(
      "powerpoint",
      "start-current",
      { runtimePresentationId: "presentation-1" });
    fireEvent.click(screen.getByRole("button", { name: "Go to slide" }));
    fireEvent.click(screen.getByRole("button", { name: "First" }));
    expect(onCommand).toHaveBeenLastCalledWith(
      "powerpoint",
      "goto",
      { runtimePresentationId: "presentation-1", slideNumber: 1 });
    fireEvent.click(screen.getByRole("button", { name: "Go to slide" }));
    fireEvent.click(screen.getByRole("button", { name: "Last" }));
    expect(onCommand).toHaveBeenLastCalledWith(
      "powerpoint",
      "goto",
      { runtimePresentationId: "presentation-1", slideNumber: 24 });

    expect(screen.getByRole<HTMLButtonElement>("button", { name: "Volume down" }).disabled).toBe(false);
    expect(screen.getByRole<HTMLButtonElement>("button", { name: "Save" }).disabled).toBe(false);
    expect(screen.getByRole<HTMLButtonElement>("button", { name: "Discard" }).disabled).toBe(false);
  });

  it("lets only the authoritative session owner manage breaks", () => {
    const onSessionCommand = vi.fn();
    const session = {
      state: "tracking" as const,
      runtimePresentationId: "presentation-1",
      presentationName: "Quarterly update.pptx",
      ownerDeviceName: "Presenter phone",
      isOwner: true,
      startedAt: "2026-07-24T09:00:00.000+02:00",
      elapsedSeconds: 75,
      breakActive: false,
      breakElapsedSeconds: 0,
      currentSlideIndex: 7,
      slideCount: 24,
      slideShowState: "running" as const
    };
    const capability = {
      ...defaultProps.capability,
      powerPoint: {
        state: "ready" as const,
        presentations: [],
        session
      }
    };
    const view = render(
      <PresentationMode
        {...defaultProps}
        capability={capability}
        onSessionCommand={onSessionCommand}
      />
    );

    fireEvent.click(screen.getByRole("button", { name: "Start break" }));
    expect(onSessionCommand).toHaveBeenLastCalledWith("break", { enabled: true });

    view.rerender(
      <PresentationMode
        {...defaultProps}
        capability={{
          ...capability,
          powerPoint: {
            ...capability.powerPoint,
            session: { ...session, breakActive: true, breakElapsedSeconds: 5 }
          }
        }}
        onSessionCommand={onSessionCommand}
      />
    );
    fireEvent.click(screen.getByRole("button", { name: "Resume presentation" }));
    expect(onSessionCommand).toHaveBeenLastCalledWith("break", { enabled: false });

    view.rerender(
      <PresentationMode
        {...defaultProps}
        capability={capability}
        connected={false}
        onSessionCommand={onSessionCommand}
      />
    );
    expect(screen.getByRole<HTMLButtonElement>("button", { name: "Start break" }).disabled).toBe(true);

    view.rerender(
      <PresentationMode
        {...defaultProps}
        capability={capability}
        sessionPending
        onSessionCommand={onSessionCommand}
      />
    );
    expect(screen.getByRole<HTMLButtonElement>("button", { name: "Start break" }).disabled).toBe(true);

    view.rerender(
      <PresentationMode
        {...defaultProps}
        capability={{
          ...capability,
          powerPoint: {
            ...capability.powerPoint,
            session: { ...session, isOwner: false }
          }
        }}
        onSessionCommand={onSessionCommand}
      />
    );
    expect(screen.queryByRole("button", { name: "Start break" })).toBeNull();
    expect(screen.queryByRole("button", { name: "Save" })).toBeNull();
    expect(screen.queryByRole("button", { name: "Discard" })).toBeNull();
    expect(screen.getByText("Tracking")).toBeTruthy();
  });

  it("keeps the local timer and explains unavailable PowerPoint automation", () => {
    const onPowerPointRefresh = vi.fn();
    render(
      <PresentationMode
        {...defaultProps}
        capability={{
          ...defaultProps.capability,
          powerPoint: {
            state: "unavailable",
            presentations: [],
            session: {
              state: "inactive",
              runtimePresentationId: null,
              presentationName: null,
              ownerDeviceName: null,
              isOwner: false,
              startedAt: null,
              elapsedSeconds: 0,
              breakActive: false,
              breakElapsedSeconds: 0,
              currentSlideIndex: null,
              slideCount: 0,
              slideShowState: "ready"
            }
          }
        }}
        onPowerPointRefresh={onPowerPointRefresh}
      />
    );

    expect(screen.getByText("PowerPoint unavailable")).toBeTruthy();
    expect(screen.getByRole("button", { name: "Timer" })).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Choose" }));
    expect(screen.getByText(/PowerPoint is not running/)).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Refresh" }));
    expect(onPowerPointRefresh).toHaveBeenCalledOnce();
  });

  it("opens and presents a saved host file from the chooser", () => {
    const onPowerPointLaunch = vi.fn();
    render(
      <PresentationMode
        {...defaultProps}
        capability={{
          ...defaultProps.capability,
          powerPoint: {
            state: "unavailable",
            presentations: [],
            availablePresentations: [{
              presentationId: "report-1",
              title: "Quarterly update",
              fileName: "quarterly-update.pptx"
            }]
          }
        }}
        onPowerPointLaunch={onPowerPointLaunch}
      />
    );

    fireEvent.click(screen.getByRole("button", { name: "Choose" }));
    fireEvent.click(screen.getByRole("radio", { name: /Quarterly update/ }));
    fireEvent.click(screen.getByRole("button", { name: "Open and present" }));
    expect(onPowerPointLaunch).toHaveBeenCalledExactlyOnceWith("report-1");
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

  it("keeps emergency laser-off available while its enable command is pending", () => {
    const onCommand = vi.fn();
    render(
      <PresentationMode
        {...defaultProps}
        pending={{
          operationId: "pointer-enable",
          target: "powerpoint",
          action: "pointer",
          enabled: true
        }}
        capability={{
          ...defaultProps.capability,
          laserPointerActive: true
        }}
        onCommand={onCommand}
      />
    );

    const laser = screen.getByRole<HTMLButtonElement>(
      "button",
      { name: "Laser pointer" });
    expect(laser.disabled).toBe(false);
    fireEvent.click(laser);
    expect(onCommand).toHaveBeenLastCalledWith(
      "powerpoint",
      "pointer",
      { enabled: false });
    expect(screen.getByRole<HTMLButtonElement>(
      "button",
      { name: "Next" }).disabled).toBe(true);
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

  it("switches to a compact presentation summary while the trackpad owns the layout", () => {
    render(
      <PresentationMode
        {...defaultProps}
        renderTrackpad={() => <div>Trackpad canvas</div>}
      />
    );

    fireEvent.click(screen.getByRole("button", { name: "Trackpad" }));

    expect(screen.getByText("Trackpad canvas")).toBeTruthy();
    expect(screen.getByLabelText("Current presentation")).toBeTruthy();
    expect(screen.getByRole("button", { name: "Previous slide" }).textContent)
      .toContain("Previous");
    expect(screen.getByRole("button", { name: "Next slide" }).textContent)
      .toContain("Next");
    expect(screen.getByRole("region", { name: "Presentation" }).className)
      .toContain("trackpad-open");
  });

  it("folds the presentation trackpad after restoring it from fullscreen", () => {
    render(
      <PresentationMode
        {...defaultProps}
        renderTrackpad={({ isFullscreen, onToggleFullscreen }) => (
          <div>
            <span>Trackpad canvas</span>
            <button type="button" onClick={onToggleFullscreen}>
              {isFullscreen ? "Restore test trackpad" : "Expand test trackpad"}
            </button>
          </div>
        )}
      />
    );

    fireEvent.click(screen.getByRole("button", { name: "Trackpad" }));
    fireEvent.click(screen.getByRole("button", { name: "Expand test trackpad" }));
    expect(screen.getByRole("region", { name: "Presentation" }).className)
      .toContain("trackpad-fullscreen");

    fireEvent.click(screen.getByRole("button", { name: "Restore test trackpad" }));
    expect(screen.queryByText("Trackpad canvas")).toBeNull();
    expect(screen.getByRole("button", { name: "Trackpad" }).getAttribute("aria-expanded"))
      .toBe("false");
    expect(screen.getByRole("region", { name: "Presentation" }).className)
      .not.toContain("trackpad-open");
  });

  it("logically locks presentation commands before showing a slow pending state", async () => {
    vi.useFakeTimers();
    const view = render(<PresentationMode {...defaultProps} capability={{ ...defaultProps.capability, canControl: false }} />);
    expect(screen.getByRole("alert").textContent).toContain("blocked by the host");
    expect(screen.getByRole<HTMLButtonElement>("button", { name: "Next" }).disabled).toBe(true);

    view.rerender(<PresentationMode
      {...defaultProps}
      pending={{ operationId: "operation-a", target: "powerpoint", action: "next" }}
    />);
    const previous = screen.getByRole<HTMLButtonElement>("button", { name: "Previous" });
    const next = screen.getByRole<HTMLButtonElement>("button", { name: "Next" });
    const start = screen.getByRole<HTMLButtonElement>("button", { name: "Start slideshow" });
    expect(previous.disabled).toBe(true);
    expect(next.disabled).toBe(true);
    expect(start.disabled).toBe(true);
    expect(previous.dataset.pendingVisual).toBe("deferred");
    expect(next.dataset.pendingVisual).toBe("deferred");
    expect(start.dataset.pendingVisual).toBe("deferred");
    expect(screen.getByRole<HTMLButtonElement>("button", { name: "Volume down" }).disabled).toBe(false);
    expect(previous.closest("[aria-busy='true']")).toBeTruthy();

    await act(() => vi.advanceTimersByTime(uiDurations.slow));
    expect(previous.dataset.pendingVisual).toBeUndefined();
    expect(next.dataset.pendingVisual).toBeUndefined();
    expect(start.dataset.pendingVisual).toBeUndefined();
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
