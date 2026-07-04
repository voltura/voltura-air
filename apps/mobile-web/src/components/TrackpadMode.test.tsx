import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { defaultTrackpadSettings } from "../gestures";
import { TrackpadMode } from "./TrackpadMode";

const baseProps = {
  audioState: { type: "audio.state" as const, volume: 45, muted: false },
  isExpanded: false,
  supportsVolumeControl: true,
  trackpadSettings: defaultTrackpadSettings,
  onLeftClick: vi.fn(),
  onRightClick: vi.fn(),
  onSetVolume: vi.fn(),
  onToggleExpanded: vi.fn(),
  onToggleMute: vi.fn(),
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
