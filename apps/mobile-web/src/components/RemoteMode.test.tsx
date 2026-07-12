import { act, fireEvent, render, screen } from "@testing-library/react";
import type { ComponentProps } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { defaultRemoteSettings } from "../remoteSettings";
import { RemoteMode } from "./RemoteMode";

const repeatStartDelayMs = 400;
const repeatIntervalMs = 55;

function renderRemote(overrides: Partial<ComponentProps<typeof RemoteMode>> = {}) {
  const props: ComponentProps<typeof RemoteMode> = {
    audioState: { type: "audio.state", volume: 50, muted: false },
    remoteSettings: defaultRemoteSettings,
    onPointerButtonClick: vi.fn(),
    onPointerMove: vi.fn(),
    sendSpecial: vi.fn(),
    ...overrides
  };

  const result = render(<RemoteMode {...props} />);
  return { ...props, ...result };
}

describe("RemoteMode", () => {
  it.each([
    ["Previous track", "MediaPreviousTrack", undefined],
    ["Play or pause", "MediaPlayPause", undefined],
    ["Next track", "MediaNextTrack", undefined],
    ["Seek backward", "ArrowLeft", undefined],
    ["Space", "Space", undefined],
    ["Seek forward", "ArrowRight", undefined],
    ["Esc or back", "Escape", undefined],
    ["Fullscreen (F)", "F", undefined],
    ["Browser fullscreen", "F11", undefined],
    ["Volume down", "VolumeDown", undefined],
    ["Mute PC", "VolumeMute", undefined],
    ["Volume up", "VolumeUp", undefined],
    ["Start or search", "Win", undefined],
    ["Alt Tab", "Tab", ["Alt"]],
    ["Browser back", "BrowserBack", undefined]
  ] as const)("sends %s", (buttonName, key, modifiers) => {
    const sendSpecial = vi.fn();
    renderRemote({ sendSpecial });

    fireEvent.click(screen.getByRole("button", { name: buttonName }));

    if (modifiers) {
      expect(sendSpecial).toHaveBeenCalledExactlyOnceWith(key, modifiers);
      return;
    }

    expect(sendSpecial).toHaveBeenCalledExactlyOnceWith(key);
  });

  it("shows the navigation ring by default and keeps OK in legacy D-pad mode", () => {
    const { rerender } = renderRemote();

    expect(screen.getByLabelText("Navigation ring")).toBeTruthy();
    expect(screen.getByRole("button", { name: "Mini trackpad" })).toBeTruthy();
    expect(screen.queryByRole("button", { name: "OK" })).toBeNull();

    rerender(
      <RemoteMode
        {...{
          audioState: { type: "audio.state", volume: 50, muted: false },
          remoteSettings: { ...defaultRemoteSettings, navigationRing: false, mode: "standard" },
          onPointerButtonClick: vi.fn(),
          onPointerMove: vi.fn(),
          sendSpecial: vi.fn()
        }}
      />
    );

    expect(screen.getByLabelText("Directional pad")).toBeTruthy();
    expect(screen.getByRole("button", { name: "OK" })).toBeTruthy();
  });

  it("sends OK as Enter in legacy D-pad mode", () => {
    const sendSpecial = vi.fn();
    renderRemote({ remoteSettings: { ...defaultRemoteSettings, navigationRing: false, mode: "standard" }, sendSpecial });

    fireEvent.click(screen.getByRole("button", { name: "OK" }));

    expect(sendSpecial).toHaveBeenCalledExactlyOnceWith("Enter");
  });

  it.each([
    ["Previous track", "P", ["Shift"]],
    ["Play or pause", "K", undefined],
    ["Next track", "N", ["Shift"]],
    ["Seek backward", "J", undefined],
    ["Seek forward", "L", undefined],
    ["Volume down", "ArrowDown", undefined],
    ["Mute PC", "M", undefined],
    ["Volume up", "ArrowUp", undefined]
  ] as const)("sends YouTube shortcut for %s when YouTube mode is enabled", (buttonName, key, modifiers) => {
    const sendSpecial = vi.fn();
    renderRemote({ remoteSettings: { ...defaultRemoteSettings, navigationRing: true, mode: "youtube" }, sendSpecial });

    fireEvent.click(screen.getByRole("button", { name: buttonName }));

    if (modifiers) {
      expect(sendSpecial).toHaveBeenCalledExactlyOnceWith(key, modifiers);
      return;
    }

    expect(sendSpecial).toHaveBeenCalledExactlyOnceWith(key);
  });

  it.each([
    ["Previous track", "MediaPreviousTrack", undefined],
    ["Play or pause", "Space", undefined],
    ["Next track", "MediaNextTrack", undefined],
    ["Seek backward", "ArrowLeft", undefined],
    ["Power menu", "S", undefined],
    ["Seek forward", "ArrowRight", undefined],
    ["Esc or back", "Backspace", undefined],
    ["Fullscreen", "Tab", undefined],
    ["End playback", "X", undefined],
    ["Info", "I", undefined],
    ["Toggle subtitles", "T", undefined],
    ["Volume down", "-", undefined],
    ["Mute PC", "F8", undefined],
    ["Volume up", "+", undefined]
  ] as const)("sends Kodi shortcut for %s when Kodi mode is enabled", (buttonName, key, modifiers) => {
    const sendSpecial = vi.fn();
    renderRemote({ remoteSettings: { ...defaultRemoteSettings, navigationRing: true, mode: "kodi" }, sendSpecial });

    fireEvent.click(screen.getByRole("button", { name: buttonName }));

    if (modifiers) {
      expect(sendSpecial).toHaveBeenCalledExactlyOnceWith(key, modifiers);
      return;
    }

    expect(sendSpecial).toHaveBeenCalledExactlyOnceWith(key);
  });

  it("hides the separate browser fullscreen button in Kodi mode", () => {
    renderRemote({ remoteSettings: { ...defaultRemoteSettings, navigationRing: true, mode: "kodi" } });

    expect(screen.queryByRole("button", { name: "Browser fullscreen" })).toBeNull();
  });

  it("replaces Space with Stop and puts the power menu beside Fullscreen only in Kodi mode", () => {
    const { rerender } = renderRemote({ remoteSettings: { ...defaultRemoteSettings, navigationRing: true, mode: "kodi" } });

    expect(screen.getByRole("button", { name: "Power menu" })).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Space" })).toBeNull();
    expect(
      screen
        .getAllByRole("button")
        .map((button) => button.getAttribute("aria-label"))
        .slice(3, 9)
    ).toEqual(["Seek backward", "End playback", "Seek forward", "Esc or back", "Fullscreen", "Power menu"]);

    rerender(
      <RemoteMode
        {...{
          audioState: { type: "audio.state", volume: 50, muted: false },
          remoteSettings: { ...defaultRemoteSettings, navigationRing: true, mode: "standard" },
          onPointerButtonClick: vi.fn(),
          onPointerMove: vi.fn(),
          sendSpecial: vi.fn()
        }}
      />
    );

    expect(screen.getByRole("button", { name: "Space" })).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Power menu" })).toBeNull();
  });

  it("keeps remote volume buttons enabled without audio state", () => {
    renderRemote({ audioState: null });

    expect((screen.getByRole("button", { name: "Volume down" }) as HTMLButtonElement).disabled).toBe(false);
    expect((screen.getByRole("button", { name: "Mute PC" }) as HTMLButtonElement).disabled).toBe(false);
    expect((screen.getByRole("button", { name: "Volume up" }) as HTMLButtonElement).disabled).toBe(false);
  });

  it("toggles the compact Windows helper panel with the Fn button", () => {
    renderRemote();

    const remoteMode = screen.getByLabelText("Couch remote");
    fireEvent.click(screen.getByRole("button", { name: "Fn" }));

    expect(remoteMode.classList.contains("remote-utility-open")).toBe(true);

    fireEvent.click(screen.getByRole("button", { name: "Main" }));

    expect(remoteMode.classList.contains("remote-utility-open")).toBe(false);
  });
});

