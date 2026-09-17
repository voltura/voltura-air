import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { defaultAppSettings } from "../../foundation/settings/appSettings";
import type { StorageProtectionState } from "../../foundation/pwa/usePersistentStorage";
import { AppSettingsSection } from "./AppSettingsSections";

function props() {
  return {
    appSettings: defaultAppSettings,
    keepDeviceScreenOn: false,
    setKeepDeviceScreenOn: vi.fn(),
    screenWakeLockSupported: true,
    protectSavedData: vi.fn(),
    storageProtection: "available" as StorageProtectionState,
    installApp: vi.fn(),
    installPrompt: null as Event | null,
    isInstalled: false,
    presentationAvailable: true,
    filesAvailable: true,
    terminalAvailable: true,
    refreshInstalledApp: vi.fn(),
    refreshMessage: "Reload from the PC if the home screen app looks stale.",
    updateAppSetting: vi.fn(),
  };
}

describe("App settings browser enhancements", () => {
  it("keeps permission requests behind a deliberate action and wires the screen preference", () => {
    const handlers = props();
    const view = render(<AppSettingsSection {...handlers} />);
    expect(handlers.protectSavedData).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole("checkbox", { name: "Keep this device’s screen on" }));
    expect(handlers.setKeepDeviceScreenOn).toHaveBeenCalledWith(true);
    expect(handlers.updateAppSetting).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole("button", { name: "Protect saved data" }));
    expect(handlers.protectSavedData).toHaveBeenCalledOnce();
    view.rerender(<AppSettingsSection {...handlers} screenWakeLockSupported={false} />);
    expect(
      screen.getByRole<HTMLInputElement>("checkbox", { name: "Keep this device’s screen on" })
        .disabled,
    ).toBe(true);
  });

  it.each([
    ["checking", true],
    ["requesting", true],
    ["unavailable", true],
    ["available", false],
    ["declined", false],
    ["failed", false],
  ] as const)("renders actionable feedback for %s", (state, disabled) => {
    render(<AppSettingsSection {...props()} storageProtection={state} />);
    expect(
      screen.getByRole<HTMLButtonElement>("button", {
        name: state === "requesting" ? "Requesting…" : "Protect saved data",
      }).disabled,
    ).toBe(disabled);
    const status = screen.getByRole("status").textContent;
    if (state === "available") {
      expect(status).toBe("");
    } else {
      expect(status?.length).toBeGreaterThan(0);
    }
  });

  it("shows the enabled status without an inert request button", () => {
    render(<AppSettingsSection {...props()} storageProtection="enabled" />);
    expect(screen.queryByRole("button", { name: "Protect saved data" })).toBeNull();
    expect(screen.getByRole("status").textContent).toBe("Browser protection enabled.");
  });

  it("offers both manual guides, preserves native installation, and hides guides when installed", () => {
    const handlers = props();
    const view = render(<AppSettingsSection {...handlers} />);
    const installInfo = screen.getByRole("button", { name: "About Home screen app" });
    fireEvent.click(installInfo);
    expect(screen.getByRole("dialog", { name: "Home screen app" })).toBeTruthy();
    expect(screen.getByText(/Open as Web App/)).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "OK" }));
    expect(screen.queryByRole("dialog")).toBeNull();
    view.rerender(
      <AppSettingsSection {...handlers} installPrompt={new Event("beforeinstallprompt")} />,
    );
    expect(screen.getByRole("button", { name: "About Home screen app" })).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Install app" }));
    expect(handlers.installApp).toHaveBeenCalledOnce();
    view.rerender(<AppSettingsSection {...handlers} isInstalled />);
    expect(screen.queryByText("Home screen app")).toBeNull();
    expect(screen.queryByRole("button", { name: "About Home screen app" })).toBeNull();
    expect(screen.getByRole("button", { name: "Protect saved data" })).toBeTruthy();
  });

  it("opens the explanations with both close actions", () => {
    render(<AppSettingsSection {...props()} />);
    fireEvent.click(screen.getByRole("button", { name: "About Keep this device’s screen on" }));
    expect(screen.getByRole("dialog", { name: "Keep this device’s screen on" })).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Close Keep this device’s screen on" }));
    expect(screen.queryByRole("dialog")).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "About Keep pairing and settings" }));
    expect(screen.getByRole("dialog", { name: "Keep pairing and settings" })).toBeTruthy();
    expect(screen.getByText(/clearing website data still removes them/)).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "OK" }));
    expect(screen.queryByRole("dialog")).toBeNull();
  });
});
