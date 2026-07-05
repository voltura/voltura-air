import { fireEvent, render, screen } from "@testing-library/react";
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
  themeMode: "system" as const,
  trackpadSettings: defaultTrackpadSettings,
  updateAppSetting: vi.fn(),
  updateKeyboardSetting: vi.fn(),
  updateRemoteSetting: vi.fn(),
  updateTrackpadSetting: vi.fn()
};

describe("SettingsDrawer", () => {
  beforeEach(() => {
    vi.stubGlobal("__APP_VERSION__", "0.2.0");
  });

  it("groups settings into folded accordions by default", () => {
    render(<SettingsDrawer {...baseProps} />);

    expect(screen.getByText("Connection")).toBeTruthy();
    expect(screen.getByText("Trackpad")).toBeTruthy();
    expect(screen.getByText("Keyboard")).toBeTruthy();
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

  it("updates the remote navigation ring setting", () => {
    const updateRemoteSetting = vi.fn();
    render(<SettingsDrawer {...baseProps} updateRemoteSetting={updateRemoteSetting} />);

    fireEvent.click(screen.getByText("Remote"));
    fireEvent.click(screen.getByRole("checkbox", { name: "Navigation ring" }));

    expect(updateRemoteSetting).toHaveBeenCalledExactlyOnceWith("navigationRing", false);
  });

  it("updates the remote mode setting", () => {
    const updateRemoteSetting = vi.fn();
    render(<SettingsDrawer {...baseProps} updateRemoteSetting={updateRemoteSetting} />);

    fireEvent.click(screen.getByText("Remote"));
    fireEvent.click(screen.getByRole("button", { name: "Kodi" }));

    expect(updateRemoteSetting).toHaveBeenCalledExactlyOnceWith("mode", "kodi");
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
