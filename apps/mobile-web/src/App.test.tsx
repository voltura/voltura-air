import { fireEvent, render, screen } from "@testing-library/react";
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
    supportsGestureDebug: false,
    supportsSleep: true,
    supportsVolumeControl: true,
    supportsRemoteLaunch: false,
    lastConnectionError: null,
    hostStatus: null,
    pairWithToken: vi.fn(),
    selectPc: vi.fn(),
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
  it("keeps settings first, brand second, and compact mode selector after the brand", () => {
    render(<App />);

    const header = screen.getByRole("banner");
    const settingsButton = screen.getByRole("button", { name: "Open settings" });
    const brand = screen.getByText("Voltura Air").closest(".brand");
    const modeButton = screen.getByRole("button", { name: "Change mode" });

    expect(header.compareDocumentPosition(settingsButton) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(settingsButton.compareDocumentPosition(brand as Element) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect((brand as Element).compareDocumentPosition(modeButton) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it("uses accessible mode labels and selected state in both navigation surfaces", () => {
    render(<App />);

    const keyboardModeButtons = screen.getAllByRole("button", { name: "Keyboard mode" });
    expect(keyboardModeButtons).toHaveLength(2);
    expect(keyboardModeButtons.every((button) => button.getAttribute("aria-selected") === "false")).toBe(true);

    const trackpadModeButtons = screen.getAllByRole("button", { name: "Trackpad mode" });
    expect(trackpadModeButtons.some((button) => button.getAttribute("aria-selected") === "true")).toBe(true);
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
});
