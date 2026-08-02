import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { createPairingKeyMaterial } from "../../foundation/connection/pairingCredentials";
import { publishScreenViewResult } from "../../foundation/connection/screenViewResultBus";
import { defaultTrackpadSettings } from "../../foundation/input/gestures";
import type { ClientMessage, ScreenViewCapability } from "../../foundation/protocol/messages";
import ScreenViewWorkspace from "./ScreenViewWorkspace";

const capability: ScreenViewCapability = {
  enabled: true,
  permissionGranted: true,
  canView: true,
  requiresRepair: false,
  encrypted: true,
  maxWidth: 1920,
  maxHeight: 1080,
  maxFramesPerSecond: 30
};

beforeEach(() => {
  const items = new Map<string, string>();
  vi.stubGlobal("localStorage", {
    get length() { return items.size; },
    clear: () => { items.clear(); },
    getItem: (key: string) => items.get(key) ?? null,
    key: (index: number) => Array.from(items.keys())[index] ?? null,
    removeItem: (key: string) => { items.delete(key); },
    setItem: (key: string, value: string) => { items.set(key, String(value)); }
  } satisfies Storage);
  vi.stubGlobal("RTCPeerConnection", class {});
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("ScreenViewWorkspace", () => {
  it("opens and requests sources without requiring secure-context randomUUID", async () => {
    const send = vi.fn<(message: ClientMessage) => void>();
    const originalRandomUuid = crypto.randomUUID;
    Object.defineProperty(crypto, "randomUUID", { configurable: true, value: undefined });

    try {
      render(<ScreenViewWorkspace
        activePc={{ customName: false, id: "http://192.168.1.10:51396", name: "PC", url: "http://192.168.1.10:51396" }}
        capability={capability}
        clientId="client-test"
        onBack={vi.fn()}
        onOpenKeyboard={vi.fn()}
        send={send}
        state="paired"
        trackpadSettings={defaultTrackpadSettings}
      />);

      expect(screen.getByText("Live mirror")).toBeTruthy();
      await waitFor(() => expect(send).toHaveBeenCalled());
      const request = send.mock.calls[0]?.[0];
      expect(request?.type).toBe("screen.view.sources.get");
      if (request?.type !== "screen.view.sources.get") {
        throw new Error("Screen source request was not sent.");
      }
      expect(request.operationId).toMatch(/^[a-z0-9-]+$/);
    } finally {
      Object.defineProperty(crypto, "randomUUID", { configurable: true, value: originalRandomUuid });
    }
  });

  it("starts mirroring automatically when the PC has one display", async () => {
    const send = vi.fn<(message: ClientMessage) => void>();
    const pcId = "http://192.168.1.10:51396";
    const key = createPairingKeyMaterial();
    if (!key) {
      throw new Error("Test key generation is unavailable.");
    }
    localStorage.setItem(`voltura-air.reconnect-key.client-test.${pcId}`, key.privateKey);

    render(<ScreenViewWorkspace
      activePc={{ customName: false, id: pcId, name: "PC", url: pcId, hostIdentityPublicKey: key.reconnectPublicKey }}
      capability={capability}
      clientId="client-test"
      onBack={vi.fn()}
      onOpenKeyboard={vi.fn()}
      send={send}
      state="paired"
      trackpadSettings={defaultTrackpadSettings}
    />);

    await waitFor(() => expect(send).toHaveBeenCalled());
    act(() => {
      publishScreenViewResult({
        type: "screen.view.sources.result",
        operationId: "sources",
        succeeded: true,
        message: "Displays are available.",
        sources: [{ id: "display-1", label: "Main display", width: 1920, height: 1080, isPrimary: true }]
      });
    });

    await waitFor(() => {
      expect(send.mock.calls.some(([message]) => message.type === "screen.view.start")).toBe(true);
    });
    const startRequest = send.mock.calls.map(([message]) => message).find((message) => message.type === "screen.view.start");
    expect(startRequest?.type).toBe("screen.view.start");
    if (startRequest?.type !== "screen.view.start") {
      throw new Error("Screen start request was not sent.");
    }
    expect(startRequest.displayId).toBe("display-1");
    expect(screen.getByRole("status").textContent).toBe("Preparing encrypted WebRTC mirror...");
  });

  it("uses an in-app full-screen fallback and keeps an explicit exit control", async () => {
    const send = vi.fn<(message: ClientMessage) => void>();
    render(<ScreenViewWorkspace
      activePc={{ customName: false, id: "http://192.168.1.10:51396", name: "PC", url: "http://192.168.1.10:51396" }}
      capability={capability}
      clientId="client-test"
      onBack={vi.fn()}
      onOpenKeyboard={vi.fn()}
      send={send}
      state="paired"
      trackpadSettings={defaultTrackpadSettings}
    />);

    fireEvent.loadedData(screen.getByLabelText("Mirrored PC display video"));
    const fullScreenButton = screen.getByRole("button", { name: "View PC screen full screen" });
    fireEvent.touchStart(fullScreenButton, {
      targetTouches: [{ identifier: 1, clientX: 360, clientY: 80 }]
    });
    fireEvent.touchEnd(fullScreenButton, { targetTouches: [] });
    fireEvent.click(fullScreenButton);
    await waitFor(() => expect(document.querySelector(".screen-view-workspace")?.classList).toContain("is-immersive"));
    expect(send.mock.calls.some(([message]) => message.type === "pointer.button")).toBe(false);
    screen.getByRole("button", { name: "Exit full screen" }).click();
    await waitFor(() => expect(document.querySelector(".screen-view-workspace")?.classList).not.toContain("is-immersive"));
  });

  it("offers full screen in portrait and exits when rotating to landscape", async () => {
    const send = vi.fn<(message: ClientMessage) => void>();
    vi.stubGlobal("innerWidth", 390);
    vi.stubGlobal("innerHeight", 844);
    render(<ScreenViewWorkspace
      activePc={{ customName: false, id: "http://192.168.1.10:51396", name: "PC", url: "http://192.168.1.10:51396" }}
      capability={capability}
      clientId="client-test"
      onBack={vi.fn()}
      onOpenKeyboard={vi.fn()}
      send={send}
      state="paired"
      trackpadSettings={defaultTrackpadSettings}
    />);

    fireEvent.loadedData(screen.getByLabelText("Mirrored PC display video"));
    screen.getByRole("button", { name: "View PC screen full screen" }).click();
    await waitFor(() => expect(document.querySelector(".screen-view-workspace")?.classList).toContain("is-immersive"));
    vi.stubGlobal("innerWidth", 844);
    vi.stubGlobal("innerHeight", 390);
    act(() => {window.dispatchEvent(new Event("orientationchange"));});
    await waitFor(() => expect(document.querySelector(".screen-view-workspace")?.classList).not.toContain("is-immersive"));
  });

  it("uses an unzoomed two-finger drag for remote scrolling despite finger-spacing wobble", async () => {
    const send = vi.fn<(message: ClientMessage) => void>();
    render(<ScreenViewWorkspace
      activePc={{ customName: false, id: "http://192.168.1.10:51396", name: "PC", url: "http://192.168.1.10:51396" }}
      capability={capability}
      clientId="client-test"
      onBack={vi.fn()}
      onOpenKeyboard={vi.fn()}
      send={send}
      state="paired"
      trackpadSettings={{ ...defaultTrackpadSettings, zoomGestures: true }}
    />);
    const stage = document.querySelector<HTMLElement>(".screen-view-stage");
    if (!stage) {throw new Error("Screen stage was not rendered.");}
    const first = [
      { identifier: 1, clientX: 100, clientY: 100 },
      { identifier: 2, clientX: 160, clientY: 100 }
    ];
    const moved = [
      { identifier: 1, clientX: 94, clientY: 125 },
      { identifier: 2, clientX: 166, clientY: 125 }
    ];

    fireEvent.touchStart(stage, { targetTouches: first });
    fireEvent.touchMove(stage, { targetTouches: moved });

    await waitFor(() => expect(send.mock.calls.some(([message]) => message.type === "pointer.wheel")).toBe(true));
    expect(document.querySelector(".screen-view-content")?.classList).not.toContain("zoomed");
  });

  it("keeps two-finger movement local after the mirror is zoomed", async () => {
    const send = vi.fn<(message: ClientMessage) => void>();
    render(<ScreenViewWorkspace
      activePc={{ customName: false, id: "http://192.168.1.10:51396", name: "PC", url: "http://192.168.1.10:51396" }}
      capability={capability}
      clientId="client-test"
      onBack={vi.fn()}
      onOpenKeyboard={vi.fn()}
      send={send}
      state="paired"
      trackpadSettings={defaultTrackpadSettings}
    />);
    const stage = document.querySelector<HTMLElement>(".screen-view-stage");
    if (!stage) {throw new Error("Screen stage was not rendered.");}
    Object.defineProperty(stage, "clientWidth", { configurable: true, value: 400 });
    Object.defineProperty(stage, "clientHeight", { configurable: true, value: 220 });
    fireEvent.loadedData(screen.getByLabelText("Mirrored PC display video"));
    const zoomModeButton = screen.getByRole("button", { name: "Two-finger mode: Scroll. Switch to Zoom" });
    fireEvent.touchStart(zoomModeButton, { targetTouches: [{ identifier: 1, clientX: 20, clientY: 200 }] });
    fireEvent.touchEnd(zoomModeButton, { targetTouches: [] });
    fireEvent.click(zoomModeButton);
    expect(send.mock.calls.some(([message]) => message.type === "pointer.button")).toBe(false);
    const first = [
      { identifier: 1, clientX: 100, clientY: 100 },
      { identifier: 2, clientX: 160, clientY: 100 }
    ];
    const spread = [
      { identifier: 1, clientX: 90, clientY: 100 },
      { identifier: 2, clientX: 170, clientY: 100 }
    ];

    fireEvent.touchStart(stage, { targetTouches: first });
    fireEvent.touchMove(stage, { targetTouches: spread });
    fireEvent.touchEnd(stage, { targetTouches: [] });
    await waitFor(() => expect(document.querySelector(".screen-view-content")?.classList).toContain("zoomed"));
    send.mockClear();

    fireEvent.touchStart(stage, { targetTouches: first });
    fireEvent.touchMove(stage, {
      targetTouches: first.map((touch) => ({ ...touch, clientY: touch.clientY + 30 }))
    });
    fireEvent.touchEnd(stage, { targetTouches: [] });

    await new Promise((resolve) => window.setTimeout(resolve, 20));
    expect(send.mock.calls.some(([message]) => message.type === "pointer.wheel")).toBe(false);

    const zoomedContent = document.querySelector<HTMLElement>(".screen-view-content");
    const zoomedTransform = zoomedContent?.style.transform;
    fireEvent.click(screen.getByRole("button", { name: "Two-finger mode: Zoom. Switch to Scroll" }));
    expect(zoomedContent?.classList).toContain("zoomed");
    expect(zoomedContent?.style.transform).toBe(zoomedTransform);
    expect(screen.getByRole("button", { name: "Two-finger mode: Scroll. Switch to Zoom" })).toBeTruthy();

    send.mockClear();
    fireEvent.touchStart(stage, { targetTouches: first });
    fireEvent.touchMove(stage, {
      targetTouches: first.map((touch) => ({ ...touch, clientY: touch.clientY + 30 }))
    });
    fireEvent.touchEnd(stage, { targetTouches: [] });

    await waitFor(() => expect(send.mock.calls.some(([message]) => message.type === "pointer.wheel")).toBe(true));
  });
});
