import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { defaultTrackpadSettings } from "../../../gestures";
import { TrackpadMode } from "./TrackpadMode";

const baseProps = {
  audioState: { type: "audio.state" as const, volume: 45, muted: false },
  isExpanded: false,
  supportsVolumeControl: true,
  trackpadSettings: defaultTrackpadSettings,
  onMouseButtonDown: vi.fn(),
  onMouseButtonUp: vi.fn(),
  onSetVolume: vi.fn(),
  onToggleExpanded: vi.fn(),
  onToggleMute: vi.fn(),
  onTouchCancel: vi.fn(),
  onTouchEnd: vi.fn(),
  onTouchMove: vi.fn(),
  onTouchStart: vi.fn()
};

describe("TrackpadMode volume control", () => {
  it("renders in normal mode when enabled and audio state exists", () => {
    render(<TrackpadMode {...baseProps} />);

    expect(screen.getByRole("button", { name: "Mute PC" })).toBeTruthy();
    expect(screen.getByRole("slider", { name: "PC volume" })).toHaveProperty("value", "45");
  });

  it("does not render when disabled", () => {
    render(<TrackpadMode {...baseProps} trackpadSettings={{ ...defaultTrackpadSettings, showVolumeControl: false }} />);

    expect(screen.queryByRole("slider", { name: "PC volume" })).toBeNull();
  });

  it("does not render when the host does not allow volume control", () => {
    render(<TrackpadMode {...baseProps} supportsVolumeControl={false} />);

    expect(screen.queryByRole("slider", { name: "PC volume" })).toBeNull();
  });

  it("does not render in expanded mode", () => {
    render(<TrackpadMode {...baseProps} isExpanded />);

    expect(screen.queryByRole("slider", { name: "PC volume" })).toBeNull();
  });

  it("sends toggle and set-volume actions", () => {
    const onToggleMute = vi.fn();
    const onSetVolume = vi.fn();
    render(<TrackpadMode {...baseProps} onSetVolume={onSetVolume} onToggleMute={onToggleMute} />);

    fireEvent.click(screen.getByRole("button", { name: "Mute PC" }));
    fireEvent.change(screen.getByRole("slider", { name: "PC volume" }), { target: { value: "77" } });

    expect(onToggleMute).toHaveBeenCalledOnce();
    expect(onSetVolume).toHaveBeenCalledWith(77);
  });
});

describe("TrackpadMode click buttons", () => {
  it("renders left then right by default", () => {
    render(<TrackpadMode {...baseProps} />);

    const buttons = screen.getAllByRole("button", { name: /left|right/i });

    expect(buttons.map((button) => button.textContent)).toEqual(["Left", "Right"]);
  });

  it("renders right then left for left-handed layout", () => {
    render(<TrackpadMode {...baseProps} trackpadSettings={{ ...defaultTrackpadSettings, leftHandedButtons: true }} />);

    const buttons = screen.getAllByRole("button", { name: /left|right/i });

    expect(buttons.map((button) => button.textContent)).toEqual(["Right", "Left"]);
  });

  it("marks large click button layout", () => {
    render(<TrackpadMode {...baseProps} trackpadSettings={{ ...defaultTrackpadSettings, largeClickButtons: true }} />);

    expect(document.querySelector(".trackpad-mode")?.classList.contains("large-click-buttons")).toBe(true);
  });

  it("sends button down and up so buttons can be held while moving", () => {
    const onMouseButtonDown = vi.fn();
    const onMouseButtonUp = vi.fn();
    render(<TrackpadMode {...baseProps} onMouseButtonDown={onMouseButtonDown} onMouseButtonUp={onMouseButtonUp} />);

    const leftButton = screen.getByRole("button", { name: "Left" });
    fireEvent.pointerDown(leftButton, { pointerId: 7 });
    fireEvent.pointerUp(leftButton, { pointerId: 7 });

    expect(onMouseButtonDown).toHaveBeenCalledWith("left");
    expect(onMouseButtonUp).toHaveBeenCalledWith("left");
  });

  it("still sends button events when pointer capture fails", () => {
    const onMouseButtonDown = vi.fn();
    const onMouseButtonUp = vi.fn();
    render(<TrackpadMode {...baseProps} onMouseButtonDown={onMouseButtonDown} onMouseButtonUp={onMouseButtonUp} />);

    const leftButton = screen.getByRole("button", { name: "Left" });
    leftButton.setPointerCapture = vi.fn(() => {
      throw new DOMException("Pointer capture is unavailable", "InvalidStateError");
    });
    leftButton.hasPointerCapture = vi.fn(() => true);
    leftButton.releasePointerCapture = vi.fn(() => {
      throw new DOMException("Pointer capture was already released", "NotFoundError");
    });

    fireEvent.pointerDown(leftButton, { pointerId: 8 });
    fireEvent.pointerUp(leftButton, { pointerId: 8 });

    expect(onMouseButtonDown).toHaveBeenCalledWith("left");
    expect(onMouseButtonUp).toHaveBeenCalledWith("left");
  });
});
