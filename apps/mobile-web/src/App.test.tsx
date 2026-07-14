import { act, fireEvent, render, screen, within } from "@testing-library/react";
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
    setHostPointerHighlight: vi.fn(),
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
    expect((screen.getByLabelText("Text to send") as HTMLTextAreaElement).value).toBe("Replacement text");
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
    expect((screen.getByRole("button", { name: "Save current text" }) as HTMLButtonElement).disabled).toBe(true);

    const secondSnippetCard = screen.getByRole("button", { name: "Second" }).closest("li");
    expect(secondSnippetCard).not.toBeNull();
    fireEvent.click(within(secondSnippetCard!).getByRole("button", { name: "Rename" }));
    const renameDialog = screen.getByRole("dialog", { name: "Rename snippet" });
    fireEvent.change(within(renameDialog).getByLabelText("Snippet name"), { target: { value: "FIRST" } });
    expect(within(renameDialog).getByText("A snippet with this name already exists.")).toBeTruthy();
    expect((within(renameDialog).getByRole("button", { name: "Rename snippet" }) as HTMLButtonElement).disabled).toBe(true);
  });

  it("reorders snippet cards after a long press and persists the new order", () => {
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
      act(() => vi.advanceTimersByTime(450));
      expect(firstCard.classList).toContain("snippet-dragging");
      expect(textTransferMode.scrollTop).toBe(35);
      textTransferMode.scrollTop = 60;

      fireEvent.touchMove(firstButton, { touches: [{ identifier: 1, clientX: 20, clientY: 100 }] });
      expect(textTransferMode.scrollTop).toBe(35);
      fireEvent.touchEnd(firstButton, { touches: [], changedTouches: [{ identifier: 1, clientX: 20, clientY: 100 }] });

      expect(Array.from(document.querySelectorAll(".snippet-load"), (button) => button.textContent)).toEqual(["Second", "First"]);
      expect(JSON.parse(localStorage.getItem("voltura-air.textSnippets.client-a") ?? "[]").map((snippet: { name: string }) => snippet.name)).toEqual(["Second", "First"]);
      expect(screen.getByText("First moved to position 2.")).toBeTruthy();
      fireEvent.click(firstButton);
      expect((editor as HTMLTextAreaElement).value).toBe("Second text");

      vi.mocked(document.elementFromPoint).mockReturnValue(null);
      vi.spyOn(secondCard, "getBoundingClientRect").mockReturnValue({ top: 40, bottom: 80 } as DOMRect);
      fireEvent.touchStart(firstCard, { touches: [{ identifier: 2, clientX: 20, clientY: 100 }] });
      act(() => vi.advanceTimersByTime(450));
      fireEvent.touchMove(firstCard, { touches: [{ identifier: 2, clientX: 20, clientY: 30 }] });
      fireEvent.touchMove(firstCard, { touches: [{ identifier: 2, clientX: 20, clientY: 20 }] });
      fireEvent.touchEnd(firstCard, { touches: [], changedTouches: [{ identifier: 2, clientX: 20, clientY: 30 }] });

      expect(Array.from(document.querySelectorAll(".snippet-load"), (button) => button.textContent)).toEqual(["First", "Second"]);
      expect(JSON.parse(localStorage.getItem("voltura-air.textSnippets.client-a") ?? "[]").map((snippet: { name: string }) => snippet.name)).toEqual(["First", "Second"]);
      expect(screen.getByText("First moved to position 1.")).toBeTruthy();
      act(() => vi.runOnlyPendingTimers());
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
    const editor = screen.getByLabelText("Text to send") as HTMLTextAreaElement;

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
