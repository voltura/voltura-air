import { fireEvent, render, screen, within } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { App } from "./App";
import { useVolturaAirConnection } from "./useVolturaAirConnection";

vi.mock("./useVolturaAirConnection", () => ({
  useVolturaAirConnection: vi.fn()
}));

function createStorage(): Storage {
  const items = new Map<string, string>();
  return {
    get length() {
      return items.size;
    },
    clear: () => items.clear(),
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

function mockConnection(overrides: Partial<ReturnType<typeof useVolturaAirConnection>> = {}) {
  vi.mocked(useVolturaAirConnection).mockReturnValue({
    state: "paired",
    message: "Connected to Very Long Living Room Editing Workstation",
    send: vi.fn(),
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
    pendingTextTransfer: false,
    requestTextTransfer: vi.fn(() => "operation-a"),
    textTransferResult: null,
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
});

describe("App header and mode navigation", () => {
  it("uses accessible mode labels and selected state in both navigation surfaces", () => {
    render(<App />);

    const keyboardModeButtons = screen.getAllByRole("button", { name: "Keyboard mode" });
    expect(keyboardModeButtons).toHaveLength(2);
    expect(keyboardModeButtons.every((button) => button.getAttribute("aria-selected") === "false")).toBe(true);

    const trackpadModeButtons = screen.getAllByRole("button", { name: "Trackpad mode" });
    expect(trackpadModeButtons.some((button) => button.getAttribute("aria-selected") === "true")).toBe(true);
  });

  it("opens either tool from Menu without changing the configured fourth mode", () => {
    render(<App />);

    fireEvent.click(screen.getByRole("button", { name: "Open menu" }));
    expect(screen.getByRole("heading", { name: "Menu" })).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Send text to PC" }));

    expect(screen.getByRole("heading", { name: "Send text to PC" })).toBeTruthy();
    expect(screen.getAllByRole("button", { name: "Dictation" })).toHaveLength(2);
    expect(screen.queryByRole("button", { name: "Back to previous mode" })).toBeNull();
    fireEvent.click(screen.getAllByRole("button", { name: "Trackpad mode" }).at(-1)!);
    expect(screen.getAllByRole("button", { name: "Trackpad mode" }).some((button) => button.getAttribute("aria-selected") === "true")).toBe(true);

    fireEvent.click(screen.getByRole("button", { name: "Open menu" }));
    const menu = screen.getByRole("heading", { name: "Menu" }).closest("aside");
    expect(menu).not.toBeNull();
    fireEvent.click(within(menu!).getByRole("button", { name: "Dictation" }));
    expect(screen.getByLabelText("Dictation text")).toBeTruthy();
    expect(screen.queryByRole("button", { name: "Back to previous mode" })).toBeNull();
  });

  it("opens compact mode navigation as an overlay without moving the keyboard controls", () => {
    render(<App />);

    fireEvent.click(screen.getAllByRole("button", { name: "Keyboard mode" })[0]);
    const primaryKeys = screen.getByLabelText("Primary keyboard keys");
    const beforeParent = primaryKeys.parentElement;

    fireEvent.click(screen.getByRole("button", { name: "Change mode" }));

    expect(screen.getByRole("menu", { name: "Change mode" })).toBeTruthy();
    expect(screen.getByLabelText("Primary keyboard keys")).toBe(primaryKeys);
    expect(primaryKeys.parentElement).toBe(beforeParent);
  });

  it("collapses the bottom mode row from the active tab and restores it from the compact selector", () => {
    render(<App />);

    const appShell = document.querySelector(".app-shell");
    const activeTrackpadButtons = screen.getAllByRole("button", { name: "Trackpad mode" });
    fireEvent.click(activeTrackpadButtons[activeTrackpadButtons.length - 1]);

    expect(appShell?.classList.contains("mode-tabs-collapsed")).toBe(true);
    expect(document.querySelector(".bottom-mode-tabs")).toBeNull();

    fireEvent.click(screen.getByRole("button", { name: "Change mode" }));
    fireEvent.click(screen.getByRole("menuitemradio", { name: "Trackpad mode" }));

    expect(appShell?.classList.contains("mode-tabs-collapsed")).toBe(false);
    expect(document.querySelector(".bottom-mode-tabs")).toBeTruthy();
  });

  it("does not reserve mode navigation on the PC unavailable screen", () => {
    mockConnection({
      state: "unavailable",
      message: "PC is not available"
    });

    render(<App />);

    expect(screen.queryByRole("button", { name: "Change mode" })).toBeNull();
    expect(screen.queryByRole("button", { name: "Keyboard mode" })).toBeNull();
  });

  it("applies stored split mode placement and chrome preferences on a landscape tablet", () => {
    localStorage.setItem("voltura-air.trackpadSettings.client-a.pc-a", JSON.stringify({
      enableSplitMode: true,
      splitTrackpadPlacement: "left",
      splitShowModeButtons: true,
      splitShowStatusRow: true
    }));

    render(<App />);

    expect(document.querySelector(".app-shell")?.classList).toContain("split-mode-active");
    expect(document.querySelector(".app-shell")?.classList).toContain("split-show-mode-buttons");
    expect(document.querySelector(".app-shell")?.classList).toContain("split-show-status-row");
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
    fireEvent.change(screen.getByLabelText("Text to send"), { target: { value: "Saved text" } });
    fireEvent.change(screen.getByLabelText("Snippet name"), { target: { value: "Original name" } });
    fireEvent.click(screen.getByRole("button", { name: "Save current text" }));
    fireEvent.click(screen.getByRole("button", { name: "Rename" }));

    const dialog = screen.getByRole("dialog", { name: "Rename snippet" });
    const dialogQueries = within(dialog);
    fireEvent.change(dialogQueries.getByLabelText("Snippet name"), { target: { value: "Renamed snippet" } });
    fireEvent.click(dialogQueries.getByRole("button", { name: "Rename snippet" }));

    expect(screen.getByRole("button", { name: "Renamed snippet" })).toBeTruthy();

    fireEvent.change(screen.getByLabelText("Text to send"), { target: { value: "Replacement text" } });
    fireEvent.click(screen.getByRole("button", { name: "Update" }));
    const updateDialog = screen.getByRole("dialog", { name: "Update snippet" });
    fireEvent.click(within(updateDialog).getByRole("button", { name: "Update snippet" }));
    fireEvent.change(screen.getByLabelText("Text to send"), { target: { value: "" } });
    fireEvent.click(screen.getByRole("button", { name: "Renamed snippet" }));
    expect((screen.getByLabelText("Text to send") as HTMLTextAreaElement).value).toBe("Replacement text");
  });
});
