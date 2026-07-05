import { renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { shouldClearStoredSecretForRejection, useVolturaAirConnection } from "./useVolturaAirConnection";

class MockWebSocket {
  static CONNECTING = 0;
  static OPEN = 1;
  static CLOSING = 2;
  static CLOSED = 3;
  static instances: MockWebSocket[] = [];

  readyState = MockWebSocket.CONNECTING;
  listeners = new Map<string, Array<(event: { data?: string }) => void>>();

  constructor(public url: string) {
    MockWebSocket.instances.push(this);
  }

  addEventListener = vi.fn((type: string, listener: (event: { data?: string }) => void) => {
    this.listeners.set(type, [...(this.listeners.get(type) ?? []), listener]);
  });
  close = vi.fn(() => {
    this.readyState = MockWebSocket.CLOSED;
  });
  send = vi.fn();

  dispatch(type: string, event: { data?: string } = {}) {
    for (const listener of this.listeners.get(type) ?? []) {
      listener(event);
    }
  }
}

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

beforeEach(() => {
  vi.stubGlobal("localStorage", createStorage());
  vi.stubGlobal("matchMedia", vi.fn(() => ({ matches: false })));
  MockWebSocket.instances = [];
  vi.stubGlobal("WebSocket", MockWebSocket);
  window.history.pushState(null, "", "/");
});

describe("shouldClearStoredSecretForRejection", () => {
  it("keeps reconnect secrets for token and protocol-shape pairing failures", () => {
    expect(shouldClearStoredSecretForRejection("stale-token")).toBe(false);
    expect(shouldClearStoredSecretForRejection("expired-token")).toBe(false);
    expect(shouldClearStoredSecretForRejection("invalid-token")).toBe(false);
    expect(shouldClearStoredSecretForRejection("missing-token")).toBe(false);
    expect(shouldClearStoredSecretForRejection("rate-limited")).toBe(false);
    expect(shouldClearStoredSecretForRejection("invalid-message")).toBe(false);
  });

  it("clears reconnect secrets only when the host says the credential was revoked", () => {
    expect(shouldClearStoredSecretForRejection("device-revoked")).toBe(true);
    expect(shouldClearStoredSecretForRejection("secret-revoked")).toBe(true);
  });
});

describe("useVolturaAirConnection", () => {
  it("waits for confirmation before opening a WebSocket from a QR pairing link", async () => {
    window.history.pushState(null, "", "/?t=fresh-token&h=http%3A%2F%2F192.168.1.50%3A51395");

    const { result } = renderHook(() => useVolturaAirConnection());

    await waitFor(() => expect(result.current.state).toBe("needs-pairing"));
    expect(MockWebSocket.instances).toHaveLength(0);
  });

  it("clears a revoked reconnect secret and returns to pairing instead of staying rejected", async () => {
    const pcUrl = "http://pc.local:51395";
    const pc = { customName: false, id: pcUrl, name: "PC", url: pcUrl };
    localStorage.setItem("voltura-air.clientId", "client-a");
    localStorage.setItem("voltura-air.pcProfiles", JSON.stringify([pc]));
    localStorage.setItem("voltura-air.activePcId", pc.id);
    localStorage.setItem(`voltura-air.secret.client-a.${pc.id}`, "old-secret");

    const { result } = renderHook(() => useVolturaAirConnection());

    await waitFor(() => expect(MockWebSocket.instances).toHaveLength(1));
    const socket = MockWebSocket.instances[0];
    socket.readyState = MockWebSocket.OPEN;
    socket.dispatch("open");
    expect(socket.send).toHaveBeenCalledWith(expect.stringContaining("\"secret\":\"old-secret\""));

    socket.dispatch("message", { data: JSON.stringify({ type: "pair.rejected", reason: "secret-revoked" }) });

    await waitFor(() => expect(result.current.state).toBe("needs-pairing"));
    expect(result.current.message).toBe("Saved pairing was removed on the PC. Scan a fresh QR code to pair again.");
    expect(localStorage.getItem(`voltura-air.secret.client-a.${pc.id}`)).toBeNull();
  });
});
