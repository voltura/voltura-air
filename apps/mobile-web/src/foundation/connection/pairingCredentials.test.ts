import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { revokePcPairing } from "./pairingCredentials";

class MockWebSocket {
  static CONNECTING = 0;
  static OPEN = 1;
  static CLOSING = 2;
  static CLOSED = 3;
  static instances: MockWebSocket[] = [];

  readyState = MockWebSocket.CONNECTING;
  private readonly listeners = new Map<string, ((event: MessageEvent) => void)[]>();

  constructor(url: string) {
    void url;
    MockWebSocket.instances.push(this);
  }

  addEventListener(type: string, listener: (event: MessageEvent) => void) {
    this.listeners.set(type, [...(this.listeners.get(type) ?? []), listener]);
  }

  removeEventListener(type: string, listener: (event: MessageEvent) => void) {
    this.listeners.set(type, (this.listeners.get(type) ?? []).filter((candidate) => candidate !== listener));
  }

  close = vi.fn(() => { this.readyState = MockWebSocket.CLOSED; });
  send = vi.fn();

  dispatch(type: string, data?: string) {
    for (const listener of this.listeners.get(type) ?? []) {
      listener({ data } as MessageEvent);
    }
  }
}

describe("revokePcPairing", () => {
  beforeEach(() => {
    MockWebSocket.instances = [];
    const items = new Map<string, string>();
    vi.stubGlobal("localStorage", {
      clear: () => { items.clear(); },
      getItem: (key: string) => items.get(key) ?? null,
      removeItem: (key: string) => { items.delete(key); },
      setItem: (key: string, value: string) => { items.set(key, value); }
    });
    vi.stubGlobal("WebSocket", MockWebSocket);
    vi.stubGlobal("matchMedia", vi.fn(() => ({ matches: false })));
  });

  afterEach(() => { vi.useRealTimers(); });

  it("closes a rejected best-effort revocation socket", () => {
    localStorage.setItem("voltura-air.secret.client-a.pc-b", "saved-secret");
    revokePcPairing({ customName: false, id: "pc-b", name: "PC B", url: "http://pc-b.local:51395" }, "client-a", "Phone", null);
    const socket = MockWebSocket.instances[0]!;

    socket.readyState = MockWebSocket.OPEN;
    socket.dispatch("open");
    socket.dispatch("message", JSON.stringify({ type: "pair.rejected", reason: "device-revoked" }));

    expect(socket.close).toHaveBeenCalledOnce();
  });

  it("bounds an unopened best-effort revocation socket", () => {
    vi.useFakeTimers();
    localStorage.setItem("voltura-air.secret.client-a.pc-b", "saved-secret");
    revokePcPairing({ customName: false, id: "pc-b", name: "PC B", url: "http://pc-b.local:51395" }, "client-a", "Phone", null);
    const socket = MockWebSocket.instances[0]!;

    vi.advanceTimersByTime(10_000);

    expect(socket.close).toHaveBeenCalledOnce();
  });
});