describe("RemoteMode repeatable controls", () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it.each([
    ["Seek backward", "ArrowLeft"],
    ["Seek forward", "ArrowRight"],
    ["D-pad up", "ArrowUp"],
    ["D-pad left", "ArrowLeft"],
    ["D-pad right", "ArrowRight"],
    ["D-pad down", "ArrowDown"]
  ] as const)("repeats %s until release", (buttonName, key) => {
    const sendSpecial = vi.fn();
    renderRemote({ sendSpecial });

    const button = screen.getByRole("button", { name: buttonName });
    fireEvent.pointerDown(button, { button: 0, pointerId: 1 });

    act(() => {
      vi.advanceTimersByTime(repeatStartDelayMs + repeatIntervalMs);
    });

    fireEvent.pointerUp(button, { pointerId: 1 });
    fireEvent.click(button);

    expect(sendSpecial).toHaveBeenCalledTimes(3);
    expect(sendSpecial).toHaveBeenNthCalledWith(1, key);
    expect(sendSpecial).toHaveBeenNthCalledWith(2, key);
    expect(sendSpecial).toHaveBeenNthCalledWith(3, key);
  });

  it("repeats volume key presses until release", () => {
    const sendSpecial = vi.fn();
    renderRemote({ sendSpecial });

    const button = screen.getByRole("button", { name: "Volume up" });
    fireEvent.pointerDown(button, { button: 0, pointerId: 1 });

    act(() => {
      vi.advanceTimersByTime(repeatStartDelayMs + repeatIntervalMs);
    });

    fireEvent.pointerUp(button, { pointerId: 1 });
    fireEvent.click(button);

    expect(sendSpecial).toHaveBeenCalledTimes(3);
    expect(sendSpecial).toHaveBeenNthCalledWith(1, "VolumeUp");
    expect(sendSpecial).toHaveBeenNthCalledWith(2, "VolumeUp");
    expect(sendSpecial).toHaveBeenNthCalledWith(3, "VolumeUp");
  });

  it("repeats YouTube volume shortcuts until release", () => {
    const sendSpecial = vi.fn();
    renderRemote({ remoteSettings: { ...defaultRemoteSettings, navigationRing: true, mode: "youtube" }, sendSpecial });

    const button = screen.getByRole("button", { name: "Volume down" });
    fireEvent.pointerDown(button, { button: 0, pointerId: 1 });

    act(() => {
      vi.advanceTimersByTime(repeatStartDelayMs + repeatIntervalMs);
    });

    fireEvent.pointerUp(button, { pointerId: 1 });
    fireEvent.click(button);

    expect(sendSpecial).toHaveBeenCalledTimes(3);
    expect(sendSpecial).toHaveBeenNthCalledWith(1, "ArrowDown");
    expect(sendSpecial).toHaveBeenNthCalledWith(2, "ArrowDown");
    expect(sendSpecial).toHaveBeenNthCalledWith(3, "ArrowDown");
  });
});

