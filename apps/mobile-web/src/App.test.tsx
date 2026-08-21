import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { App } from "./App";
import { useVolturaAirConnection } from "./foundation/connection/useVolturaAirConnection";
import { usePwaLifecycle } from "./foundation/pwa/usePwaLifecycle";

vi.mock("./foundation/connection/useVolturaAirConnection", () => ({
  useVolturaAirConnection: vi.fn()
}));

vi.mock("./foundation/pwa/usePwaLifecycle", () => ({
  usePwaLifecycle: vi.fn()
}));

function createStorage(): Storage {
  const items = new Map<string, string>();
  return {
    get length() {
      return items.size;
    },
    clear: () => { items.clear(); },
    getItem: (key: string) => items.get(key) ?? null,
    key: (index: number) => Array.from(items.keys())[index] ?? null,
    removeItem: (key: string) => {
      items.delete(key);
    },
    setItem: (key: string, value: string) => {
      items.set(key, String(value));
    }
  };
}

function readStoredStringProperty(key: string, property: string): string[] {
  const parsed: unknown = JSON.parse(localStorage.getItem(key) ?? "[]");
  if (!Array.isArray(parsed)) {
    return [];
  }

  return (parsed as unknown[]).flatMap((candidate) => {
    if (typeof candidate !== "object" || candidate === null) {
      return [];
    }

    const value = (candidate as Record<string, unknown>)[property];
    return typeof value === "string" ? [value] : [];
  });
}

function mockConnection(overrides: Partial<ReturnType<typeof useVolturaAirConnection>> = {}) {
  vi.mocked(useVolturaAirConnection).mockReturnValue({
    state: "paired",
    connectionEpoch: 1,
    message: "Connected to Very Long Living Room Editing Workstation",
    send: vi.fn(),
    screenViewCapability: undefined,
    phoneWebcamCapability: undefined,
    fileManagerCapability: undefined,
    requestAudioState: vi.fn(),
    clientId: "client-a",
    deviceName: "Phone",
    activePc: {
      customName: true,
      id: "pc-a",
      name: "Very Long Living Room Editing Workstation",
      url: "http://pc.local:51395"
    },
    pairedPcs: [],
    reconnectablePcs: [],
    audioState: null,
    awakeCapability: null,
    awakeResult: null,
    pendingAwakeChange: null,
    requestAwakeChange: vi.fn(),
    powerCapabilities: null,
    pendingPowerAction: null,
    powerActionResult: null,
    requestPowerAction: vi.fn(),
    appLaunchResult: null,
    pendingAppLaunchId: null,
    requestAppLaunch: vi.fn(),
    presentationCapability: { canControl: true, canSaveReports: true, laserPointerActive: false },
    pendingPresentationCommand: null,
    pendingPresentationSession: null,
    pendingPowerPointRefresh: null,
    pendingPowerPointLaunch: null,
    presentationResult: null,
    presentationSessionResult: null,
    powerPointRefreshResult: null,
    powerPointLaunchResult: null,
    requestPresentationCommand: vi.fn(() => "presentation-operation-a"),
    requestPowerPointRefresh: vi.fn(() => "presentation-refresh-a"),
    requestPowerPointLaunch: vi.fn(() => "presentation-launch-a"),
    requestPresentationSession: vi.fn(() => "presentation-session-a"),
    pendingPresentationReportSave: null,
    presentationReportSaveResult: null,
    requestPresentationReportSave: vi.fn(() => "presentation-report-operation-a"),
    pendingUrlOpen: false,
    urlOpenResult: null,
    urlOpenCapability: { canOpen: true },
    requestUrlOpen: vi.fn(() => "url-operation-a"),
    pendingTextTransfer: false,
    requestTextTransfer: vi.fn(() => "operation-a"),
    requestClipboardRead: vi.fn(() => "clipboard-operation-a"),
    requestClipboardReadForDevice: vi.fn(() => null),
    cancelClipboardReadForDevice: vi.fn(),
    customScreensCapability: undefined,
    customScreenDefinition: null,
    customScreenGetResult: null,
    customScreenInvokeResult: null,
    pendingCustomScreenButtonIds: new Set<string>(),
    requestCustomScreen: vi.fn(),
    invokeCustomScreenButton: vi.fn(),
    textTransferResult: null,
    clipboardReadResult: null,
    clipboardText: "",
    setClipboardText: vi.fn(),
    clipboardReadPermission: undefined,
    pendingClipboardRead: false,
    supportsGestureDebug: false,
    supportsSleep: true,
    supportsVolumeControl: true,
    supportsRemoteLaunch: false,
    supportsTextTransfer: true,
    lastConnectionError: null,
    hostStatus: null,
    pairWithToken: vi.fn(),
    selectPc: vi.fn(),
    beginNewPairing: vi.fn(),
    connectManualPc: vi.fn(),
    addManualPc: vi.fn(),
    disconnectActivePc: vi.fn(),
    forgetPc: vi.fn(),
    renamePc: vi.fn(),
    renameDevice: vi.fn(),
    setHostCustomPointer: vi.fn(),
    setHostControlDepth: vi.fn(),
    setHostShowModeButtons: vi.fn(),
    setHostPointerSpeed: vi.fn(),
    ...overrides
  });
}

beforeEach(() => {
  vi.stubGlobal("localStorage", createStorage());
  vi.stubGlobal("sessionStorage", createStorage());
  vi.stubGlobal("__APP_VERSION__", "test");
  vi.stubGlobal(
    "matchMedia",
    vi.fn(() => ({
      matches: false,
      addEventListener: vi.fn(),
      removeEventListener: vi.fn()
    }))
  );
  mockConnection();
  vi.mocked(usePwaLifecycle).mockReturnValue({
    installApp: vi.fn(),
    installPrompt: null,
    isInstalled: false,
    refreshInstalledApp: vi.fn(),
    refreshMessage: "Reload from the PC if the home screen app looks stale."
  });
});

