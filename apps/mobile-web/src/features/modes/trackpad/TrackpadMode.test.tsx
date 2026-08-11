import { createEvent, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { defaultTrackpadSettings } from "../../../foundation/input/gestures";
import { TrackpadMode } from "./TrackpadMode";

const baseProps = {
  audioState: { type: "audio.state" as const, volume: 45, muted: false },
  isExpanded: false,
  supportsVolumeControl: true,
  trackpadSettings: defaultTrackpadSettings,
  twoFingerMode: "scroll" as const,
  onMouseButtonDown: vi.fn(),
  onMouseButtonUp: vi.fn(),
  onSetVolume: vi.fn(),
  onToggleExpanded: vi.fn(),
  onToggleMute: vi.fn(),
  onTwoFingerModeChange: vi.fn(),
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

describe("TrackpadMode gyro movement", () => {
  it("disables touch gestures and uses the surface as a clutch", () => {
    const setEngaged = vi.fn();
    const onTouchStart = vi.fn();
    const view = render(<TrackpadMode {...baseProps} onTouchStart={onTouchStart} gyro={{
      availability: "ready",
      enableFromUserGesture: vi.fn(),
      engaged: false,
      selected: true,
      setEngaged,
      setSelected: vi.fn()
    }} />);
    const surface = view.container.querySelector(".trackpad-surface")!;

    fireEvent.touchStart(surface);
    fireEvent.pointerDown(surface, { pointerId: 41, button: 0, isPrimary: true });
    fireEvent.pointerUp(surface, { pointerId: 41 });

    expect(onTouchStart).not.toHaveBeenCalled();
    expect(setEngaged).toHaveBeenNthCalledWith(1, true);
    expect(setEngaged).toHaveBeenLastCalledWith(false);
  });

  it("uses a short stationary clutch tap as a primary click", () => {
    const calls: string[] = [];
    const view = render(<TrackpadMode
      {...baseProps}
      onMouseButtonDown={(button) => { calls.push(`${button}-down`); }}
      onMouseButtonUp={(button) => { calls.push(`${button}-up`); }}
      gyro={{
        availability: "ready",
        enableFromUserGesture: vi.fn(),
        engaged: false,
        selected: true,
        setEngaged: (value) => { calls.push(value ? "move-on" : "move-off"); },
        setSelected: vi.fn()
      }}
    />);
    const surface = view.container.querySelector(".trackpad-surface")!;

    fireEvent.pointerDown(surface, { pointerId: 51, button: 0, isPrimary: true });
    fireEvent.pointerUp(surface, { pointerId: 51 });

    expect(calls).toEqual(["move-on", "move-off", "left-down", "left-up"]);
  });

  it("uses press duration rather than live sensor noise to recognize a tap", () => {
    const onMouseButtonDown = vi.fn();
    const onMouseButtonUp = vi.fn();
    const view = render(<TrackpadMode
      {...baseProps}
      onMouseButtonDown={onMouseButtonDown}
      onMouseButtonUp={onMouseButtonUp}
      gyro={{
        availability: "ready",
        enableFromUserGesture: vi.fn(),
        engaged: false,
        selected: true,
        setEngaged: vi.fn(),
        setSelected: vi.fn()
      }}
    />);
    const surface = view.container.querySelector(".trackpad-surface")!;

    fireEvent.pointerDown(surface, { pointerId: 52, button: 0, isPrimary: true });
    fireEvent.pointerUp(surface, { pointerId: 52 });

    expect(onMouseButtonDown).toHaveBeenCalledWith("left");
    expect(onMouseButtonUp).toHaveBeenCalledWith("left");
  });

  it("uses the touch lifecycle for a phone tap and ignores its duplicate pointer events", () => {
    const calls: string[] = [];
    const view = render(<TrackpadMode
      {...baseProps}
      onMouseButtonDown={(button) => { calls.push(`${button}-down`); }}
      onMouseButtonUp={(button) => { calls.push(`${button}-up`); }}
      gyro={{
        availability: "ready",
        enableFromUserGesture: vi.fn(),
        engaged: false,
        selected: true,
        setEngaged: (value) => { calls.push(value ? "move-on" : "move-off"); },
        setSelected: vi.fn()
      }}
    />);
    const surface = view.container.querySelector(".trackpad-surface")!;
    const touch = { identifier: 71 };

    fireEvent.pointerDown(surface, { pointerId: 71, pointerType: "touch", button: 0, isPrimary: true });
    fireEvent.touchStart(surface, { touches: [touch], changedTouches: [touch] });
    fireEvent.pointerUp(surface, { pointerId: 71, pointerType: "touch", button: 0, isPrimary: true });
    fireEvent.touchEnd(surface, { touches: [], changedTouches: [touch] });

    expect(calls).toEqual(["move-on", "move-off", "left-down", "left-up"]);
  });

  it("does not click after a held or cancelled clutch press", () => {
    const onMouseButtonDown = vi.fn();
    const onMouseButtonUp = vi.fn();
    const view = render(<TrackpadMode
      {...baseProps}
      onMouseButtonDown={onMouseButtonDown}
      onMouseButtonUp={onMouseButtonUp}
      gyro={{
        availability: "ready",
        enableFromUserGesture: vi.fn(),
        engaged: false,
        selected: true,
        setEngaged: vi.fn(),
        setSelected: vi.fn()
      }}
    />);
    const surface = view.container.querySelector(".trackpad-surface")!;

    const heldDown = createEvent.pointerDown(surface, { pointerId: 53, button: 0, isPrimary: true });
    const heldUp = createEvent.pointerUp(surface, { pointerId: 53 });
    Object.defineProperty(heldDown, "timeStamp", { value: 100 });
    Object.defineProperty(heldUp, "timeStamp", { value: 401 });
    fireEvent(surface, heldDown);
    fireEvent(surface, heldUp);
    fireEvent.pointerDown(surface, { pointerId: 54, button: 0, isPrimary: true });
    fireEvent.pointerCancel(surface, { pointerId: 54 });
    fireEvent.pointerUp(surface, { pointerId: 54 });

    expect(onMouseButtonDown).not.toHaveBeenCalled();
    expect(onMouseButtonUp).not.toHaveBeenCalled();
  });

  it("clicks with Enter and uses Space as the keyboard movement clutch", () => {
    const calls: string[] = [];
    render(<TrackpadMode
      {...baseProps}
      onMouseButtonDown={(button) => { calls.push(`${button}-down`); }}
      onMouseButtonUp={(button) => { calls.push(`${button}-up`); }}
      gyro={{
        availability: "ready",
        enableFromUserGesture: vi.fn(),
        engaged: false,
        selected: true,
        setEngaged: (value) => { calls.push(value ? "move-on" : "move-off"); },
        setSelected: vi.fn()
      }}
    />);
    const clutch = screen.getByRole("button", { name: "Tap to click, hold to move the mouse" });

    fireEvent.keyDown(clutch, { key: "Enter" });
    fireEvent.keyDown(clutch, { key: " ", code: "Space" });
    fireEvent.keyUp(clutch, { key: " ", code: "Space" });

    expect(calls).toEqual(["left-down", "left-up", "move-on", "move-off"]);
  });

  it("supports synthetic assistive activation without doubling a physical click", () => {
    const onMouseButtonDown = vi.fn();
    const onMouseButtonUp = vi.fn();
    render(<TrackpadMode
      {...baseProps}
      onMouseButtonDown={onMouseButtonDown}
      onMouseButtonUp={onMouseButtonUp}
      gyro={{
        availability: "ready",
        enableFromUserGesture: vi.fn(),
        engaged: false,
        selected: true,
        setEngaged: vi.fn(),
        setSelected: vi.fn()
      }}
    />);
    const clutch = screen.getByRole("button", { name: "Tap to click, hold to move the mouse" });

    fireEvent.click(clutch, { detail: 0 });
    fireEvent.click(clutch, { detail: 1 });

    expect(onMouseButtonDown).toHaveBeenCalledExactlyOnceWith("left");
    expect(onMouseButtonUp).toHaveBeenCalledExactlyOnceWith("left");
  });

  it("clears an active clutch when Gyro stops accepting input", () => {
    const setEngaged = vi.fn();
    const gyroBase = {
      enableFromUserGesture: vi.fn(),
      engaged: false,
      selected: true,
      setEngaged,
      setSelected: vi.fn()
    };
    const view = render(<TrackpadMode {...baseProps} gyro={{ ...gyroBase, availability: "ready" }} />);
    const surface = view.container.querySelector(".trackpad-surface")!;

    fireEvent.pointerDown(surface, { pointerId: 61, button: 0, isPrimary: true });
    view.rerender(<TrackpadMode {...baseProps} gyro={{ ...gyroBase, availability: "no-data" }} />);
    expect(setEngaged).toHaveBeenLastCalledWith(false);

    view.rerender(<TrackpadMode {...baseProps} gyro={{ ...gyroBase, availability: "ready" }} />);
    fireEvent.pointerDown(surface, { pointerId: 62, button: 0, isPrimary: true });
    expect(setEngaged).toHaveBeenLastCalledWith(true);
  });

  it("rejects secondary pointers and invalidates a tap when another pointer joins", () => {
    const onMouseButtonDown = vi.fn();
    const onMouseButtonUp = vi.fn();
    const setEngaged = vi.fn();
    const view = render(<TrackpadMode
      {...baseProps}
      onMouseButtonDown={onMouseButtonDown}
      onMouseButtonUp={onMouseButtonUp}
      gyro={{
        availability: "ready",
        enableFromUserGesture: vi.fn(),
        engaged: false,
        selected: true,
        setEngaged,
        setSelected: vi.fn()
      }}
    />);
    const surface = view.container.querySelector(".trackpad-surface")!;

    fireEvent.pointerDown(surface, { pointerId: 63, button: 2, isPrimary: true });
    expect(setEngaged).not.toHaveBeenCalled();
    fireEvent.pointerDown(surface, { pointerId: 64, button: 0, isPrimary: true });
    fireEvent.pointerDown(surface, { pointerId: 65, button: 0, isPrimary: false });
    fireEvent.pointerUp(surface, { pointerId: 64, button: 0, isPrimary: true });

    expect(onMouseButtonDown).not.toHaveBeenCalled();
    expect(onMouseButtonUp).not.toHaveBeenCalled();
  });

  it("does not add a surface click after an explicit button tap during the clutch", () => {
    const buttonCalls: string[] = [];
    const view = render(<TrackpadMode
      {...baseProps}
      onMouseButtonDown={(button) => { buttonCalls.push(`${button}-down`); }}
      onMouseButtonUp={(button) => { buttonCalls.push(`${button}-up`); }}
      gyro={{
        availability: "ready",
        enableFromUserGesture: vi.fn(),
        engaged: false,
        selected: true,
        setEngaged: vi.fn(),
        setSelected: vi.fn()
      }}
    />);
    const surface = view.container.querySelector(".trackpad-surface")!;
    const right = screen.getByRole("button", { name: "Right" });

    fireEvent.pointerDown(surface, { pointerId: 66, button: 0, isPrimary: true });
    fireEvent.pointerDown(right, { pointerId: 67, button: 0, isPrimary: true });
    fireEvent.pointerUp(right, { pointerId: 67, button: 0, isPrimary: true });
    fireEvent.pointerUp(surface, { pointerId: 66, button: 0, isPrimary: true });

    expect(buttonCalls).toEqual(["right-down", "right-up"]);
  });

  it("engages movement while a mouse button is held and preserves button ordering", () => {
    const calls: string[] = [];
    render(<TrackpadMode {...baseProps}
      onMouseButtonDown={(button) => { calls.push(`${button}-down`); }}
      onMouseButtonUp={(button) => { calls.push(`${button}-up`); }}
      gyro={{
        availability: "ready",
        enableFromUserGesture: vi.fn(),
        engaged: false,
        selected: true,
        setEngaged: (value) => { calls.push(value ? "move-on" : "move-off"); },
        setSelected: vi.fn()
      }}
    />);
    const left = screen.getByRole("button", { name: "Left" });

    fireEvent.pointerDown(left, { pointerId: 42 });
    fireEvent.pointerUp(left, { pointerId: 42 });

    expect(calls).toEqual(["move-on", "left-down", "left-up", "move-off"]);
  });
});

describe("TrackpadMode two-finger mode", () => {
  it("shows an isolated Scroll and Zoom switch when Pinch zoom is enabled", () => {
    const onTouchStart = vi.fn();
    const onTwoFingerModeChange = vi.fn();
    const view = render(<TrackpadMode
      {...baseProps}
      onTouchStart={onTouchStart}
      onTwoFingerModeChange={onTwoFingerModeChange}
      trackpadSettings={{ ...defaultTrackpadSettings, zoomGestures: true }}
    />);
    const scrollButton = screen.getByRole("button", { name: "Two-finger mode: Scroll. Switch to Zoom" });

    fireEvent.touchStart(scrollButton, { targetTouches: [{ identifier: 1, clientX: 20, clientY: 20 }] });
    fireEvent.touchEnd(scrollButton, { targetTouches: [] });
    fireEvent.click(scrollButton);

    expect(onTouchStart).not.toHaveBeenCalled();
    expect(onTwoFingerModeChange).toHaveBeenCalledExactlyOnceWith("zoom");

    view.rerender(<TrackpadMode
      {...baseProps}
      onTwoFingerModeChange={onTwoFingerModeChange}
      trackpadSettings={{ ...defaultTrackpadSettings, zoomGestures: true }}
      twoFingerMode="zoom"
    />);
    fireEvent.click(screen.getByRole("button", { name: "Two-finger mode: Zoom. Switch to Scroll" }));
    expect(onTwoFingerModeChange).toHaveBeenLastCalledWith("scroll");
  });

  it("hides the switch when Pinch zoom is disabled", () => {
    render(<TrackpadMode {...baseProps} />);

    expect(screen.queryByRole("button", { name: "Two-finger mode: Scroll. Switch to Zoom" })).toBeNull();
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
