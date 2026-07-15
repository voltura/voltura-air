import { fireEvent, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { defaultRemoteSettings } from "../remoteSettings";
import { RemoteMode } from "./RemoteMode";
import { renderRemote } from "./remoteModeTestUtils";
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
          onUrlOpen: vi.fn(),
          onPowerAction: vi.fn(),
          pendingPowerAction: null,
          pendingAppLaunchId: null,
          pendingUrlOpen: false,
          powerActionResult: null,
          powerCapabilities: null,
          urlOpenCapability: { canOpen: true },
          urlOpenResult: null,
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
          onUrlOpen: vi.fn(),
          onPowerAction: vi.fn(),
          pendingPowerAction: null,
          pendingAppLaunchId: null,
          pendingUrlOpen: false,
          powerActionResult: null,
          powerCapabilities: null,
          urlOpenCapability: { canOpen: true },
          urlOpenResult: null,
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

  it("disables application buttons and shows progress while pending", () => {
    const action = { id: "preset.browser", label: "Browser", kind: "browser" } as const;
    renderRemote({ appLaunchActions: [action], pendingAppLaunchId: action.id });

    fireEvent.click(screen.getByRole("button", { name: "Fn" }));
    const button = screen.getByRole("button", { name: "Start Browser" }) as HTMLButtonElement;

    expect(button.disabled).toBe(true);
    expect(button.textContent).toContain("Starting…");
  });

  it("preserves the URL draft after a returned failure and offers retry", () => {
    const onUrlOpen = vi.fn(() => "url-operation-a");
    const view = renderRemote({ onUrlOpen });
    fireEvent.click(screen.getByRole("button", { name: "Fn" }));

    const input = screen.getByLabelText("Open URL on PC") as HTMLInputElement;
    fireEvent.change(input, { target: { value: "example.com/page?q=test" } });
    fireEvent.click(screen.getByRole("button", { name: "Open" }));

    expect(onUrlOpen).toHaveBeenCalledExactlyOnceWith("example.com/page?q=test");

    view.rerender(
      <RemoteMode
        {...view}
        pendingUrlOpen={false}
        urlOpenResult={{
          type: "url.open.result",
          operationId: "url-operation-a",
          succeeded: false,
          code: "launch-failed",
          message: "Windows could not open the URL using the default browser."
        }}
      />
    );

    expect((screen.getByLabelText("Open URL on PC") as HTMLInputElement).value).toBe("example.com/page?q=test");
    expect(screen.getByRole("alert").textContent).toContain("default browser");
    fireEvent.click(screen.getByRole("button", { name: "Retry" }));
    expect(onUrlOpen).toHaveBeenCalledTimes(2);
  });

  it("keeps URL controls hidden until the PC permission is available", () => {
    renderRemote({ urlOpenCapability: { canOpen: false } });
    fireEvent.click(screen.getByRole("button", { name: "Fn" }));

    expect(screen.getByRole("alert").textContent).toContain("Allow URL opening");
    expect(screen.queryByLabelText("Open URL on PC")).toBeNull();
    expect(screen.queryByRole("button", { name: "Open" })).toBeNull();
  });

  it("validates URL drafts before enabling Open", () => {
    const onUrlOpen = vi.fn(() => "url-operation-a");
    renderRemote({ onUrlOpen });
    fireEvent.click(screen.getByRole("button", { name: "Fn" }));

    const input = screen.getByLabelText("Open URL on PC") as HTMLInputElement;
    const button = screen.getByRole("button", { name: "Open" }) as HTMLButtonElement;
    expect(button.disabled).toBe(true);

    fireEvent.change(input, { target: { value: "javascript:alert(1)" } });
    expect(button.disabled).toBe(true);
    expect(input.getAttribute("aria-invalid")).toBe("true");
    expect(screen.getByRole("alert").textContent).toContain("HTTP or HTTPS");
    fireEvent.click(button);
    expect(onUrlOpen).not.toHaveBeenCalled();

    fireEvent.change(input, { target: { value: "router" } });
    expect(button.disabled).toBe(false);
    expect(input.hasAttribute("aria-invalid")).toBe(false);
    fireEvent.click(button);
    expect(onUrlOpen).toHaveBeenCalledExactlyOnceWith("router");
  });

  it("opens URL guidance in the detailed information dialog", () => {
    renderRemote();
    fireEvent.click(screen.getByRole("button", { name: "Fn" }));
    fireEvent.click(screen.getByRole("button", { name: "About Open URL on PC" }));

    const dialog = screen.getByRole("dialog", { name: "Open URL on PC" });
    expect(dialog.classList.contains("info-dialog-detailed")).toBe(true);
    expect(dialog.textContent).toContain("Addresses without a scheme use HTTPS");
    expect(dialog.textContent).toContain("default browser");
    fireEvent.click(screen.getByRole("button", { name: "OK" }));
    expect(screen.queryByRole("dialog", { name: "Open URL on PC" })).toBeNull();
  });
});