describe("App header and mode navigation", () => {
  it("keeps connection errors short in the header and presents the complete dismissible error once per occurrence", async () => {
    const error = {
      code: "VAIR-PAIR-HOST-PROOF-INVALID",
      message: "PC identity check failed. Scan a fresh QR code from the PC."
    };
    mockConnection({ lastConnectionError: error });
    const view = render(<App />);

    expect(document.querySelector(".status")?.getAttribute("title")).toBe("Connection issue");
    expect(document.querySelector(".status")?.classList.contains("error")).toBe(true);
    const dialog = screen.getByRole("dialog", { name: "Connection issue" });
    expect(dialog.textContent).toContain(error.message);
    expect(dialog.textContent).toContain(`Diagnostic code: ${error.code}`);

    fireEvent.click(within(dialog).getByRole("button", { name: "OK" }));
    expect(screen.queryByRole("dialog", { name: "Connection issue" })).toBeNull();

    window.dispatchEvent(new Event("resize"));
    view.rerender(<App />);
    expect(screen.queryByRole("dialog", { name: "Connection issue" })).toBeNull();

    mockConnection({ lastConnectionError: null });
    view.rerender(<App />);
    mockConnection({ lastConnectionError: error });
    view.rerender(<App />);
    await waitFor(() => {
      expect(screen.getByRole("dialog", { name: "Connection issue" })).not.toBeNull();
    });
  });

  it("keeps reconnect errors in the blocking recovery panel when the host becomes unavailable", () => {
    const firstError = {
      code: "VAIR-PAIR-HOST-UNREACHABLE",
      message: "HTPC-BEE is currently not available. Check that Voltura Air is running on the PC. Retrying..."
    };
    mockConnection({
      state: "unavailable",
      message: firstError.message,
      lastConnectionError: firstError,
      activePc: {
        customName: true,
        id: "pc-a",
        name: "HTPC-BEE",
        transportMode: "secure-direct",
        url: "https://secure-direct.invalid"
      }
    });
    const view = render(<App />);

    expect(screen.queryByRole("dialog", { name: "Connection issue" })).toBeNull();
    expect(screen.getByRole("dialog", { name: "Secure Direct unavailable" })).not.toBeNull();
    expect(screen.getByRole("button", { name: "Try reconnect" })).not.toBeNull();
    expect(screen.getByRole("button", { name: "Take photo of new QR code" })).not.toBeNull();
    expect(screen.getByRole("button", { name: "Enter host manually" })).not.toBeNull();

    const nextError = {
      code: "VAIR-PAIR-SECURE-DIRECT",
      message: "Could not establish the secure direct connection."
    };
    mockConnection({
      state: "unavailable",
      message: nextError.message,
      lastConnectionError: nextError,
      activePc: {
        customName: true,
        id: "pc-a",
        name: "HTPC-BEE",
        transportMode: "secure-direct",
        url: "https://secure-direct.invalid"
      }
    });
    view.rerender(<App />);

    expect(screen.queryByRole("dialog", { name: "Connection issue" })).toBeNull();
    expect(screen.getByRole("dialog", { name: "Secure Direct unavailable" })).not.toBeNull();
  });

  it("uses the effective device control-depth preference on the app shell and bottom navigation frame", () => {
    mockConnection({ hostStatus: { controlDepth: false } });
    const view = render(<App />);
    expect(document.querySelector(".app-shell")?.classList).not.toContain("control-depth");
    expect(document.querySelector(".app-frame")?.classList).not.toContain("control-depth");

    mockConnection({ hostStatus: { controlDepth: true } });
    view.rerender(<App />);
    expect(document.querySelector(".app-shell")?.classList).toContain("control-depth");
    expect(document.querySelector(".app-frame")?.classList).toContain("control-depth");
  });

  it("clears stale owned laser state when the app starts outside Presentation", async () => {
    const requestPresentationCommand = vi.fn(() => "laser-cleanup");
    mockConnection({
      presentationCapability: { canControl: true, canSaveReports: true, laserPointerActive: true },
      requestPresentationCommand
    });

    render(<App />);

    await waitFor(() => {
      expect(requestPresentationCommand).toHaveBeenCalledWith("powerpoint", "pointer", false);
    });
  });

  it("never activates PowerPoint when Trackpad reconnects after an earlier Presentation visit", async () => {
    const requestPresentationCommand = vi.fn(() => "presentation-operation-a");
    const readyPowerPointCapability = {
      canControl: true,
      canSaveReports: true,
      laserPointerActive: false,
      powerPoint: {
        state: "ready" as const,
        foregroundActivationSupported: true,
        presentations: [{
          runtimePresentationId: "presentation-1",
          name: "Quarterly update.pptx",
          state: "ready" as const,
          slideCount: 24,
          currentSlideIndex: null,
          currentShowPosition: null,
          slideShowState: "ready" as const
        }]
      }
    };
    mockConnection({
      presentationCapability: readyPowerPointCapability,
      requestPresentationCommand
    });
    const view = render(<App />);

    expect(document.querySelector(".app-shell")?.classList).toContain("trackpad-active");
    expect(requestPresentationCommand).not.toHaveBeenCalled();

    fireEvent.click(screen.getAllByRole("button", { name: "Presentation" }).at(-1)!);
    await waitFor(() => {
      expect(requestPresentationCommand).toHaveBeenCalledExactlyOnceWith(
        "powerpoint",
        "activate",
        { runtimePresentationId: "presentation-1" });
    });

    fireEvent.click(screen.getAllByRole("button", { name: "Trackpad" }).at(-1)!);
    expect(document.querySelector(".app-shell")?.classList).toContain("trackpad-active");

    mockConnection({
      state: "unavailable",
      connectionEpoch: 1,
      message: "PC is currently not available. Retrying...",
      presentationCapability: undefined,
      requestPresentationCommand
    });
    view.rerender(<App />);
    mockConnection({
      connectionEpoch: 2,
      presentationCapability: readyPowerPointCapability,
      requestPresentationCommand
    });
    view.rerender(<App />);

    await waitFor(() => {
      expect(document.querySelector(".app-shell")?.classList).toContain("trackpad-active");
    });
    expect(requestPresentationCommand).toHaveBeenCalledTimes(1);
  });

  it("refreshes after a developer-mode long press on the Voltura Air brand", async () => {
    vi.useFakeTimers();
    const refreshInstalledApp = vi.fn();
    vi.mocked(usePwaLifecycle).mockReturnValue({
      installApp: vi.fn(),
      installPrompt: null,
      isInstalled: false,
      refreshInstalledApp,
      refreshMessage: "Reload from the PC if the home screen app looks stale."
    });
    mockConnection({ hostStatus: { developerMode: true } });
    render(<App />);

    const brand = screen.getByText("Voltura Air").parentElement!;
    fireEvent.pointerDown(brand, { button: 0, clientX: 20, clientY: 20, pointerId: 1 });
    await act(() => vi.advanceTimersByTime(699));
    expect(refreshInstalledApp).not.toHaveBeenCalled();
    await act(() => vi.advanceTimersByTime(1));
    expect(refreshInstalledApp).toHaveBeenCalledTimes(1);
    fireEvent.pointerUp(brand, { pointerId: 1 });
    vi.useRealTimers();
  });

  it("opens Presentation as a selectable mode and sends its primary action through the acknowledged command path", () => {
    const requestPresentationCommand = vi.fn(() => "presentation-operation-a");
    mockConnection({ requestPresentationCommand });
    render(<App />);

    fireEvent.click(screen.getByRole("button", { name: "Open menu" }));
    const menu = screen.getByRole("heading", { name: "Menu" }).closest("dialog");
    fireEvent.click(within(menu!).getByRole("button", { name: "Presentation" }));
    fireEvent.click(screen.getByRole("button", { name: "Next" }));

    expect(screen.getByRole("heading", { name: "Presentation" })).toBeTruthy();
    expect(requestPresentationCommand).toHaveBeenCalledExactlyOnceWith("powerpoint", "next");
    expect(screen.getAllByRole("button", { name: "Trackpad" })).not.toHaveLength(0);
  });

  it("hides Presentation entry points when the host does not advertise the capability", () => {
    mockConnection({ presentationCapability: undefined });
    render(<App />);

    expect(screen.queryByRole("button", { name: "Presentation" })).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "Open menu" }));
    expect(screen.queryByRole("button", { name: "Presentation" })).toBeNull();
    fireEvent.click(screen.getByText("App"));
    expect(screen.queryByRole("option", { name: "Presentation" })).toBeNull();
  });

  it("leaves Presentation immediately when the host removes the capability", async () => {
    const { rerender } = render(<App />);
    fireEvent.click(screen.getByRole("button", { name: "Open menu" }));
    const menu = screen.getByRole("heading", { name: "Menu" }).closest("dialog");
    fireEvent.click(within(menu!).getByRole("button", { name: "Presentation" }));
    expect(screen.getByRole("heading", { name: "Presentation" })).toBeTruthy();

    mockConnection({ presentationCapability: undefined });
    rerender(<App />);

    await waitFor(() => { expect(screen.queryByRole("heading", { name: "Presentation" })).toBeNull(); });
    expect(screen.getByLabelText("Dictation text")).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Presentation" })).toBeNull();
  });

  it("offers desktop recovery while an administrator app blocks remote input", async () => {
    const send = vi.fn();
    mockConnection({ hostStatus: { inputBlockedByElevation: true }, send });

    const { rerender } = render(<App />);

    expect(screen.getByRole("dialog").textContent).toContain("Administrator app active");
    fireEvent.click(screen.getByRole("button", { name: "Show desktop" }));
    expect(send).toHaveBeenCalledWith({ type: "keyboard.special", key: "D", modifiers: ["Win"] });

    fireEvent.click(screen.getByRole("button", { name: "Continue" }));
    expect(screen.queryByRole("dialog")).toBeNull();
    expect(screen.getByRole("button", { name: "PC input paused. Open recovery options." })).toBeTruthy();

    fireEvent.click(screen.getByRole("button", { name: "PC input paused. Open recovery options." }));
    expect(screen.getByRole("dialog").textContent).toContain("Other controls remain available.");

    mockConnection({ hostStatus: { inputBlockedByElevation: false }, send });
    rerender(<App />);
    mockConnection({ hostStatus: { inputBlockedByElevation: true }, send });
    rerender(<App />);

    await waitFor(() => { expect(screen.getByRole("dialog").textContent).toContain("Administrator app active"); });
  });

  it("uses accessible mode labels and selected state in both navigation surfaces", () => {
    render(<App />);

    const keyboardModeButtons = screen.getAllByRole("button", { name: "Keyboard" });
    expect(keyboardModeButtons).toHaveLength(2);
    expect(keyboardModeButtons.every((button) => button.getAttribute("aria-current") === null)).toBe(true);

    const trackpadModeButtons = screen.getAllByRole("button", { name: "Trackpad" });
    expect(trackpadModeButtons.some((button) => button.getAttribute("aria-current") === "page")).toBe(true);
  });

  it("opens either tool from Menu without changing the configured fourth mode", () => {
    render(<App />);

    fireEvent.click(screen.getByRole("button", { name: "Open menu" }));
    expect(screen.getByRole("heading", { name: "Menu" })).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Send text to PC" }));

    expect(screen.getByRole("heading", { name: "Send text to PC" })).toBeTruthy();
    expect(screen.getAllByRole("button", { name: "Presentation" })).toHaveLength(2);
    expect(screen.queryByRole("button", { name: "Back to previous mode" })).toBeNull();
    fireEvent.click(screen.getAllByRole("button", { name: "Trackpad" }).at(-1)!);
    expect(screen.getAllByRole("button", { name: "Trackpad" }).some((button) => button.getAttribute("aria-current") === "page")).toBe(true);

    fireEvent.click(screen.getByRole("button", { name: "Open menu" }));
    const menu = screen.getByRole("heading", { name: "Menu" }).closest("dialog");
    expect(menu).not.toBeNull();
    fireEvent.click(within(menu!).getByRole("button", { name: "Dictation" }));
    expect(screen.getByLabelText("Dictation text")).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Back to previous mode" })).toBeNull();
  });

  it("leaves a custom screen when a standard mode is selected from Menu", () => {
    mockConnection({
      customScreensCapability: {
        catalogRevision: "catalog.one",
        screens: [{
          id: "screen.media",
          name: "Media controls",
          revision: "revision.one"
        }]
      },
      customScreenDefinition: {
        id: "screen.media",
        name: "Media controls",
        revision: "revision.one",
        orientationLayoutsEnabled: false,
        showNavigationHeader: true,
        sections: []
      }
    });
    render(<App />);

    fireEvent.click(screen.getByRole("button", { name: "Open menu" }));
    let menu = screen.getByRole("heading", { name: "Menu" }).closest("dialog");
    fireEvent.click(within(menu!).getByRole("button", { name: "Media controls" }));
    expect(screen.getByRole("heading", { name: "Media controls" })).toBeTruthy();

    fireEvent.click(screen.getByRole("button", { name: "Open menu" }));
    menu = screen.getByRole("heading", { name: "Menu" }).closest("dialog");
    fireEvent.click(within(menu!).getByRole("button", { name: "Dictation" }));

    expect(screen.queryByRole("heading", { name: "Media controls" })).toBeNull();
    expect(screen.getByLabelText("Dictation text")).toBeTruthy();
  });

  it("shows a dismissible error dialog when a custom-screen definition is incompatible", () => {
    mockConnection({
      customScreensCapability: {
        catalogRevision: "catalog.one",
        screens: [{
          id: "screen.gyro",
          name: "Gyro controls",
          revision: "revision.one"
        }]
      },
      customScreenGetResult: {
        type: "custom.screen.get.result",
        operationId: "operation-incompatible",
        succeeded: false,
        code: "VAIR-CUSTOM-SCREEN-INCOMPATIBLE",
        message: "This custom screen uses features this version of the app cannot display. Refresh the app and try again."
      }
    });
    render(<App />);

    fireEvent.click(screen.getByRole("button", { name: "Open menu" }));
    const menu = screen.getByRole("heading", { name: "Menu" }).closest("dialog");
    fireEvent.click(within(menu!).getByRole("button", { name: "Gyro controls" }));

    const dialog = screen.getByRole("dialog", { name: "Custom screen unavailable" });
    expect(dialog.textContent).toContain("Refresh the app and try again.");
    expect(dialog.textContent).toContain("Diagnostic code: VAIR-CUSTOM-SCREEN-INCOMPATIBLE");
    fireEvent.click(within(dialog).getByRole("button", { name: "OK" }));
    expect(screen.queryByRole("dialog", { name: "Custom screen unavailable" })).toBeNull();
    expect(screen.getByRole("alert").textContent).toContain("Refresh the app and try again.");
  });

  it("shows reconnect actions instead of a custom-screen error after host communication is lost", () => {
    const connection = {
      customScreensCapability: {
        catalogRevision: "catalog.one",
        screens: [{
          id: "screen.gyro",
          name: "Gyro controls",
          revision: "revision.one"
        }]
      },
      customScreenGetResult: {
        type: "custom.screen.get.result" as const,
        operationId: "operation-incompatible",
        succeeded: false,
        code: "VAIR-CUSTOM-SCREEN-INCOMPATIBLE",
        message: "This custom screen uses features this version of the app cannot display. Refresh the app and try again."
      }
    };
    mockConnection(connection);
    const view = render(<App />);

    fireEvent.click(screen.getByRole("button", { name: "Open menu" }));
    const menu = screen.getByRole("heading", { name: "Menu" }).closest("dialog");
    fireEvent.click(within(menu!).getByRole("button", { name: "Gyro controls" }));
    expect(screen.getByRole("dialog", { name: "Custom screen unavailable" })).not.toBeNull();

    mockConnection({
      ...connection,
      state: "unavailable",
      message: "HTPC-BEE is currently not available.",
      lastConnectionError: {
        code: "VAIR-PAIR-HOST-UNREACHABLE",
        message: "HTPC-BEE is currently not available. Check that Voltura Air is running on the PC. Retrying..."
      },
      activePc: {
        customName: true,
        id: "pc-a",
        name: "HTPC-BEE",
        transportMode: "secure-direct",
        url: "https://secure-direct.invalid"
      }
    });
    view.rerender(<App />);

    expect(screen.queryByRole("dialog", { name: "Custom screen unavailable" })).toBeNull();
    expect(screen.getByRole("dialog", { name: "Secure Direct unavailable" })).not.toBeNull();
    expect(screen.getByRole("button", { name: "Try reconnect" })).not.toBeNull();
  });

  it("hides mode-button rows on a custom screen and leaves through the compact selector", () => {
    mockConnection({
      customScreensCapability: {
        catalogRevision: "catalog.one",
        screens: [{
          id: "screen.media",
          name: "Media controls",
          revision: "revision.one"
        }]
      },
      customScreenDefinition: {
        id: "screen.media",
        name: "Media controls",
        revision: "revision.one",
        orientationLayoutsEnabled: false,
        showNavigationHeader: true,
        sections: []
      }
    });
    render(<App />);

    const openCustomScreen = () => {
      fireEvent.click(screen.getByRole("button", { name: "Open menu" }));
      const menu = screen.getByRole("heading", { name: "Menu" }).closest("dialog");
      fireEvent.click(within(menu!).getByRole("button", { name: "Media controls" }));
      expect(screen.getByRole("heading", { name: "Media controls" })).toBeTruthy();
    };

    openCustomScreen();
    expect(document.querySelector(".top-mode-tabs")).toBeNull();
    expect(document.querySelector(".bottom-mode-tabs")).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "Change mode" }));
    fireEvent.click(screen.getByRole("menuitemradio", { name: "Trackpad" }));
    expect(screen.queryByRole("heading", { name: "Media controls" })).toBeNull();
    expect(document.querySelector(".trackpad-mode")).not.toBeNull();
    expect(document.querySelector(".top-mode-tabs")).not.toBeNull();
    expect(document.querySelector(".bottom-mode-tabs")).not.toBeNull();
  });

  it("treats Screen as a separate workspace with only the compact mode selector", async () => {
    mockConnection({
      screenViewCapability: {
        enabled: true,
        permissionGranted: true,
        canView: true,
        requiresRepair: false,
        encrypted: true,
        maxWidth: 1920,
        maxHeight: 1080,
        maxFramesPerSecond: 30
      }
    });
    render(<App />);

    fireEvent.click(screen.getByRole("button", { name: "Open menu" }));
    const menu = screen.getByRole("heading", { name: "Menu" }).closest("dialog");
    fireEvent.click(within(menu!).getByRole("button", { name: "View PC screen" }));

    expect(await screen.findByText("Live mirror")).toBeTruthy();
    expect(document.querySelector(".app-shell")?.classList).toContain("screen-view-active");
    expect(document.querySelector(".top-mode-tabs")).toBeNull();
    expect(document.querySelector(".bottom-mode-tabs")).toBeNull();
    expect(screen.getByRole("button", { name: "Change mode" })).toBeTruthy();

    fireEvent.click(screen.getByRole("button", { name: "Change mode" }));
    fireEvent.click(screen.getByRole("menuitemradio", { name: "Keyboard" }));
    expect(screen.queryByText("Live mirror")).toBeNull();
    expect(document.querySelector(".app-shell")?.classList).not.toContain("screen-view-active");
    expect(document.querySelector(".bottom-mode-tabs")).not.toBeNull();
  });

  it.each([
    { label: "Enhanced Direct", transportMode: "secure-direct" as const, url: "https://secure-direct.invalid" },
    { label: "Relay", transportMode: "relay" as const, url: "https://voltura.se/a" }
  ])("opens Phone webcam as a normal app tool over $label", async ({ transportMode, url }) => {
    mockConnection({
      activePc: {
        customName: true,
        id: "pc-a",
        name: "Webcam PC",
        transportMode,
        url
      },
      phoneWebcamCapability: {
        enabled: true,
        permissionGranted: true,
        canUse: true,
        requiresRepair: false,
        microphoneAvailable: false,
        maxWidth: 1920,
        maxHeight: 1080,
        maxFramesPerSecond: 30
      }
    });
    render(<App />);

    fireEvent.click(screen.getByRole("button", { name: "Open menu" }));
    const menu = screen.getByRole("heading", { name: "Menu" }).closest("dialog")!;
    fireEvent.click(within(menu).getByRole("button", { name: "Phone webcam" }));

    expect(await screen.findByRole("heading", { name: "Phone webcam" })).toBeTruthy();
    expect(screen.getByRole("button", { name: "Allow camera access" })).toBeTruthy();
    expect(screen.queryByRole("dialog", { name: "Menu" })).toBeNull();
    expect(document.querySelector(".top-mode-tabs")).toBeNull();
    expect(document.querySelector(".bottom-mode-tabs")).toBeNull();
  });

  it("refreshes the open Phone webcam workspace from live host capability changes", async () => {
    const available = {
      enabled: true,
      permissionGranted: true,
      canUse: true,
      requiresRepair: false,
      microphoneAvailable: false,
      maxWidth: 1920,
      maxHeight: 1080,
      maxFramesPerSecond: 30
    };
    const activePc = { customName: true, id: "pc-a", name: "Webcam PC", transportMode: "secure-direct" as const, url: "https://secure-direct.invalid" };
    mockConnection({ activePc, phoneWebcamCapability: available });
    const rendered = render(<App />);
    fireEvent.click(screen.getByRole("button", { name: "Open menu" }));
    fireEvent.click(within(screen.getByRole("heading", { name: "Menu" }).closest("dialog")!).getByRole("button", { name: "Phone webcam" }));
    expect(await screen.findByRole("heading", { name: "Phone webcam" })).toBeTruthy();

    mockConnection({ activePc, phoneWebcamCapability: { ...available, enabled: false, canUse: false } });
    rendered.rerender(<App />);

    expect((screen.getByRole("button", { name: "Allow camera access" }) as HTMLButtonElement).disabled).toBe(true);
    expect(screen.getByText("Enable Phone webcam in the Windows app first.")).toBeTruthy();
  });

  it("keeps Phone webcam visible instead of showing the generic disconnected takeover", async () => {
    const capability = {
      enabled: true,
      permissionGranted: true,
      canUse: true,
      requiresRepair: false,
      microphoneAvailable: false,
      maxWidth: 1920,
      maxHeight: 1080,
      maxFramesPerSecond: 30
    };
    const activePc = { customName: true, id: "pc-a", name: "Webcam PC", transportMode: "secure-direct" as const, url: "https://secure-direct.invalid" };
    mockConnection({ activePc, phoneWebcamCapability: capability });
    const rendered = render(<App />);
    fireEvent.click(screen.getByRole("button", { name: "Open menu" }));
    fireEvent.click(within(screen.getByRole("heading", { name: "Menu" }).closest("dialog")!).getByRole("button", { name: "Phone webcam" }));
    expect(await screen.findByRole("heading", { name: "Phone webcam" })).toBeTruthy();

    mockConnection({ activePc, phoneWebcamCapability: capability, state: "disconnected", message: "PC disconnected" });
    rendered.rerender(<App />);

    expect(screen.getByRole("heading", { name: "Phone webcam" })).toBeTruthy();
    expect(document.querySelector(".pairing-status.blocking")).toBeNull();
  });

  it("does not offer Phone webcam over Standard Local HTTP", () => {
    mockConnection({
      phoneWebcamCapability: {
        enabled: true,
        permissionGranted: true,
        canUse: true,
        requiresRepair: false,
        microphoneAvailable: false,
        maxWidth: 1920,
        maxHeight: 1080,
        maxFramesPerSecond: 30
      }
    });
    render(<App />);

    fireEvent.click(screen.getByRole("button", { name: "Open menu" }));
    const menu = screen.getByRole("heading", { name: "Menu" }).closest("dialog")!;
    expect(within(menu).queryByRole("button", { name: "Phone webcam" })).toBeNull();
  });

  it("leaves third-party notices through the compact mode selector", () => {
    render(<App />);

    fireEvent.click(screen.getByRole("button", { name: "Open menu" }));
    const menu = screen.getByRole("heading", { name: "Menu" }).closest("dialog")!;
    fireEvent.click(within(menu).getByRole("button", { name: "Third-party notices" }));
    expect(screen.getByRole("heading", { name: "Third-party notices" })).toBeTruthy();

    fireEvent.click(screen.getByRole("button", { name: "Change mode" }));
    fireEvent.click(screen.getByRole("menuitemradio", { name: "Keyboard" }));

    expect(screen.queryByRole("heading", { name: "Third-party notices" })).toBeNull();
    expect(document.querySelector(".keyboard-mode")).not.toBeNull();
  });

  async function expectSettingsToolToLeaveScreen(modeName: "Keyboard" | "Trackpad") {
    mockConnection({
      screenViewCapability: {
        enabled: true,
        permissionGranted: true,
        canView: true,
        requiresRepair: false,
        encrypted: true,
        maxWidth: 1920,
        maxHeight: 1080,
        maxFramesPerSecond: 30
      }
    });
    render(<App />);

    fireEvent.click(screen.getByRole("button", { name: "Open menu" }));
    let menu = screen.getByRole("heading", { name: "Menu" }).closest("dialog")!;
    fireEvent.click(within(menu).getByRole("button", { name: "View PC screen" }));
    expect(await screen.findByText("Live mirror")).toBeTruthy();

    fireEvent.click(screen.getByRole("button", { name: "Open menu" }));
    menu = screen.getByRole("heading", { name: "Menu" }).closest("dialog")!;
    const tools = within(menu).getByRole("region", { name: "Tools" });
    const modeButton = Array.from(tools.querySelectorAll("button"))
      .find((button) => button.textContent?.trim() === modeName);
    expect(modeButton).toBeDefined();
    fireEvent.click(modeButton!);

    expect(screen.queryByText("Live mirror")).toBeNull();
    expect(document.querySelector(".app-shell")?.classList).not.toContain("screen-view-active");
  }

  it("leaves Screen when opening Keyboard from the Settings drawer", async () => {
    await expectSettingsToolToLeaveScreen("Keyboard");
  });

  it("leaves Screen when opening Trackpad from the Settings drawer", async () => {
    await expectSettingsToolToLeaveScreen("Trackpad");
  });

  it("requests Gyro from Tools and replaces Screen with the Trackpad in Gyro mode", async () => {
    const motionPermission = vi.fn(() => Promise.resolve("granted" as const));
    const orientationPermission = vi.fn(() => Promise.resolve("granted" as const));
    Object.defineProperty(window, "isSecureContext", { configurable: true, value: true });
    vi.stubGlobal("DeviceMotionEvent", { requestPermission: motionPermission });
    vi.stubGlobal("DeviceOrientationEvent", { requestPermission: orientationPermission });
    mockConnection({
      screenViewCapability: {
        enabled: true,
        permissionGranted: true,
        canView: true,
        requiresRepair: false,
        encrypted: true,
        maxWidth: 1920,
        maxHeight: 1080,
        maxFramesPerSecond: 30
      }
    });
    render(<App />);

    fireEvent.click(screen.getByRole("button", { name: "Open menu" }));
    let menu = screen.getByRole("heading", { name: "Menu" }).closest("dialog")!;
    fireEvent.click(within(menu).getByRole("button", { name: "View PC screen" }));
    expect(await screen.findByText("Live mirror")).toBeTruthy();

    fireEvent.click(screen.getByRole("button", { name: "Open menu" }));
    menu = screen.getByRole("heading", { name: "Menu" }).closest("dialog")!;
    fireEvent.click(within(menu).getByRole("button", { name: "Gyro mouse" }));

    expect(motionPermission).toHaveBeenCalledOnce();
    expect(orientationPermission).toHaveBeenCalledOnce();
    expect(screen.queryByText("Live mirror")).toBeNull();
    await waitFor(() => { expect(screen.getByRole("button", { name: "Gyro" }).getAttribute("aria-pressed")).toBe("true"); });
  });

  it("navigates from Screen to a custom screen selected in the Settings drawer", async () => {
    mockConnection({
      screenViewCapability: {
        enabled: true,
        permissionGranted: true,
        canView: true,
        requiresRepair: false,
        encrypted: true,
        maxWidth: 1920,
        maxHeight: 1080,
        maxFramesPerSecond: 30
      },
      customScreensCapability: {
        catalogRevision: "catalog.one",
        screens: [{
          id: "screen.media",
          name: "Media controls",
          revision: "revision.one"
        }]
      },
      customScreenDefinition: {
        id: "screen.media",
        name: "Media controls",
        revision: "revision.one",
        orientationLayoutsEnabled: false,
        showNavigationHeader: true,
        sections: []
      }
    });
    render(<App />);

    fireEvent.click(screen.getByRole("button", { name: "Open menu" }));
    let menu = screen.getByRole("heading", { name: "Menu" }).closest("dialog")!;
    fireEvent.click(within(menu).getByRole("button", { name: "View PC screen" }));
    expect(await screen.findByText("Live mirror")).toBeTruthy();

    fireEvent.click(screen.getByRole("button", { name: "Open menu" }));
    menu = screen.getByRole("heading", { name: "Menu" }).closest("dialog")!;
    fireEvent.click(within(menu).getByRole("button", { name: "Media controls" }));

    expect(screen.queryByText("Live mirror")).toBeNull();
    expect(screen.getByRole("heading", { name: "Media controls" })).toBeTruthy();
    expect(document.querySelector(".app-shell")?.classList).not.toContain("screen-view-active");
  });

  it.each(["YouTube", "Kodi"])("navigates from Screen to the %s Remote mode selected in settings", async (modeName) => {
    mockConnection({
      screenViewCapability: {
        enabled: true,
        permissionGranted: true,
        canView: true,
        requiresRepair: false,
        encrypted: true,
        maxWidth: 1920,
        maxHeight: 1080,
        maxFramesPerSecond: 30
      },
      supportsRemoteLaunch: true
    });
    render(<App />);

    fireEvent.click(screen.getByRole("button", { name: "Open menu" }));
    let menu = screen.getByRole("heading", { name: "Menu" }).closest("dialog")!;
    fireEvent.click(within(menu).getByRole("button", { name: "View PC screen" }));
    expect(await screen.findByText("Live mirror")).toBeTruthy();

    fireEvent.click(screen.getByRole("button", { name: "Open menu" }));
    menu = screen.getByRole("heading", { name: "Menu" }).closest("dialog")!;
    const remoteSettingsSummary = menu.querySelector<HTMLElement>("[data-settings-section=\"remote\"] > summary");
    expect(remoteSettingsSummary).not.toBeNull();
    fireEvent.click(remoteSettingsSummary!);
    fireEvent.click(within(menu).getByRole("button", { name: modeName }));

    expect(screen.queryByText("Live mirror")).toBeNull();
    expect(document.querySelector(".app-shell")?.classList).not.toContain("screen-view-active");
    expect(document.querySelector(".app-shell")?.classList).toContain("remote-active");
  });

  it("opens compact mode navigation as an overlay without moving the keyboard controls", () => {
    render(<App />);

    fireEvent.click(screen.getAllByRole("button", { name: "Keyboard" }).at(0)!);
    const primaryKeys = screen.getByLabelText("Primary keyboard keys");
    const beforeParent = primaryKeys.parentElement;

    fireEvent.click(screen.getByRole("button", { name: "Change mode" }));

    expect(screen.getByRole("menu", { name: "Change mode" })).toBeTruthy();
    expect(screen.getByLabelText("Primary keyboard keys")).toBe(primaryKeys);
    expect(primaryKeys.parentElement).toBe(beforeParent);
  });

  it("keeps the bottom mode row mounted across mode changes, then collapses it from the active tab", () => {
    render(<App />);

    const appShell = document.querySelector(".app-shell");
    const bottomModeNavigation = document.querySelector<HTMLElement>(".bottom-mode-tabs");
    expect(bottomModeNavigation).not.toBeNull();
    expect(appShell?.contains(bottomModeNavigation)).toBe(false);
    expect(bottomModeNavigation?.parentElement).toBe(appShell?.parentElement);
    expect(bottomModeNavigation?.parentElement?.classList).toContain("app-frame");

    for (const modeName of ["Keyboard", "Remote", "Presentation"]) {
      fireEvent.click(within(bottomModeNavigation!).getByRole("button", { name: modeName }));

      expect(document.querySelector(".bottom-mode-tabs")).toBe(bottomModeNavigation);
      expect(appShell?.classList.contains("mode-tabs-collapsed")).toBe(false);
      expect(within(bottomModeNavigation!).getByRole("button", { name: modeName }).getAttribute("aria-current")).toBe("page");
    }

    fireEvent.click(within(bottomModeNavigation!).getByRole("button", { name: "Presentation" }));

    expect(appShell?.classList.contains("mode-tabs-collapsed")).toBe(true);
    expect(document.querySelector(".bottom-mode-tabs")).toBeNull();
    expect(screen.getByText("Switch modes from here.")).toBeTruthy();

    fireEvent.click(screen.getByRole("button", { name: "Change mode" }));
    expect(screen.queryByText("Switch modes from here.")).toBeNull();
    fireEvent.click(screen.getByRole("menuitemradio", { name: "Keyboard" }));

    expect(appShell?.classList.contains("mode-tabs-collapsed")).toBe(false);
    expect(document.querySelector(".bottom-mode-tabs")).toBeTruthy();

    fireEvent.click(screen.getAllByRole("button", { name: "Keyboard" }).at(-1)!);
    expect(screen.queryByText("Switch modes from here.")).toBeNull();
  });

  it("confirms opening the configured remote app before sending the launch command", () => {
    const send = vi.fn();
    localStorage.setItem("voltura-air.remoteSettings.client-a.pc-a", JSON.stringify({ mode: "kodi" }));
    mockConnection({ send, supportsRemoteLaunch: true });
    render(<App />);

    fireEvent.click(screen.getAllByRole("button", { name: "Remote" }).at(-1)!);

    const dialog = screen.getByRole("dialog", { name: "Open Kodi?" });
    expect(send).not.toHaveBeenCalled();
    expect(within(dialog).getByRole("button", { name: "Open Kodi" })).toBe(document.activeElement);

    fireEvent.click(within(dialog).getByRole("button", { name: "Open Kodi" }));

    expect(send).toHaveBeenCalledExactlyOnceWith({ type: "remote.launch", action: "startOrActivateKodi" });
  });

  it("does not launch a remote app when its confirmation is cancelled", () => {
    const send = vi.fn();
    localStorage.setItem("voltura-air.remoteSettings.client-a.pc-a", JSON.stringify({ mode: "youtube" }));
    mockConnection({ send, supportsRemoteLaunch: true });
    render(<App />);

    fireEvent.click(screen.getAllByRole("button", { name: "Remote" }).at(-1)!);
    fireEvent.click(screen.getByRole("button", { name: "Cancel" }));

    expect(screen.queryByRole("dialog", { name: "Open YouTube?" })).toBeNull();
    expect(send).not.toHaveBeenCalled();
  });

  it("confirms a remote app selected from settings before sending the launch command", () => {
    const send = vi.fn();
    mockConnection({ send, supportsRemoteLaunch: true });
    render(<App />);

    fireEvent.click(screen.getByRole("button", { name: "Open menu" }));
    const remoteSettingsSummary = document.querySelector<HTMLElement>("[data-settings-section=\"remote\"] > summary");
    expect(remoteSettingsSummary).not.toBeNull();
    fireEvent.click(remoteSettingsSummary!);
    fireEvent.click(screen.getByRole("button", { name: "YouTube" }));

    const dialog = screen.getByRole("dialog", { name: "Open YouTube?" });
    expect(send).not.toHaveBeenCalled();
    fireEvent.click(within(dialog).getByRole("button", { name: "Open YouTube" }));

    expect(send).toHaveBeenCalledExactlyOnceWith({ type: "remote.launch", action: "openYoutube" });
  });

  it("opens Remote when the active Standard mode is pressed in settings", () => {
    render(<App />);

    fireEvent.click(screen.getByRole("button", { name: "Open menu" }));
    const remoteSettingsSummary = document.querySelector<HTMLElement>("[data-settings-section=\"remote\"] > summary");
    expect(remoteSettingsSummary).not.toBeNull();
    fireEvent.click(remoteSettingsSummary!);
    fireEvent.click(screen.getByRole("button", { name: "Standard" }));

    expect(document.querySelector(".app-shell")?.classList.contains("remote-active")).toBe(true);
    expect(screen.queryByRole("dialog", { name: "Menu" })).toBeNull();
  });

  it.each([
    { ariaLabel: "Dictation", fourthMode: "dictation" },
    { ariaLabel: "Send text to PC", fourthMode: "text-transfer" },
    { ariaLabel: "Get text from PC", fourthMode: "clipboard-read" },
    { ariaLabel: "Presentation", fourthMode: "presentation" }
  ])("keeps the configured $fourthMode fourth mode in the isolated bottom navigation", ({ ariaLabel, fourthMode }) => {
    localStorage.setItem("voltura-air.appSettings.client-a.pc-a", JSON.stringify({ fourthMode }));
    render(<App />);

    const bottomModeNavigation = document.querySelector<HTMLElement>(".bottom-mode-tabs");
    expect(bottomModeNavigation).not.toBeNull();
    const fourthModeButton = within(bottomModeNavigation!).getByRole("button", { name: ariaLabel });

    fireEvent.click(fourthModeButton);

    expect(document.querySelector(".bottom-mode-tabs")).toBe(bottomModeNavigation);
    expect(fourthModeButton.getAttribute("aria-current")).toBe("page");
  });

  it("uses Files as an optional fourth mode without changing Presentation's default", async () => {
    localStorage.setItem("voltura-air.appSettings.client-a.pc-a", JSON.stringify({ fourthMode: "files" }));
    mockConnection({ fileManagerCapability: { canBrowse: false, canModify: false, hidesProtectedSystemItems: true, maxPageSize: 100 } });
    render(<App />);

    const bottomModeNavigation = document.querySelector<HTMLElement>(".bottom-mode-tabs");
    const filesButton = within(bottomModeNavigation!).getByRole("button", { name: "Files on PC" });
    fireEvent.click(filesButton);

    expect(await screen.findByText("Files needs permission")).toBeTruthy();
    expect(document.querySelector(".app-shell")?.classList.contains("files-active")).toBe(true);
    expect(document.querySelector(".bottom-mode-tabs")).toBeNull();
    expect(screen.getByRole("button", { name: "Change mode" })).toBeTruthy();
  });

  it("keeps Presentation navigation available when it is the configured fourth mode", () => {
    localStorage.setItem("voltura-air.appSettings.client-a.pc-a", JSON.stringify({ fourthMode: "presentation" }));
    render(<App />);

    const appShell = document.querySelector(".app-shell");
    const bottomModeNavigation = document.querySelector<HTMLElement>(".bottom-mode-tabs");
    const presentationButton = within(bottomModeNavigation!).getByRole("button", { name: "Presentation" });

    fireEvent.click(presentationButton);

    expect(appShell?.classList.contains("presentation-active")).toBe(true);
    expect(document.querySelector(".bottom-mode-tabs")).toBe(bottomModeNavigation);
    expect(presentationButton.getAttribute("aria-current")).toBe("page");

    fireEvent.click(presentationButton);

    expect(document.querySelector(".bottom-mode-tabs")).toBeNull();
    expect(screen.getByRole("button", { name: "Change mode" })).toBeTruthy();
  });

  it("hides the bottom mode row and keeps the header selector available while remote Functions is open", () => {
    render(<App />);

    fireEvent.click(screen.getAllByRole("button", { name: "Remote" }).at(-1)!);
    expect(document.querySelector(".bottom-mode-tabs")).toBeTruthy();

    fireEvent.click(screen.getByRole("button", { name: "Functions" }));

    const appShell = document.querySelector(".app-shell");
    expect(appShell?.classList.contains("remote-utility-open")).toBe(true);
    expect(appShell?.classList.contains("has-mode-navigation")).toBe(false);
    expect(document.querySelector(".bottom-mode-tabs")).toBeNull();

    fireEvent.click(screen.getByRole("button", { name: "Change mode" }));
    expect(screen.getByRole("menu", { name: "Change mode" })).toBeTruthy();
  });

  it("does not reserve mode navigation on the PC unavailable screen", () => {
    mockConnection({
      state: "unavailable",
      message: "PC is not available"
    });

    render(<App />);

    expect(screen.queryByRole("button", { name: "Change mode" })).toBeNull();
    expect(screen.queryByRole("button", { name: "Keyboard" })).toBeNull();
    expect(screen.getByRole("button", { name: "Take photo of new QR code" })).toBeTruthy();
  });

  it("offers direct saved-PC reconnect without removing QR pairing", () => {
    const selectPc = vi.fn();
    const inputClick = vi.spyOn(HTMLInputElement.prototype, "click").mockImplementation(() => undefined);
    const savedPc = {
      customName: true,
      id: "pc-a",
      name: "Living Room PC",
      url: "http://pc.local:51395"
    };
    mockConnection({
      activePc: null,
      message: "Disconnected. Choose a saved PC or scan a pairing QR.",
      pairedPcs: [savedPc],
      reconnectablePcs: [savedPc],
      selectPc,
      state: "disconnected"
    });

    render(<App />);

    expect(screen.getByRole("heading", { name: "PC disconnected" })).toBeTruthy();
    expect(screen.getByRole("dialog").getAttribute("aria-modal")).toBe("true");
    expect(screen.getByText("Reconnect to Living Room PC, or pair another PC by taking a photo of its QR code.")).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Take photo of QR code" }));
    expect(inputClick).toHaveBeenCalledOnce();

    fireEvent.click(screen.getByRole("button", { name: "Try reconnect" }));
    expect(selectPc).toHaveBeenCalledExactlyOnceWith("pc-a");
    inputClick.mockRestore();
  });

  it("lets the user choose which saved PC to reconnect", () => {
    const selectPc = vi.fn();
    const savedPcs = [
      { customName: true, id: "pc-a", name: "Office PC", url: "http://office.local:51395" },
      { customName: true, id: "pc-b", name: "Living Room PC", url: "http://living-room.local:51395" }
    ];
    mockConnection({
      activePc: null,
      message: "Choose a PC or scan a pairing QR.",
      pairedPcs: savedPcs,
      reconnectablePcs: savedPcs,
      selectPc,
      state: "needs-pairing"
    });

    render(<App />);

    expect(screen.getByRole("heading", { name: "Connect to a PC" })).toBeTruthy();
    fireEvent.change(screen.getByLabelText("Saved PC"), { target: { value: "pc-b" } });
    fireEvent.click(screen.getByRole("button", { name: "Try reconnect" }));

    expect(selectPc).toHaveBeenCalledExactlyOnceWith("pc-b");
  });

  it("keeps a manually requested reconnect front and center until it succeeds", async () => {
    const selectPc = vi.fn();
    mockConnection({
      state: "unavailable",
      message: "PC is currently not available. Retrying...",
      selectPc
    });
    const view = render(<App />);

    fireEvent.click(screen.getByRole("button", { name: "Try reconnect" }));

    expect(selectPc).toHaveBeenCalledWith("pc-a");
    expect(screen.getByRole("dialog").getAttribute("aria-modal")).toBe("true");
    const reconnectingAction = screen.getByRole("button", { name: "Reconnecting…" });
    expect(reconnectingAction).toBe(document.activeElement);
    expect(reconnectingAction.getAttribute("aria-disabled")).toBe("true");
    fireEvent.click(reconnectingAction);
    expect(selectPc).toHaveBeenCalledOnce();
    expect(screen.getByRole("button", { name: "Expand trackpad" })).toBeTruthy();

    mockConnection({ state: "connecting", message: "Connecting...", selectPc });
    view.rerender(<App />);
    expect(screen.getByRole("dialog").getAttribute("aria-busy")).toBe("true");

    mockConnection({ state: "paired", message: "Connected", selectPc });
    view.rerender(<App />);

    await waitFor(() => {
      expect(screen.getByRole("heading", { name: "Connected to Very Long Living Room Editing Workstation" })).toBeTruthy();
    });
    const connectedAction = screen.getByRole("button", { name: "Connected" });
    expect(connectedAction).toBe(document.activeElement);
    expect(connectedAction.getAttribute("aria-disabled")).toBe("true");
    await waitFor(() => {
      expect(screen.queryByRole("dialog")).toBeNull();
    }, { timeout: 1200 });
  });

  it("returns a failed manual reconnect to the unavailable feedback without exposing a false connected state", async () => {
    const selectPc = vi.fn();
    mockConnection({ state: "unavailable", message: "PC is currently not available. Retrying...", selectPc });
    const view = render(<App />);

    fireEvent.click(screen.getByRole("button", { name: "Try reconnect" }));
    mockConnection({ state: "connecting", message: "Connecting...", selectPc });
    view.rerender(<App />);
    expect(screen.getByRole("heading", { name: /Reconnecting to/ })).toBeTruthy();

    mockConnection({ state: "unavailable", message: "PC is currently not available. Retrying...", selectPc });
    view.rerender(<App />);

    await waitFor(() => {
      expect(screen.getByRole("heading", { name: "PC not available" })).toBeTruthy();
    });
    expect(screen.getByRole<HTMLButtonElement>("button", { name: "Try reconnect" }).disabled).toBe(false);
    expect(screen.queryByText("Connected to Very Long Living Room Editing Workstation")).toBeNull();
  });

  it("applies stored split mode placement and chrome preferences on a landscape tablet", () => {
    localStorage.setItem("voltura-air.trackpadSettings.client-a.pc-a", JSON.stringify({
      enableSplitMode: true,
      splitTrackpadPlacement: "left",
      splitShowStatusRow: true
    }));

    render(<App />);

    expect(document.querySelector(".app-shell")?.classList).toContain("split-mode-active");
    expect(document.querySelector(".app-shell")?.classList).toContain("split-show-mode-buttons");
    expect(document.querySelector(".app-shell")?.classList).toContain("split-show-header");
    expect(document.querySelector(".split-mode-shell")?.classList).toContain("split-trackpad-left");
  });
});

