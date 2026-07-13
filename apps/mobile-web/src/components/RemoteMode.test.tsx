import { act, fireEvent, render, screen } from "@testing-library/react";
import type { ComponentProps } from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { defaultRemoteSettings } from "../remoteSettings";
import { RemoteMode } from "./RemoteMode";

const repeatStartDelayMs = 400;
const repeatIntervalMs = 55;

function renderRemote(overrides: Partial<ComponentProps<typeof RemoteMode>> = {}) {
  const props: ComponentProps<typeof RemoteMode> = {
    appLaunchActions: [],
    audioState: { type: "audio.state", volume: 50, muted: false },
    remoteSettings: defaultRemoteSettings,
    onPointerButtonClick: vi.fn(),
    onPointerMove: vi.fn(),
    onAppLaunch: vi.fn(),
    onPowerAction: vi.fn(),
    pendingPowerAction: null,
    pendingAppLaunchId: null,
    powerActionResult: null,
    powerCapabilities: null,
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
    ["Switch app", "Tab", ["Alt"]],
    ["Task view", "Tab", ["Win"]],
    ["Show desktop", "D", ["Win"]],
    ["Close focused window", "F4", ["Alt"]],
    ["Minimize focused window", "ArrowDown", ["Win"]],
    ["Browser back", "BrowserBack", undefined],
    ["New tab", "T", ["Control"]],
    ["Close tab", "W", ["Control"]],
    ["Reopen closed tab", "T", ["Control", "Shift"]],
    ["Next tab", "Tab", ["Control"]],
    ["Previous tab", "Tab", ["Control", "Shift"]],
    ["Reload page", "R", ["Control"]]
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
          appLaunchActions: [],
          audioState: { type: "audio.state", volume: 50, muted: false },
          remoteSettings: { ...defaultRemoteSettings, navigationRing: false, mode: "standard" },
          onPointerButtonClick: vi.fn(),
          onPointerMove: vi.fn(),
          onAppLaunch: vi.fn(),
          onPowerAction: vi.fn(),
          pendingPowerAction: null,
          pendingAppLaunchId: null,
          powerActionResult: null,
          powerCapabilities: null,
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
    ["Toggle video", "Tab", undefined],
    ["Stop playback", "X", undefined],
    ["Info", "I", undefined],
    ["Toggle subtitles", "T", undefined],
    ["Toggle fullscreen or windowed", "\\", undefined],
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
    expect(screen.getByRole("button", { name: "Toggle fullscreen or windowed" }).classList).toContain("remote-nav-action-fullscreen");
  });

  it("uses accessible names instead of title tooltips for Kodi controls", () => {
    renderRemote({ remoteSettings: { ...defaultRemoteSettings, navigationRing: true, mode: "kodi" } });

    expect(document.querySelectorAll(".remote-media-section [title], .remote-navigation-section [title]")).toHaveLength(0);
  });

  it("replaces Space with Stop and puts the power menu beside Toggle video only in Kodi mode", () => {
    const { rerender } = renderRemote({ remoteSettings: { ...defaultRemoteSettings, navigationRing: true, mode: "kodi" } });

    expect(screen.getByRole("button", { name: "Power menu" })).toBeTruthy();
    expect(screen.getByRole("button", { name: "Stop playback" }).textContent).toContain("Stop");
    expect(screen.queryByRole("button", { name: "Space" })).toBeNull();
    expect(
      screen
        .getAllByRole("button")
        .map((button) => button.getAttribute("aria-label"))
        .slice(3, 9)
    ).toEqual(["Seek backward", "Stop playback", "Seek forward", "Esc or back", "Toggle video", "Power menu"]);

    rerender(
      <RemoteMode
        {...{
          appLaunchActions: [],
          audioState: { type: "audio.state", volume: 50, muted: false },
          remoteSettings: { ...defaultRemoteSettings, navigationRing: true, mode: "standard" },
          onPointerButtonClick: vi.fn(),
          onPointerMove: vi.fn(),
          onAppLaunch: vi.fn(),
          onPowerAction: vi.fn(),
          pendingPowerAction: null,
          pendingAppLaunchId: null,
          powerActionResult: null,
          powerCapabilities: null,
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

  it("keeps essential helpers while hiding optional helper groups", () => {
    renderRemote({
      remoteSettings: { ...defaultRemoteSettings, showBrowserHelpers: false, showWindowHelpers: false }
    });

    expect(screen.getByRole("button", { name: "Start or search" })).toBeTruthy();
    expect(screen.getByRole("button", { name: "Switch app" })).toBeTruthy();
    expect(screen.getByRole("button", { name: "Task view" })).toBeTruthy();
    expect(screen.getByRole("button", { name: "Browser back" })).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Show desktop" })).toBeNull();
    expect(screen.queryByRole("button", { name: "New tab" })).toBeNull();
  });

  it("shows only host-approved application buttons and sends their opaque IDs", () => {
    const onAppLaunch = vi.fn();
    renderRemote({
      appLaunchActions: [
        { id: "preset.browser", label: "WWW", kind: "browser" },
        { id: "preset.powerpoint", label: "PPT", kind: "powerpoint" },
        { id: "custom.media", label: "Media Room", kind: "custom" }
      ],
      onAppLaunch
    });

    fireEvent.click(screen.getByRole("button", { name: "Fn" }));
    fireEvent.click(screen.getByRole("button", { name: "Start Media Room" }));

    expect(screen.getByRole("button", { name: "Start WWW" }).textContent).toContain("WWW");
    expect(screen.getByRole("button", { name: "Start PPT" }).textContent).toContain("PPT");
    expect(onAppLaunch).toHaveBeenCalledExactlyOnceWith("custom.media");
    expect(screen.queryByText(/\.exe/i)).toBeNull();
  });

  it("disables application buttons while pending and shows host result feedback", () => {
    const action = { id: "preset.browser", label: "Browser", kind: "browser" } as const;
    const { rerender } = renderRemote({ appLaunchActions: [action], pendingAppLaunchId: action.id });

    fireEvent.click(screen.getByRole("button", { name: "Fn" }));
    expect((screen.getByRole("button", { name: "Start Browser" }) as HTMLButtonElement).disabled).toBe(true);
    expect(screen.getByRole("status").textContent).toContain("Waiting for the PC");

    rerender(
      <RemoteMode
        {...{
          audioState: null,
          appLaunchActions: [action],
          remoteSettings: defaultRemoteSettings,
          onAppLaunch: vi.fn(),
          onPointerButtonClick: vi.fn(),
          onPointerMove: vi.fn(),
          onPowerAction: vi.fn(),
          pendingAppLaunchId: null,
          pendingPowerAction: null,
          powerActionResult: null,
          powerCapabilities: null,
          sendSpecial: vi.fn()
        }}
      />
    );
    fireEvent.click(screen.getByRole("button", { name: "Fn" }));
    expect(screen.getByRole("alert").textContent).toBe("Browser could not be started.");
  });
});

describe("RemoteMode app switcher", () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("keeps a short Switch app press equivalent to Alt+Tab", () => {
    const sendSpecial = vi.fn();
    renderRemote({ sendSpecial });
    const button = screen.getByRole("button", { name: "Switch app" });

    fireEvent.pointerDown(button, { button: 0, pointerId: 1, clientX: 100 });
    act(() => vi.advanceTimersByTime(200));
    fireEvent.pointerUp(button, { pointerId: 1, clientX: 100 });
    fireEvent.click(button);

    expect(sendSpecial).toHaveBeenCalledExactlyOnceWith("Tab", ["Alt"]);
  });

  it("opens the persistent switcher, steps in both directions, and commits on release", () => {
    const sendSpecial = vi.fn();
    renderRemote({ sendSpecial });
    const button = screen.getByRole("button", { name: "Switch app" });

    fireEvent.pointerDown(button, { button: 0, pointerId: 2, clientX: 100 });
    act(() => vi.advanceTimersByTime(400));

    expect(screen.getByRole("status").textContent).toContain("Slide left or right to choose");
    fireEvent.pointerMove(button, { pointerId: 2, clientX: 150 });
    fireEvent.pointerMove(button, { pointerId: 2, clientX: 55 });
    fireEvent.pointerUp(button, { pointerId: 2, clientX: 55 });

    expect(sendSpecial).toHaveBeenNthCalledWith(1, "Tab", ["Control", "Alt"]);
    expect(sendSpecial).toHaveBeenNthCalledWith(2, "Tab");
    expect(sendSpecial).toHaveBeenNthCalledWith(3, "Tab", ["Shift"]);
    expect(sendSpecial).toHaveBeenNthCalledWith(4, "Tab", ["Shift"]);
    expect(sendSpecial).toHaveBeenNthCalledWith(5, "Enter");
    expect(screen.queryByRole("status")).toBeNull();
  });

  it("cancels an active switcher when the pointer gesture is cancelled", () => {
    const sendSpecial = vi.fn();
    renderRemote({ sendSpecial });
    const button = screen.getByRole("button", { name: "Switch app" });

    fireEvent.pointerDown(button, { button: 0, pointerId: 3, clientX: 100 });
    act(() => vi.advanceTimersByTime(400));
    fireEvent.pointerCancel(button, { pointerId: 3 });

    expect(sendSpecial).toHaveBeenNthCalledWith(1, "Tab", ["Control", "Alt"]);
    expect(sendSpecial).toHaveBeenNthCalledWith(2, "Escape");
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
