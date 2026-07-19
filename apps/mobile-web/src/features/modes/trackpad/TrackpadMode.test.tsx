import { fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { defaultTrackpadSettings } from "../../../foundation/input/gestures";
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

afterEach(() => {
  Object.defineProperty(document, "visibilityState", { configurable: true, value: "visible" });
  vi.restoreAllMocks();
});

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
    expect(onMouseButtonUp).toHaveBeenCalledExactlyOnceWith("left");
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

  it("releases a held button exactly once when the component unmounts", () => {
    const onMouseButtonUp = vi.fn();
    const view = render(<TrackpadMode {...baseProps} onMouseButtonUp={onMouseButtonUp} />);

    fireEvent.pointerDown(screen.getByRole("button", { name: "Left" }), { pointerId: 9 });
    view.unmount();

    expect(onMouseButtonUp).toHaveBeenCalledExactlyOnceWith("left");
  });

  it("releases a held button after pointer capture is lost", () => {
    const onMouseButtonUp = vi.fn();
    render(<TrackpadMode {...baseProps} onMouseButtonUp={onMouseButtonUp} />);
    const leftButton = screen.getByRole("button", { name: "Left" });

    fireEvent.pointerDown(leftButton, { pointerId: 10 });
    fireEvent.lostPointerCapture(leftButton, { pointerId: 10 });

    expect(onMouseButtonUp).toHaveBeenCalledExactlyOnceWith("left");
  });

  it("releases a held button when the window loses focus", () => {
    const onMouseButtonUp = vi.fn();
    render(<TrackpadMode {...baseProps} onMouseButtonUp={onMouseButtonUp} />);

    fireEvent.pointerDown(screen.getByRole("button", { name: "Left" }), { pointerId: 11 });
    fireEvent.blur(window);

    expect(onMouseButtonUp).toHaveBeenCalledExactlyOnceWith("left");
  });

  it("releases a held button when the document becomes hidden", () => {
    const onMouseButtonUp = vi.fn();
    render(<TrackpadMode {...baseProps} onMouseButtonUp={onMouseButtonUp} />);

    fireEvent.pointerDown(screen.getByRole("button", { name: "Left" }), { pointerId: 12 });
    Object.defineProperty(document, "visibilityState", { configurable: true, value: "hidden" });
    fireEvent(document, new Event("visibilitychange"));

    expect(onMouseButtonUp).toHaveBeenCalledExactlyOnceWith("left");
  });

  it("does not duplicate releases when several cleanup signals arrive", () => {
    const onMouseButtonUp = vi.fn();
    const view = render(<TrackpadMode {...baseProps} onMouseButtonUp={onMouseButtonUp} />);
    const leftButton = screen.getByRole("button", { name: "Left" });

    fireEvent.pointerDown(leftButton, { pointerId: 13 });
    fireEvent.blur(window);
    Object.defineProperty(document, "visibilityState", { configurable: true, value: "hidden" });
    fireEvent(document, new Event("visibilitychange"));
    fireEvent.pointerCancel(leftButton, { pointerId: 13 });
    view.unmount();

    expect(onMouseButtonUp).toHaveBeenCalledExactlyOnceWith("left");
  });

  it("releases each independently held logical button", () => {
    const onMouseButtonUp = vi.fn();
    render(<TrackpadMode {...baseProps} onMouseButtonUp={onMouseButtonUp} />);

    fireEvent.pointerDown(screen.getByRole("button", { name: "Left" }), { pointerId: 14 });
    fireEvent.pointerDown(screen.getByRole("button", { name: "Right" }), { pointerId: 15 });
    fireEvent.blur(window);

    expect(onMouseButtonUp).toHaveBeenCalledTimes(2);
    expect(onMouseButtonUp).toHaveBeenCalledWith("left");
    expect(onMouseButtonUp).toHaveBeenCalledWith("right");
  });

  it("releases a logical button once when multiple pointers own it", () => {
    const onMouseButtonUp = vi.fn();
    render(<TrackpadMode {...baseProps} onMouseButtonUp={onMouseButtonUp} />);
    const leftButton = screen.getByRole("button", { name: "Left" });

    fireEvent.pointerDown(leftButton, { pointerId: 16 });
    fireEvent.pointerDown(leftButton, { pointerId: 17 });
    fireEvent.blur(window);

    expect(onMouseButtonUp).toHaveBeenCalledExactlyOnceWith("left");
  });

  it("does not treat a callback-only rerender as a cleanup boundary", () => {
    const onMouseButtonUp = vi.fn();
    const view = render(
      <TrackpadMode {...baseProps} onMouseButtonUp={(button) => { onMouseButtonUp(button); }} />
    );
    const leftButton = screen.getByRole("button", { name: "Left" });
    fireEvent.pointerDown(leftButton, { pointerId: 18 });

    view.rerender(
      <TrackpadMode {...baseProps} onMouseButtonUp={(button) => { onMouseButtonUp(button); }} />
    );

    expect(onMouseButtonUp).not.toHaveBeenCalled();
    fireEvent.pointerUp(leftButton, { pointerId: 18 });
    expect(onMouseButtonUp).toHaveBeenCalledExactlyOnceWith("left");
  });

  it("removes its global cleanup listeners on unmount", () => {
    const addWindowListener = vi.spyOn(window, "addEventListener");
    const removeWindowListener = vi.spyOn(window, "removeEventListener");
    const addDocumentListener = vi.spyOn(document, "addEventListener");
    const removeDocumentListener = vi.spyOn(document, "removeEventListener");
    const view = render(<TrackpadMode {...baseProps} />);
    const blurListener = addWindowListener.mock.calls.find(([type]) => type === "blur")?.[1];
    const visibilityListener = addDocumentListener.mock.calls.find(([type]) => type === "visibilitychange")?.[1];

    view.unmount();

    expect(blurListener).toBeTypeOf("function");
    expect(visibilityListener).toBeTypeOf("function");
    expect(removeWindowListener).toHaveBeenCalledWith("blur", blurListener);
    expect(removeDocumentListener).toHaveBeenCalledWith("visibilitychange", visibilityListener);
  });
});