describe("App launch feedback", () => {
  it("shows a status toast while an app launch is pending", () => {
    mockConnection({ pendingAppLaunchId: "preset.browser" });

    render(<App />);

    expect(screen.getByRole("status").textContent).toContain("Waiting for the PC");
  });

  it("shows an alert toast when an app launch fails", () => {
    mockConnection({
      appLaunchResult: {
        type: "app.launch.result",
        operationId: "op-app",
        actionId: "preset.browser",
        succeeded: false,
        code: "not-configured",
        message: "Browser could not be started."
      }
    });

    render(<App />);

    expect(screen.getByRole("alert").textContent).toBe("Browser could not be started.");
  });
});

describe("Text transfer feedback", () => {
  it("shows progress outside the text transfer page", () => {
    mockConnection({ pendingTextTransfer: true });

    render(<App />);

    expect(screen.getByRole("status").textContent).toBe("Waiting for the PC to send text…");
  });

  it("shows the delivery result outside the text transfer page", () => {
    mockConnection({
      textTransferResult: {
        type: "text.send.result",
        operationId: "operation-a",
        succeeded: true,
        message: "Text sent successfully."
      }
    });

    render(<App />);

    expect(screen.getByRole("status").textContent).toBe("Text sent successfully.");
  });

  it("uses an in-app dialog to rename saved snippets", () => {
    render(<App />);
    fireEvent.click(screen.getByRole("button", { name: "Open menu" }));
    fireEvent.click(screen.getByRole("button", { name: "Send text to PC" }));
    fireEvent.click(screen.getByText("Saved snippets").closest("summary")!);
    fireEvent.click(screen.getByRole("button", { name: "Use device keyboard" }));
    fireEvent.change(screen.getByLabelText("Text to send"), { target: { value: "Saved text" } });
    fireEvent.change(screen.getByLabelText("Snippet name"), { target: { value: "Original name" } });
    fireEvent.click(screen.getByRole("button", { name: "Save current text" }));
    fireEvent.click(screen.getByRole("button", { name: "Rename" }));

    const dialog = screen.getByRole("dialog", { name: "Rename snippet" });
    const dialogQueries = within(dialog);
    fireEvent.change(dialogQueries.getByLabelText("Snippet name"), { target: { value: "Renamed snippet" } });
    fireEvent.click(dialogQueries.getByRole("button", { name: "Rename snippet" }));

    expect(screen.getByRole("button", { name: "Renamed snippet" })).toBeTruthy();

    fireEvent.click(screen.getByRole("button", { name: "Use device keyboard" }));
    fireEvent.change(screen.getByLabelText("Text to send"), { target: { value: "Replacement text" } });
    fireEvent.click(screen.getByRole("button", { name: "Update" }));
    const updateDialog = screen.getByRole("dialog", { name: "Update snippet" });
    fireEvent.click(within(updateDialog).getByRole("button", { name: "Update snippet" }));
    fireEvent.click(screen.getByRole("button", { name: "Use device keyboard" }));
    fireEvent.change(screen.getByLabelText("Text to send"), { target: { value: "" } });
    fireEvent.click(screen.getByRole("button", { name: "Renamed snippet" }));
    expect(screen.getByLabelText<HTMLTextAreaElement>("Text to send").value).toBe("Replacement text");
    expect(document.querySelector(".text-transfer-editor-surface")?.classList).toContain("snippet-copied");
    expect(screen.getByText("Renamed snippet copied to the text box.")).toBeTruthy();
  });

  it("starts with saved snippets folded and keeps an exact draft match visually neutral", () => {
    render(<App />);
    fireEvent.click(screen.getByRole("button", { name: "Open menu" }));
    fireEvent.click(screen.getByRole("button", { name: "Send text to PC" }));

    const summary = screen.getByText("Saved snippets").closest("summary")!;
    const details = summary.closest("details")!;
    expect(details.open).toBe(false);

    fireEvent.click(summary);
    fireEvent.click(screen.getByRole("button", { name: "Use device keyboard" }));
    fireEvent.change(screen.getByLabelText("Text to send"), { target: { value: "Already saved" } });
    fireEvent.change(screen.getByLabelText("Snippet name"), { target: { value: "Greeting" } });
    fireEvent.click(screen.getByRole("button", { name: "Save current text" }));

    const savedSnippet = screen.getByRole("button", { name: "Greeting" });
    expect(savedSnippet.classList).toContain("draft-match");
    expect(savedSnippet.classList).not.toContain("active");
    expect(savedSnippet.getAttribute("aria-pressed")).toBeNull();
  });

  it("requires snippet names to be unique when saving and renaming", () => {
    render(<App />);
    fireEvent.click(screen.getByRole("button", { name: "Open menu" }));
    fireEvent.click(screen.getByRole("button", { name: "Send text to PC" }));
    fireEvent.click(screen.getByText("Saved snippets").closest("summary")!);

    const editor = screen.getByLabelText("Text to send");
    const nameInput = screen.getByLabelText("Snippet name");
    fireEvent.change(editor, { target: { value: "First text" } });
    fireEvent.change(nameInput, { target: { value: "First" } });
    fireEvent.click(screen.getByRole("button", { name: "Save current text" }));
    fireEvent.change(editor, { target: { value: "Second text" } });
    fireEvent.change(nameInput, { target: { value: "Second" } });
    fireEvent.click(screen.getByRole("button", { name: "Save current text" }));

    fireEvent.change(nameInput, { target: { value: " first " } });
    expect(screen.getByText("A snippet with this name already exists.")).toBeTruthy();
    expect(screen.getByRole<HTMLButtonElement>("button", { name: "Save current text" }).disabled).toBe(true);

    const secondSnippetCard = screen.getByRole("button", { name: "Second" }).closest("li");
    expect(secondSnippetCard).not.toBeNull();
    fireEvent.click(within(secondSnippetCard!).getByRole("button", { name: "Rename" }));
    const renameDialog = screen.getByRole("dialog", { name: "Rename snippet" });
    fireEvent.change(within(renameDialog).getByLabelText("Snippet name"), { target: { value: "FIRST" } });
    expect(within(renameDialog).getByText("A snippet with this name already exists.")).toBeTruthy();
    expect(within(renameDialog).getByRole<HTMLButtonElement>("button", { name: "Rename snippet" }).disabled).toBe(true);
  });

  it("reorders snippet cards after a long press and persists the new order", async () => {
    vi.useFakeTimers();
    const originalElementFromPoint = document.elementFromPoint;
    try {
      render(<App />);
      fireEvent.click(screen.getByRole("button", { name: "Open menu" }));
      fireEvent.click(screen.getByRole("button", { name: "Send text to PC" }));
      fireEvent.click(screen.getByText("Saved snippets").closest("summary")!);

      const editor = screen.getByLabelText("Text to send");
      const nameInput = screen.getByLabelText("Snippet name");
      for (const [name, text] of [["First", "First text"], ["Second", "Second text"]]) {
        fireEvent.change(editor, { target: { value: text } });
        fireEvent.change(nameInput, { target: { value: name } });
        fireEvent.click(screen.getByRole("button", { name: "Save current text" }));
      }

      const firstButton = screen.getByRole("button", { name: "First" });
      const firstCard = firstButton.closest("li")!;
      const secondCard = screen.getByRole("button", { name: "Second" }).closest("li")!;
      const textTransferMode = firstCard.closest<HTMLElement>(".text-transfer-mode")!;
      textTransferMode.scrollTop = 100;
      fireEvent.touchStart(secondCard, { touches: [{ identifier: 9, clientX: 20, clientY: 100 }] });
      fireEvent.touchMove(secondCard, { touches: [{ identifier: 9, clientX: 20, clientY: 60 }] });
      expect(textTransferMode.scrollTop).toBe(140);
      fireEvent.touchEnd(secondCard, { touches: [], changedTouches: [{ identifier: 9, clientX: 20, clientY: 60 }] });

      Object.defineProperty(document, "elementFromPoint", { configurable: true, value: vi.fn(() => secondCard) });
      textTransferMode.scrollTop = 35;
      fireEvent.touchStart(firstButton, { touches: [{ identifier: 1, clientX: 20, clientY: 20 }] });
      await act(() => vi.advanceTimersByTime(450));
      expect(firstCard.classList).toContain("snippet-dragging");
      expect(textTransferMode.scrollTop).toBe(35);
      textTransferMode.scrollTop = 60;

      fireEvent.touchMove(firstButton, { touches: [{ identifier: 1, clientX: 20, clientY: 100 }] });
      expect(textTransferMode.scrollTop).toBe(35);
      fireEvent.touchEnd(firstButton, { touches: [], changedTouches: [{ identifier: 1, clientX: 20, clientY: 100 }] });

      expect(Array.from(document.querySelectorAll(".snippet-load"), (button) => button.textContent)).toEqual(["Second", "First"]);
      expect(readStoredStringProperty("voltura-air.textSnippets.client-a", "name")).toEqual(["Second", "First"]);
      expect(screen.getByText("First moved to position 2.")).toBeTruthy();
      fireEvent.click(firstButton);
      expect((editor as HTMLTextAreaElement).value).toBe("Second text");

      fireEvent.touchStart(firstButton, { touches: [{ identifier: 7, clientX: 20, clientY: 20 }] });
      await act(() => vi.advanceTimersByTime(450));
      fireEvent.touchMove(firstButton, { touches: [{ identifier: 7, clientX: 20, clientY: 100 }] });
      fireEvent.touchCancel(firstButton, { touches: [], changedTouches: [{ identifier: 7, clientX: 20, clientY: 100 }] });

      expect(Array.from(document.querySelectorAll(".snippet-load"), (button) => button.textContent)).toEqual(["Second", "First"]);
      expect(readStoredStringProperty("voltura-air.textSnippets.client-a", "name")).toEqual(["Second", "First"]);
      expect(firstCard.classList).not.toContain("snippet-dragging");

      vi.mocked(document.elementFromPoint).mockReturnValue(null);
      vi.spyOn(secondCard, "getBoundingClientRect").mockReturnValue({ top: 40, bottom: 80 } as DOMRect);
      fireEvent.touchStart(firstCard, { touches: [{ identifier: 2, clientX: 20, clientY: 100 }] });
      await act(() => vi.advanceTimersByTime(450));
      fireEvent.touchMove(firstCard, { touches: [{ identifier: 2, clientX: 20, clientY: 30 }] });
      fireEvent.touchMove(firstCard, { touches: [{ identifier: 2, clientX: 20, clientY: 20 }] });
      fireEvent.touchEnd(firstCard, { touches: [], changedTouches: [{ identifier: 2, clientX: 20, clientY: 30 }] });

      expect(Array.from(document.querySelectorAll(".snippet-load"), (button) => button.textContent)).toEqual(["First", "Second"]);
      expect(readStoredStringProperty("voltura-air.textSnippets.client-a", "name")).toEqual(["First", "Second"]);
      expect(screen.getByText("First moved to position 1.")).toBeTruthy();
      await act(() => vi.runOnlyPendingTimers());
    } finally {
      if (originalElementFromPoint) {
        Object.defineProperty(document, "elementFromPoint", { configurable: true, value: originalElementFromPoint });
      } else {
        Reflect.deleteProperty(document, "elementFromPoint");
      }
      vi.useRealTimers();
    }
  });

  it("makes the text editor's touchpad, click, and device-keyboard controls explicit", () => {
    const send = vi.fn();
    mockConnection({ send });
    render(<App />);
    fireEvent.click(screen.getByRole("button", { name: "Open menu" }));
    fireEvent.click(screen.getByRole("button", { name: "Send text to PC" }));
    const editor = screen.getByLabelText<HTMLTextAreaElement>("Text to send");

    expect(document.querySelector(".app-shell")?.classList).toContain("text-transfer-active");
    expect(editor.readOnly).toBe(false);
    expect(editor.placeholder).toBe("Type or paste text here…");
    const editorToolbar = document.querySelector<HTMLElement>(".text-transfer-editor-toolbar");
    expect(editorToolbar).not.toBeNull();
    const editorModeControl = within(editorToolbar!).getByLabelText("Text box mode");
    expect(screen.getByRole("button", { name: "Use device keyboard" }).getAttribute("aria-pressed")).toBe("true");
    expect(within(editorModeControl).queryByText("Keyboard")).toBeNull();
    expect(within(editorModeControl).getByText("Touchpad")).toBeTruthy();
    expect(within(editorToolbar!).getByLabelText("Device keyboard type")).toBeTruthy();
    expect(screen.getByRole("button", { name: "Send text" })).toBeTruthy();
    expect(screen.getByText("Clear after sending")).toBeTruthy();
    expect(screen.getByText("Saved snippets")).toBeTruthy();

    fireEvent.click(screen.getByRole("button", { name: "Touchpad" }));
    expect(editor.readOnly).toBe(true);
    expect(editor.placeholder).toBe("");
    expect(editor.closest(".text-transfer-editor-surface")?.classList).not.toContain("is-editing");
    expect(screen.getByRole("button", { name: "Touchpad" }).getAttribute("aria-pressed")).toBe("true");
    expect(within(editorModeControl).getByText("Keyboard")).toBeTruthy();
    expect(within(editorModeControl).queryByText("Touchpad")).toBeNull();
    expect(within(editorToolbar!).queryByLabelText("Device keyboard type")).toBeNull();
    expect(screen.queryByRole("button", { name: "Send text" })).toBeNull();
    expect(screen.queryByText("Clear after sending")).toBeNull();
    expect(screen.queryByText("Saved snippets")).toBeNull();
    expect(screen.queryByText("Move pointer")).toBeNull();

    fireEvent.touchStart(editor, { targetTouches: [{ identifier: 1, clientX: 10, clientY: 10 }] });
    fireEvent.touchEnd(editor, { targetTouches: [] });
    expect(send).toHaveBeenCalledExactlyOnceWith({ type: "pointer.button", button: "left", action: "click" });

    send.mockClear();
    fireEvent.click(screen.getByRole("button", { name: "Left" }));
    fireEvent.click(screen.getByRole("button", { name: "Right" }));
    expect(send).toHaveBeenNthCalledWith(1, { type: "pointer.button", button: "left", action: "click" });
    expect(send).toHaveBeenNthCalledWith(2, { type: "pointer.button", button: "right", action: "click" });

    send.mockClear();
    fireEvent.click(screen.getByRole("button", { name: "Use device keyboard" }));
    expect(editor.readOnly).toBe(false);
    expect(document.activeElement).toBe(editor);
    expect(screen.getByRole("button", { name: "Touchpad" })).toBeTruthy();
    expect(screen.getByRole("button", { name: "Use device keyboard" }).getAttribute("aria-pressed")).toBe("true");
    expect(screen.getByRole("tab", { name: "Show regular keyboard" })).toBeTruthy();
    fireEvent.click(screen.getByRole("tab", { name: "Show numeric keyboard" }));
    expect(editor.inputMode).toBe("numeric");
    fireEvent.touchStart(editor, { targetTouches: [{ identifier: 1, clientX: 10, clientY: 10 }] });
    fireEvent.touchEnd(editor, { targetTouches: [] });
    expect(send).not.toHaveBeenCalled();

    fireEvent.click(screen.getByRole("button", { name: "Touchpad" }));
    expect(editor.readOnly).toBe(true);
    expect(document.activeElement).not.toBe(editor);
    expect(screen.queryByRole("tab", { name: "Show numeric keyboard" })).toBeNull();
  });

  it("reverses text-transfer pointer buttons for the left-handed layout", () => {
    localStorage.setItem("voltura-air.trackpadSettings.client-a.pc-a", JSON.stringify({ leftHandedButtons: true }));
    render(<App />);
    fireEvent.click(screen.getByRole("button", { name: "Open menu" }));
    fireEvent.click(screen.getByRole("button", { name: "Send text to PC" }));
    fireEvent.click(screen.getByRole("button", { name: "Touchpad" }));

    const mouseButtons = screen.getByLabelText("Mouse buttons");
    expect(within(mouseButtons).getAllByRole("button").map((button) => button.textContent)).toEqual(["Right", "Left"]);
  });
});