describe("RemoteMode mini trackpad", () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("moves the pointer when dragged", () => {
    const onPointerMove = vi.fn();
    renderRemote({ onPointerMove });

    const trackpad = screen.getByRole("button", { name: "Mini trackpad" });
    fireEvent.pointerDown(trackpad, { button: 0, pointerId: 1, clientX: 10, clientY: 20 });
    fireEvent.pointerMove(trackpad, { pointerId: 1, clientX: 14, clientY: 18 });

    expect(onPointerMove).toHaveBeenCalledExactlyOnceWith(5.4, -2.7);
  });

  it("turns a single tap into a left click", () => {
    const onPointerButtonClick = vi.fn();
    renderRemote({ onPointerButtonClick });

    const trackpad = screen.getByRole("button", { name: "Mini trackpad" });
    fireEvent.pointerDown(trackpad, { button: 0, pointerId: 1, clientX: 10, clientY: 20 });
    fireEvent.pointerUp(trackpad, { pointerId: 1, clientX: 10, clientY: 20, timeStamp: 100 });

    expect(onPointerButtonClick).not.toHaveBeenCalled();

    act(() => {
      vi.advanceTimersByTime(280);
    });

    expect(onPointerButtonClick).toHaveBeenCalledExactlyOnceWith("left");
  });

  it("turns a Kodi mini trackpad tap into Enter instead of a left click", () => {
    const onPointerButtonClick = vi.fn();
    const sendSpecial = vi.fn();
    renderRemote({ remoteSettings: { ...defaultRemoteSettings, navigationRing: true, mode: "kodi" }, onPointerButtonClick, sendSpecial });

    const trackpad = screen.getByRole("button", { name: "Mini trackpad" });
    fireEvent.pointerDown(trackpad, { button: 0, pointerId: 1, clientX: 10, clientY: 20 });
    fireEvent.pointerUp(trackpad, { pointerId: 1, clientX: 10, clientY: 20, timeStamp: 100 });

    act(() => {
      vi.advanceTimersByTime(280);
    });

    expect(sendSpecial).toHaveBeenCalledExactlyOnceWith("Enter");
    expect(onPointerButtonClick).not.toHaveBeenCalled();
  });

  it("turns Kodi mini trackpad keyboard activation into Enter", () => {
    const onPointerButtonClick = vi.fn();
    const sendSpecial = vi.fn();
    renderRemote({ remoteSettings: { ...defaultRemoteSettings, navigationRing: true, mode: "kodi" }, onPointerButtonClick, sendSpecial });

    fireEvent.keyDown(screen.getByRole("button", { name: "Mini trackpad" }), { key: "Enter" });

    expect(sendSpecial).toHaveBeenCalledExactlyOnceWith("Enter");
    expect(onPointerButtonClick).not.toHaveBeenCalled();
  });

  it("turns a double tap into a right click without sending the pending left click", () => {
    const onPointerButtonClick = vi.fn();
    renderRemote({ onPointerButtonClick });

    const trackpad = screen.getByRole("button", { name: "Mini trackpad" });
    fireEvent.pointerDown(trackpad, { button: 0, pointerId: 1, clientX: 10, clientY: 20 });
    fireEvent.pointerUp(trackpad, { pointerId: 1, clientX: 10, clientY: 20, timeStamp: 100 });
    fireEvent.pointerDown(trackpad, { button: 0, pointerId: 2, clientX: 12, clientY: 21 });
    fireEvent.pointerUp(trackpad, { pointerId: 2, clientX: 12, clientY: 21, timeStamp: 240 });

    act(() => {
      vi.advanceTimersByTime(280);
    });

    expect(onPointerButtonClick).toHaveBeenCalledExactlyOnceWith("right");
  });

  it("turns a navigation panel background tap into a left click", () => {
    const onPointerButtonClick = vi.fn();
    renderRemote({ onPointerButtonClick });

    const panel = screen.getByText("Navigation").closest(".remote-navigation-section") as HTMLElement;
    fireEvent.pointerDown(panel, { button: 0, pointerId: 1, clientX: 20, clientY: 30 });
    fireEvent.pointerUp(panel, { pointerId: 1, clientX: 20, clientY: 30, timeStamp: 100 });

    expect(onPointerButtonClick).not.toHaveBeenCalled();

    act(() => {
      vi.advanceTimersByTime(280);
    });

    expect(onPointerButtonClick).toHaveBeenCalledExactlyOnceWith("left");
  });

  it("turns a navigation panel background double tap into a right click", () => {
    const onPointerButtonClick = vi.fn();
    renderRemote({ onPointerButtonClick });

    const panel = screen.getByText("Navigation").closest(".remote-navigation-section") as HTMLElement;
    fireEvent.pointerDown(panel, { button: 0, pointerId: 1, clientX: 20, clientY: 30 });
    fireEvent.pointerUp(panel, { pointerId: 1, clientX: 20, clientY: 30, timeStamp: 100 });
    fireEvent.pointerDown(panel, { button: 0, pointerId: 2, clientX: 22, clientY: 31 });
    fireEvent.pointerUp(panel, { pointerId: 2, clientX: 22, clientY: 31, timeStamp: 240 });

    act(() => {
      vi.advanceTimersByTime(280);
    });

    expect(onPointerButtonClick).toHaveBeenCalledExactlyOnceWith("right");
  });

  it("moves the pointer when the navigation panel background is dragged", () => {
    const onPointerButtonClick = vi.fn();
    const onPointerMove = vi.fn();
    renderRemote({ onPointerButtonClick, onPointerMove });

    const panel = screen.getByText("Navigation").closest(".remote-navigation-section") as HTMLElement;
    fireEvent.pointerDown(panel, { button: 0, pointerId: 1, clientX: 20, clientY: 30 });
    fireEvent.pointerMove(panel, { pointerId: 1, clientX: 32, clientY: 24 });
    fireEvent.pointerUp(panel, { pointerId: 1, clientX: 32, clientY: 24, timeStamp: 100 });

    act(() => {
      vi.advanceTimersByTime(280);
    });

    expect(onPointerMove).toHaveBeenCalledExactlyOnceWith(16.2, -8.1);
    expect(onPointerButtonClick).not.toHaveBeenCalled();
  });

  it("does not turn ring button presses into mouse clicks", () => {
    const onPointerButtonClick = vi.fn();
    const sendSpecial = vi.fn();
    renderRemote({ onPointerButtonClick, sendSpecial });

    const button = screen.getByRole("button", { name: "D-pad left" });
    fireEvent.pointerDown(button, { button: 0, pointerId: 1, clientX: 20, clientY: 30 });
    fireEvent.pointerUp(button, { pointerId: 1, clientX: 20, clientY: 30, timeStamp: 100 });

    act(() => {
      vi.advanceTimersByTime(280);
    });

    expect(sendSpecial).toHaveBeenCalledExactlyOnceWith("ArrowLeft");
    expect(onPointerButtonClick).not.toHaveBeenCalled();
  });
});
