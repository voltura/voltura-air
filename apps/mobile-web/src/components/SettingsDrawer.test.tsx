import { fireEvent, render, screen, within } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { defaultAppSettings } from "../appSettings";
import { defaultTrackpadSettings } from "../gestures";
import { defaultKeyboardSettings } from "../keyboardSettings";
import { defaultRemoteSettings } from "../remoteSettings";
import { SettingsDrawer } from "./SettingsDrawer";

const baseProps = {
  activePc: null,
  appSettings: defaultAppSettings,
  diagnostics: "{}",
  deviceName: "Phone",
  disconnectActivePc: vi.fn(),
  forgetPc: vi.fn(),
  installApp: vi.fn(),
  installPrompt: null,
  isInstalled: false,
  isOpen: true,
  keyboardSettings: defaultKeyboardSettings,
  onClose: vi.fn(),
  onManualHostSubmit: vi.fn(),
  onOpenGestureDebug: vi.fn(),
  onPairingQrSelected: vi.fn(),
  pairedPcs: [],
  pairingQrInputRef: { current: null },
  pairingScanMessage: "Scan the QR code shown on your PC.",
  refreshInstalledApp: vi.fn(),
  refreshMessage: "Reload from the PC if the home screen app looks stale.",
  renameDevice: vi.fn(),
  renamePc: vi.fn(),
  remoteSettings: defaultRemoteSettings,
  scanPairingQr: vi.fn(),
  selectPc: vi.fn(),
  setThemeMode: vi.fn(),
  showGestureDebug: false,
  supportsRemoteLaunch: false,
  themeMode: "system" as const,
  trackpadSettings: defaultTrackpadSettings,
  updateAppSetting: vi.fn(),
  updateKeyboardSetting: vi.fn(),
  updateRemoteSetting: vi.fn(),
  updateTrackpadSetting: vi.fn()
};

describe("SettingsDrawer", () => {
  beforeEach(() => {
    vi.stubGlobal("__APP_VERSION__", "test-version");
    Object.defineProperty(navigator, "vibrate", { configurable: true, value: vi.fn(() => true) });
  });

  it("groups settings into folded accordions by default", () => {
    render(<SettingsDrawer {...baseProps} />);

    expect(screen.getByText("Connection")).toBeTruthy();
    expect(screen.getByText("Trackpad")).toBeTruthy();
    expect(screen.getByText("Keyboard")).toBeTruthy();
    expect(screen.getByText("Split mode")).toBeTruthy();
    expect(screen.getByText("Remote")).toBeTruthy();
    expect(screen.getByText("Appearance")).toBeTruthy();
    expect(screen.getByText("App")).toBeTruthy();
    expect(Array.from(document.querySelectorAll("details")).every((details) => !details.open)).toBe(true);
  });

  it("keeps only one settings accordion open", () => {
    render(<SettingsDrawer {...baseProps} />);

    fireEvent.click(screen.getByText("Trackpad"));
    expect(screen.getByText("Trackpad").closest("details")?.open).toBe(true);

    fireEvent.click(screen.getByText("Keyboard"));
    expect(screen.getByText("Trackpad").closest("details")?.open).toBe(false);
    expect(screen.getByText("Keyboard").closest("details")?.open).toBe(true);
  });

  it("keeps all split controls in the dedicated split mode accordion", () => {
    render(<SettingsDrawer {...baseProps} />);

    const splitSection = screen.getByText("Split mode").closest("details") as HTMLElement;
    fireEvent.click(screen.getByText("Split mode"));
    fireEvent.click(within(splitSection).getByRole("button", { name: "About Split mode" }));

    expect(screen.getByRole("dialog", { name: "Split mode" })).toBeTruthy();
    expect(screen.getByText("Shows the keyboard and trackpad side by side. It is intended mainly for landscape phones and tablets.")).toBeTruthy();

    fireEvent.click(screen.getByRole("button", { name: "OK" }));
    expect(within(splitSection).getByRole("checkbox", { name: "Enable split mode" })).toBeTruthy();

    const trackpadSection = screen.getByText("Trackpad").closest("details") as HTMLElement;
    fireEvent.click(screen.getByText("Trackpad"));
    expect(within(trackpadSection).queryByRole("checkbox", { name: "Enable split mode" })).toBeNull();

    const keyboardSection = screen.getByText("Keyboard").closest("details") as HTMLElement;
    fireEvent.click(screen.getByText("Keyboard"));
    expect(within(keyboardSection).queryByRole("checkbox", { name: "Enable split mode" })).toBeNull();
    expect(screen.queryByRole("button", { name: "About Show function keys" })).toBeNull();
  });

  it("updates the remote navigation ring setting", () => {
    const updateRemoteSetting = vi.fn();
    render(<SettingsDrawer {...baseProps} updateRemoteSetting={updateRemoteSetting} />);

    fireEvent.click(screen.getByText("Remote"));
    fireEvent.click(screen.getByRole("checkbox", { name: "Navigation ring" }));

    expect(updateRemoteSetting).toHaveBeenCalledExactlyOnceWith("navigationRing", false);
  });

  it("updates grouped helper visibility settings", () => {
    const updateRemoteSetting = vi.fn();
    render(<SettingsDrawer {...baseProps} updateRemoteSetting={updateRemoteSetting} />);

    fireEvent.click(screen.getByText("Remote"));
    fireEvent.click(screen.getByRole("checkbox", { name: "Browser tabs and reload" }));

    expect(updateRemoteSetting).toHaveBeenCalledExactlyOnceWith("showBrowserHelpers", false);
  });

  it("updates split mode layout settings", () => {
    const updateTrackpadSetting = vi.fn();
    render(<SettingsDrawer {...baseProps} updateTrackpadSetting={updateTrackpadSetting} />);

    fireEvent.click(screen.getByText("Split mode"));
    fireEvent.click(screen.getByRole("checkbox", { name: "Enable split mode" }));
    fireEvent.click(screen.getByRole("button", { name: "Left" }));
    fireEvent.click(screen.getByRole("checkbox", { name: "Show mode buttons in split mode" }));
    fireEvent.click(screen.getByRole("checkbox", { name: "Show status row in split mode" }));

    expect(updateTrackpadSetting).toHaveBeenNthCalledWith(1, "enableSplitMode", true);
    expect(updateTrackpadSetting).toHaveBeenNthCalledWith(2, "splitTrackpadPlacement", "left");
    expect(updateTrackpadSetting).toHaveBeenNthCalledWith(3, "splitShowModeButtons", true);
    expect(updateTrackpadSetting).toHaveBeenNthCalledWith(4, "splitShowStatusRow", true);
  });

  it("updates haptic feedback when browser vibration is available", () => {
    const updateTrackpadSetting = vi.fn();
    render(<SettingsDrawer {...baseProps} updateTrackpadSetting={updateTrackpadSetting} />);

    fireEvent.click(screen.getByText("Trackpad"));
    fireEvent.click(screen.getByRole("checkbox", { name: "Haptic feedback" }));

    expect(updateTrackpadSetting).toHaveBeenCalledExactlyOnceWith("hapticFeedback", true);
  });

  it("updates the remote mode setting", () => {
    const updateRemoteSetting = vi.fn();
    render(<SettingsDrawer {...baseProps} updateRemoteSetting={updateRemoteSetting} />);

    fireEvent.click(screen.getByText("Remote"));
    fireEvent.click(screen.getByRole("button", { name: "Kodi" }));

    expect(updateRemoteSetting).toHaveBeenCalledExactlyOnceWith("mode", "kodi");
  });


  it("shows launch action settings only when the host allows remote launch", () => {
    const { rerender } = render(<SettingsDrawer {...baseProps} />);

    fireEvent.click(screen.getByText("Remote"));
    expect(screen.queryByRole("checkbox", { name: "Open YouTube from Remote mode" })).toBeNull();
    expect(screen.queryByRole("checkbox", { name: "Start Kodi from Remote mode" })).toBeNull();

    rerender(<SettingsDrawer {...baseProps} supportsRemoteLaunch />);

    expect(screen.getByRole("checkbox", { name: "Open YouTube from Remote mode" })).toBeTruthy();
    expect(screen.getByRole("checkbox", { name: "Start Kodi from Remote mode" })).toBeTruthy();
  });

  it("updates local remote launch action settings", () => {
    const updateRemoteSetting = vi.fn();
    render(<SettingsDrawer {...baseProps} supportsRemoteLaunch updateRemoteSetting={updateRemoteSetting} />);

    fireEvent.click(screen.getByText("Remote"));
    fireEvent.click(screen.getByRole("checkbox", { name: "Open YouTube from Remote mode" }));

    expect(updateRemoteSetting).toHaveBeenCalledExactlyOnceWith("openYoutube", false);
  });

  it("updates the app auto refresh setting", () => {
    const updateAppSetting = vi.fn();
    render(<SettingsDrawer {...baseProps} updateAppSetting={updateAppSetting} />);

    fireEvent.click(screen.getByText("App"));
    fireEvent.click(screen.getByRole("checkbox", { name: "Auto refresh" }));

    expect(updateAppSetting).toHaveBeenCalledExactlyOnceWith("autoRefresh", false);
  });

  it("hides gesture debug unless the host enables it", () => {
    const { rerender } = render(<SettingsDrawer {...baseProps} />);

    fireEvent.click(screen.getByText("Trackpad"));
    expect(screen.queryByRole("button", { name: "Open gesture debug" })).toBeNull();

    rerender(<SettingsDrawer {...baseProps} showGestureDebug />);
    expect(screen.getByRole("button", { name: "Open gesture debug" })).toBeTruthy();
  });
});
