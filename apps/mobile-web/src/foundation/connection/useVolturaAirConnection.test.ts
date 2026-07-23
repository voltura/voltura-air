import { act, renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { createPairingKeyMaterial } from "./pairingCredentials";
import {
  shouldClearStoredReconnectKeyForRejection,
  useVolturaAirConnection,
} from "./useVolturaAirConnection";

class MockWebSocket {
  static CONNECTING = 0;
  static OPEN = 1;
  static CLOSING = 2;
  static CLOSED = 3;
  static instances: MockWebSocket[] = [];

  readyState = MockWebSocket.CONNECTING;
  listeners = new Map<string, ((event: { code?: number; data?: string; reason?: string }) => void)[]>();

  constructor(public url: string) {
    MockWebSocket.instances.push(this);
  }

  addEventListener = vi.fn(
    (
      type: string,
      listener: (event: {
        code?: number;
        data?: string;
        reason?: string;
      }) => void,
    ) => {
    this.listeners.set(type, [...(this.listeners.get(type) ?? []), listener]);
  },
  );
  removeEventListener = vi.fn(
    (
      type: string,
      listener: (event: {
        code?: number;
        data?: string;
        reason?: string;
      }) => void,
    ) => {
      this.listeners.set(
        type,
        (this.listeners.get(type) ?? []).filter(
          (candidate) => candidate !== listener,
        ),
      );
    },
  );
  close = vi.fn(() => {
    this.readyState = MockWebSocket.CLOSED;
  });
  send = vi.fn();

  dispatch(
    type: string,
    event: { code?: number; data?: string; reason?: string } = {},
  ) {
    for (const listener of this.listeners.get(type) ?? []) {
      listener(event);
    }
  }
}

function dispatchSocketEvent(
  socket: MockWebSocket,
  type: string,
  event: { code?: number; data?: string; reason?: string } = {},
) {
  act(() => {
    socket.dispatch(type, event);
  });
}

function getSocket(index: number): MockWebSocket {
  const socket = MockWebSocket.instances[index];
  if (!socket) {
    throw new Error(`Expected WebSocket instance ${index}.`);
  }

  return socket;
}

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
    },
  };
}

function storeReconnectKey(clientId: string, pcId: string): string {
  const key = createPairingKeyMaterial();
  if (!key) {
    throw new Error("Expected test browser to create reconnect key material.");
  }

  localStorage.setItem(
    `voltura-air.reconnect-key.${clientId}.${pcId}`,
    key.privateKey,
  );
  return key.privateKey;
}

beforeEach(() => {
  vi.stubGlobal("localStorage", createStorage());
  vi.stubGlobal(
    "matchMedia",
    vi.fn(() => ({ matches: false })),
  );
  MockWebSocket.instances = [];
  vi.stubGlobal("WebSocket", MockWebSocket);
  window.history.pushState(null, "", "/");
});
describe("shouldClearStoredReconnectKeyForRejection", () => {
  it("keeps reconnect keys for token and protocol-shape pairing failures", () => {
    expect(shouldClearStoredReconnectKeyForRejection("stale-token")).toBe(
      false,
    );
    expect(shouldClearStoredReconnectKeyForRejection("expired-token")).toBe(
      false,
    );
    expect(shouldClearStoredReconnectKeyForRejection("invalid-token")).toBe(
      false,
    );
    expect(shouldClearStoredReconnectKeyForRejection("rate-limited")).toBe(
      false,
    );
    expect(shouldClearStoredReconnectKeyForRejection("invalid-message")).toBe(
      false,
    );
  });

  it("clears reconnect keys only when the host rejects the registered key", () => {
    expect(shouldClearStoredReconnectKeyForRejection("device-revoked")).toBe(
      true,
    );
    expect(shouldClearStoredReconnectKeyForRejection("invalid-proof")).toBe(
      true,
    );
  });
});
describe("useVolturaAirConnection", () => {
  it("recovers from an unsafe address host hint without persisting or opening it", async () => {
    window.history.pushState(null, "", "/?h=javascript:alert(1)");

    let unmount: (() => void) | undefined;
    expect(() => {
      unmount = renderHook(() => useVolturaAirConnection()).unmount;
    }).not.toThrow();

    await waitFor(() => { expect(MockWebSocket.instances).toHaveLength(1); });
    expect(getSocket(0).url).toMatch(/^wss?:\/\//);
    expect(getSocket(0).url).not.toContain("javascript");
    expect(getSocket(0).url).not.toContain("null");
    expect(localStorage.getItem("voltura-air.pcProfiles")).not.toContain(
      "javascript",
    );
    expect(localStorage.getItem("voltura-air.pcProfiles")).not.toContain(
      '"null"',
    );
    unmount?.();
  });

  it("ignores malformed server messages without exceptions or partial state changes", async () => {
    const pcUrl = "http://pc.local:51395";
    const pc = { customName: false, id: pcUrl, name: "PC", url: pcUrl };
    localStorage.setItem("voltura-air.clientId", "client-a");
    localStorage.setItem("voltura-air.pcProfiles", JSON.stringify([pc]));
    localStorage.setItem("voltura-air.activePcId", pc.id);
    storeReconnectKey("client-a", pc.id);
    const errors: Event[] = [];
    const rejections: Event[] = [];
    const onError = (event: Event) => { errors.push(event); };
    const onRejection = (event: Event) => { rejections.push(event); };
    window.addEventListener("error", onError);
    window.addEventListener("unhandledrejection", onRejection);

    const { result, unmount } = renderHook(() => useVolturaAirConnection());
    await waitFor(() => { expect(MockWebSocket.instances).toHaveLength(1); });
    const socket = getSocket(0);
    socket.readyState = MockWebSocket.OPEN;
    dispatchSocketEvent(socket, "open");
    dispatchSocketEvent(socket, "message", {
      data: JSON.stringify({
        type: "pair.accepted",
        clientId: "client-a",
        pcName: "Office PC",
        paired: true,
        capabilities: {
          volume: true,
          power: {
            lock: true,
            blackoutDisplay: true,
            displayOff: true,
            screenSaver: true,
            screenSaverAvailable: true,
            signOut: true,
            restart: true,
            shutdown: true,
          },
        },
        host: { hostVersion: "0.6.4", pointerSpeed: 55 },
      }),
    });
    dispatchSocketEvent(socket, "message", {
      data: JSON.stringify({ type: "audio.state", volume: 44, muted: false }),
    });
    await waitFor(() => { expect(result.current.audioState?.volume).toBe(44); });
    const snapshot = {
      state: result.current.state,
      message: result.current.message,
      audioState: result.current.audioState,
      hostStatus: result.current.hostStatus,
      powerCapabilities: result.current.powerCapabilities,
      lastConnectionError: result.current.lastConnectionError,
    };

    for (const malformed of [
      { type: "status", connected: true, pcName: {} },
      { type: "pair.accepted", clientId: "client-a", pcName: [], paired: true },
      {
        type: "system.power.result",
        action: "lock",
        succeeded: "yes",
        message: "Done",
      },
      { type: "audio.state", volume: {}, muted: false },
    ]) {
      expect(() => {
        dispatchSocketEvent(socket, "message", {
          data: JSON.stringify(malformed),
        });
      }).not.toThrow();
      expect({
        state: result.current.state,
        message: result.current.message,
        audioState: result.current.audioState,
        hostStatus: result.current.hostStatus,
        powerCapabilities: result.current.powerCapabilities,
        lastConnectionError: result.current.lastConnectionError,
      }).toEqual(snapshot);
    }

    expect(errors).toHaveLength(0);
    expect(rejections).toHaveLength(0);
    expect(socket.close).not.toHaveBeenCalled();
    window.removeEventListener("error", onError);
    window.removeEventListener("unhandledrejection", onRejection);
    unmount();
  });

  it("waits for confirmation before opening a WebSocket from a QR pairing link", async () => {
    window.history.pushState(
      null,
      "",
      `/pair?t=${"a".repeat(32)}&v=0.6.1&h=http%3A%2F%2F192.168.1.50%3A51395`,
    );

    const { result } = renderHook(() => useVolturaAirConnection());

    await waitFor(() => { expect(result.current.state).toBe("needs-pairing"); });
    expect(MockWebSocket.instances).toHaveLength(0);
  });

  it("does not replace a saved active PC from an invalid pairing URL host hint", async () => {
    const pcUrl = "http://pc.local:51395";
    const pc = { customName: false, id: pcUrl, name: "Current PC", url: pcUrl };
    localStorage.setItem("voltura-air.clientId", "client-a");
    localStorage.setItem("voltura-air.pcProfiles", JSON.stringify([pc]));
    localStorage.setItem("voltura-air.activePcId", pc.id);
    storeReconnectKey("client-a", pc.id);
    window.history.pushState(
      null,
      "",
      "/pair?t=short&v=0.6.1&h=http%3A%2F%2Fpc-two.local%3A51395",
    );

    const { result } = renderHook(() => useVolturaAirConnection());

    await waitFor(() => { expect(MockWebSocket.instances).toHaveLength(1); });
    expect(getSocket(0).url).toBe("ws://pc.local:51395/ws");
    expect(result.current.activePc).toEqual(pc);
    expect(result.current.pairedPcs).toEqual([pc]);
    expect(
      JSON.parse(localStorage.getItem("voltura-air.pcProfiles") ?? "[]"),
    ).toEqual([pc]);
    expect(localStorage.getItem("voltura-air.activePcId")).toBe(pc.id);
  });

  it("releases every WebSocket listener when the connection owner unmounts", async () => {
    const pcUrl = "http://pc.local:51395";
    const pc = { customName: false, id: pcUrl, name: "PC", url: pcUrl };
    localStorage.setItem("voltura-air.pcProfiles", JSON.stringify([pc]));
    localStorage.setItem("voltura-air.activePcId", pc.id);

    const { unmount } = renderHook(() => useVolturaAirConnection());

    await waitFor(() => { expect(MockWebSocket.instances).toHaveLength(1); });
    const socket = getSocket(0);
    unmount();

    expect(socket.removeEventListener).toHaveBeenCalledTimes(4);
    expect(
      [...socket.listeners.values()].every(
        (listeners) => listeners.length === 0,
      ),
    ).toBe(true);
    expect(socket.close).toHaveBeenCalledOnce();
  });

  it("keeps the active WebSocket when PC display metadata changes", async () => {
    const pcUrl = "http://pc.local:51395";
    const pc = { customName: false, id: pcUrl, name: "PC", url: pcUrl };
    localStorage.setItem("voltura-air.clientId", "client-a");
    localStorage.setItem("voltura-air.pcProfiles", JSON.stringify([pc]));
    localStorage.setItem("voltura-air.activePcId", pc.id);
    storeReconnectKey("client-a", pc.id);

    const { result } = renderHook(() => useVolturaAirConnection());

    await waitFor(() => { expect(MockWebSocket.instances).toHaveLength(1); });
    const socket = getSocket(0);
    socket.readyState = MockWebSocket.OPEN;
    dispatchSocketEvent(socket, "open");
    dispatchSocketEvent(socket, "message", {
      data: JSON.stringify({
        type: "pair.accepted",
        clientId: "client-a",
        pcName: "Office PC",
        paired: true,
      }),
    });

    await waitFor(() => {
      expect(result.current.state).toBe("paired");
      expect(result.current.activePc?.name).toBe("Office PC");
    });
    expect(MockWebSocket.instances).toHaveLength(1);
    expect(socket.close).not.toHaveBeenCalled();

    act(() => {
      result.current.renamePc(pc.id, "My Desk");
    });

    await waitFor(() => {
      expect(result.current.activePc?.name).toBe("My Desk");
      expect(result.current.message).toBe("Connected to My Desk");
      expect(MockWebSocket.instances).toHaveLength(1);
      expect(socket.close).not.toHaveBeenCalled();
    });
  });

  it("clears a rejected reconnect key and returns to pairing instead of staying rejected", async () => {
    const pcUrl = "http://pc.local:51395";
    const pc = { customName: false, id: pcUrl, name: "PC", url: pcUrl };
    localStorage.setItem("voltura-air.clientId", "client-a");
    localStorage.setItem("voltura-air.pcProfiles", JSON.stringify([pc]));
    localStorage.setItem("voltura-air.activePcId", pc.id);
    storeReconnectKey("client-a", pc.id);

    const { result } = renderHook(() => useVolturaAirConnection());

    await waitFor(() => { expect(MockWebSocket.instances).toHaveLength(1); });
    const socket = getSocket(0);
    socket.readyState = MockWebSocket.OPEN;
    dispatchSocketEvent(socket, "open");
    await waitFor(() => {
      expect(socket.send).toHaveBeenCalledWith(
        expect.stringContaining('"type":"pair.hello"'),
      );
    });
    expect(socket.send).toHaveBeenCalledWith(
      expect.not.stringContaining("reconnect-key"),
    );

    dispatchSocketEvent(socket, "message", {
      data: JSON.stringify({ type: "pair.rejected", reason: "invalid-proof" }),
    });

    await waitFor(() => { expect(result.current.state).toBe("needs-pairing"); });
    expect(result.current.message).toBe(
      "Saved pairing was removed on the PC. Scan a fresh QR code to pair again.",
    );
    expect(
      localStorage.getItem(`voltura-air.reconnect-key.client-a.${pc.id}`),
    ).toBeNull();
    expect(result.current.reconnectablePcs).toHaveLength(0);
  });

  it("keeps an intentionally disconnected PC available for saved reconnect", async () => {
    const pcUrl = "http://pc.local:51395";
    const pc = { customName: true, id: pcUrl, name: "Office PC", url: pcUrl };
    localStorage.setItem("voltura-air.clientId", "client-a");
    localStorage.setItem("voltura-air.pcProfiles", JSON.stringify([pc]));
    localStorage.setItem("voltura-air.activePcId", pc.id);
    storeReconnectKey("client-a", pc.id);

    const { result } = renderHook(() => useVolturaAirConnection());

    await waitFor(() => { expect(MockWebSocket.instances).toHaveLength(1); });
    expect(result.current.reconnectablePcs).toEqual([pc]);

    act(() => { result.current.disconnectActivePc(); });

    await waitFor(() => {
      expect(result.current.activePc).toBeNull();
      expect(result.current.state).toBe("disconnected");
    });
    expect(result.current.pairedPcs).toEqual([pc]);
    expect(result.current.reconnectablePcs).toEqual([pc]);
  });

  it("stores host pointer settings from metadata and sends device overrides", async () => {
    const pcUrl = "http://pc.local:51395";
    const pc = { customName: false, id: pcUrl, name: "PC", url: pcUrl };
    localStorage.setItem("voltura-air.clientId", "client-a");
    localStorage.setItem("voltura-air.pcProfiles", JSON.stringify([pc]));
    localStorage.setItem("voltura-air.activePcId", pc.id);
    storeReconnectKey("client-a", pc.id);

    const { result } = renderHook(() => useVolturaAirConnection());

    await waitFor(() => { expect(MockWebSocket.instances).toHaveLength(1); });
    const socket = getSocket(0);
    socket.readyState = MockWebSocket.OPEN;
    dispatchSocketEvent(socket, "open");
    dispatchSocketEvent(socket, "message", {
      data: JSON.stringify({
        type: "pair.accepted",
        clientId: "client-a",
        pcName: "PC",
        paired: true,
        host: { pointerSpeed: 65 },
      }),
    });

    await waitFor(() => { expect(result.current.hostStatus?.pointerSpeed).toBe(65); });
    expect(socket.send).toHaveBeenCalledWith(
      expect.stringContaining('"type":"status.get"'),
    );
    expect(socket.send).not.toHaveBeenCalledWith(
      expect.stringContaining("pointerSpeed"),
    );

    act(() => {
      result.current.setHostPointerSpeed(45);
    });

    await waitFor(() => { expect(result.current.hostStatus?.pointerSpeed).toBe(45); });
    expect(socket.send).toHaveBeenCalledWith(
      JSON.stringify({ type: "pointer.speed.set", pointerSpeed: 45 }),
    );

    act(() => {
      result.current.setHostCustomPointer(true);
    });

    await waitFor(() => { expect(result.current.hostStatus?.customPointerEnabled).toBe(true); });
    expect(socket.send).toHaveBeenCalledWith(
      JSON.stringify({ type: "custom.pointer.set", enabled: true }),
    );
  });

  it("ignores state-changing messages from obsolete sockets", async () => {
    const pcUrl = "http://pc.local:51395";
    const pc = { customName: false, id: pcUrl, name: "PC", url: pcUrl };
    localStorage.setItem("voltura-air.clientId", "client-a");
    localStorage.setItem("voltura-air.pcProfiles", JSON.stringify([pc]));
    localStorage.setItem("voltura-air.activePcId", pc.id);
    storeReconnectKey("client-a", pc.id);

    const { result } = renderHook(() => useVolturaAirConnection());

    await waitFor(() => { expect(MockWebSocket.instances).toHaveLength(1); });
    const staleSocket = getSocket(0);

    act(() => {
      result.current.addManualPc("http://pc-two.local:51395");
    });

    await waitFor(() => { expect(MockWebSocket.instances).toHaveLength(2); });
    dispatchSocketEvent(staleSocket, "open");
    dispatchSocketEvent(staleSocket, "message", {
      data: JSON.stringify({
        type: "pair.accepted",
        clientId: "client-a",
        pcName: "Stale PC",
        paired: true,
      }),
    });

    expect(result.current.state).toBe("connecting");
    expect(result.current.message).not.toContain("Stale PC");
    expect(
      localStorage.getItem(`voltura-air.reconnect-key.client-a.${pc.id}`),
    ).not.toBeNull();
  });

  it("discards an unreachable manual address without changing the saved PC", async () => {
    const pcUrl = "http://pc.local:51395";
    const pc = { customName: false, id: pcUrl, name: "PC", url: pcUrl };
    localStorage.setItem("voltura-air.clientId", "client-a");
    localStorage.setItem("voltura-air.pcProfiles", JSON.stringify([pc]));
    localStorage.setItem("voltura-air.activePcId", pc.id);
    storeReconnectKey("client-a", pc.id);

    const { result } = renderHook(() => useVolturaAirConnection());

    await waitFor(() => { expect(MockWebSocket.instances).toHaveLength(1); });
    act(() => {
      result.current.addManualPc("http://jjjjjjjj");
    });

    await waitFor(() => { expect(MockWebSocket.instances).toHaveLength(2); });
    const candidateSocket = getSocket(1);
    expect(candidateSocket.url).toBe("ws://jjjjjjjj/ws");
    expect(result.current.activePc).toEqual(pc);
    expect(result.current.pairedPcs).toEqual([pc]);
    expect(
      JSON.parse(localStorage.getItem("voltura-air.pcProfiles") ?? "[]"),
    ).toEqual([pc]);
    expect(localStorage.getItem("voltura-air.activePcId")).toBe(pc.id);

    dispatchSocketEvent(candidateSocket, "error");

    await waitFor(() => { expect(MockWebSocket.instances).toHaveLength(3); });
    expect(getSocket(2).url).toBe("ws://pc.local:51395/ws");
    expect(result.current.activePc).toEqual(pc);
    expect(result.current.pairedPcs).toEqual([pc]);
    expect(
      JSON.parse(localStorage.getItem("voltura-air.pcProfiles") ?? "[]"),
    ).toEqual([pc]);
    expect(localStorage.getItem("voltura-air.activePcId")).toBe(pc.id);
    expect(
      localStorage.getItem(`voltura-air.reconnect-key.client-a.${pc.id}`),
    ).not.toBeNull();
    expect(
      localStorage.getItem(
        "voltura-air.reconnect-key.client-a.http://jjjjjjjj",
      ),
    ).toBeNull();
  });

  it("stores a token pairing reconnect key only after the host accepts it", async () => {
    const pcUrl = "http://pc.local:51395";
    const pc = { customName: false, id: pcUrl, name: "PC", url: pcUrl };
    const candidateUrl = "http://pc-two.local:51395";
    localStorage.setItem("voltura-air.clientId", "client-a");
    localStorage.setItem("voltura-air.pcProfiles", JSON.stringify([pc]));
    localStorage.setItem("voltura-air.activePcId", pc.id);
    storeReconnectKey("client-a", pc.id);

    const { result } = renderHook(() => useVolturaAirConnection());

    await waitFor(() => { expect(MockWebSocket.instances).toHaveLength(1); });
    act(() => {
      result.current.pairWithToken("pair-token", candidateUrl);
    });

    await waitFor(() => { expect(MockWebSocket.instances).toHaveLength(2); });
    const candidateSocket = getSocket(1);
    expect(result.current.activePc?.id).toBe(candidateUrl);
    expect(result.current.pairedPcs.map((profile) => profile.id)).toEqual([
      pc.id,
      candidateUrl,
    ]);
    expect(localStorage.getItem("voltura-air.activePcId")).toBe(candidateUrl);
    expect(
      localStorage.getItem(
        `voltura-air.reconnect-key.client-a.${candidateUrl}`,
      ),
    ).toBeNull();

    candidateSocket.readyState = MockWebSocket.OPEN;
    dispatchSocketEvent(candidateSocket, "open");
    dispatchSocketEvent(candidateSocket, "message", {
      data: JSON.stringify({
        type: "pair.accepted",
        clientId: "client-a",
        pcName: "Second PC",
        paired: true,
      }),
    });

    await waitFor(() => { expect(result.current.activePc?.id).toBe(candidateUrl); });
    expect(result.current.pairedPcs.map((profile) => profile.id)).toEqual([
      pc.id,
      candidateUrl,
    ]);
    const storedProfiles: unknown = JSON.parse(
      localStorage.getItem("voltura-air.pcProfiles") ?? "[]",
    );
    expect(
      Array.isArray(storedProfiles)
        ? (storedProfiles as unknown[]).flatMap((profile) => typeof profile === "object" && profile !== null && typeof (profile as { id?: unknown }).id === "string" ? [(profile as { id: string }).id] : [],
          )
        : [],
    ).toEqual([pc.id, candidateUrl]);
    expect(localStorage.getItem("voltura-air.activePcId")).toBe(candidateUrl);
    expect(
      localStorage.getItem(
        `voltura-air.reconnect-key.client-a.${candidateUrl}`,
      ),
    ).not.toBeNull();
  });

  it("marks the connection unavailable when an input action is attempted on a closed socket", async () => {
    const pcUrl = "http://pc.local:51395";
    const pc = { customName: false, id: pcUrl, name: "PC", url: pcUrl };
    localStorage.setItem("voltura-air.clientId", "client-a");
    localStorage.setItem("voltura-air.pcProfiles", JSON.stringify([pc]));
    localStorage.setItem("voltura-air.activePcId", pc.id);
    storeReconnectKey("client-a", pc.id);

    const { result } = renderHook(() => useVolturaAirConnection());

    await waitFor(() => { expect(MockWebSocket.instances).toHaveLength(1); });
    const socket = getSocket(0);
    socket.readyState = MockWebSocket.OPEN;
    dispatchSocketEvent(socket, "open");
    dispatchSocketEvent(socket, "message", {
      data: JSON.stringify({
        type: "pair.accepted",
        clientId: "client-a",
        pcName: "PC",
        paired: true,
        capabilities: { inputAck: true },
      }),
    });

    await waitFor(() => { expect(result.current.state).toBe("paired"); });
    socket.readyState = MockWebSocket.CLOSED;

    act(() => {
      result.current.send({ type: "pointer.move", dx: 4, dy: 2 });
    });

    expect(result.current.state).toBe("unavailable");
    expect(result.current.lastConnectionError?.code).toBe(
      "VAIR-PAIR-CLIENT-SEND-FAILED",
    );
    expect(socket.send).not.toHaveBeenCalledWith(
      expect.stringContaining('"type":"pointer.move"'),
    );
  });

  it("surfaces current input errors without disconnecting the socket", async () => {
    const pcUrl = "http://pc.local:51395";
    const pc = { customName: false, id: pcUrl, name: "PC", url: pcUrl };
    localStorage.setItem("voltura-air.clientId", "client-a");
    localStorage.setItem("voltura-air.pcProfiles", JSON.stringify([pc]));
    localStorage.setItem("voltura-air.activePcId", pc.id);
    storeReconnectKey("client-a", pc.id);

    const { result } = renderHook(() => useVolturaAirConnection());

    await waitFor(() => { expect(MockWebSocket.instances).toHaveLength(1); });
    const socket = getSocket(0);
    socket.readyState = MockWebSocket.OPEN;
    dispatchSocketEvent(socket, "open");
    dispatchSocketEvent(socket, "message", {
      data: JSON.stringify({
        type: "pair.accepted",
        clientId: "client-a",
        pcName: "PC",
        paired: true,
        capabilities: { inputAck: true },
      }),
    });

    dispatchSocketEvent(socket, "message", {
      data: JSON.stringify({
        type: "input.error",
        seq: 1,
        code: "VAIR-INPUT-NATIVE-SEND-FAILED",
        message: "Windows did not accept this input action. Try again.",
      }),
    });

    expect(result.current.state).toBe("paired");
    expect(result.current.message).toBe(
      "Windows did not accept this input action. Try again.",
    );
    expect(result.current.lastConnectionError?.code).toBe(
      "VAIR-INPUT-NATIVE-SEND-FAILED",
    );
    expect(socket.close).not.toHaveBeenCalled();
  });

  it("prevents duplicate power requests and surfaces failures without disconnecting the socket", async () => {
    const pcUrl = "http://pc.local:51395";
    const pc = { customName: false, id: pcUrl, name: "PC", url: pcUrl };
    localStorage.setItem("voltura-air.clientId", "client-a");
    localStorage.setItem("voltura-air.pcProfiles", JSON.stringify([pc]));
    localStorage.setItem("voltura-air.activePcId", pc.id);
    storeReconnectKey("client-a", pc.id);

    const { result } = renderHook(() => useVolturaAirConnection());

    await waitFor(() => { expect(MockWebSocket.instances).toHaveLength(1); });
    const socket = getSocket(0);
    socket.readyState = MockWebSocket.OPEN;
    dispatchSocketEvent(socket, "open");
    dispatchSocketEvent(socket, "message", {
      data: JSON.stringify({
        type: "pair.accepted",
        clientId: "client-a",
        pcName: "PC",
        paired: true,
      }),
    });

    act(() => {
      result.current.requestPowerAction("lock");
      result.current.requestPowerAction("lock");
    });

    const lockRequests = socket.send.mock.calls
      .map(
        ([payload]) =>
          JSON.parse(payload as string) as {
            type?: string;
            operationId?: string;
          },
      )
      .filter((payload) => payload.type === "system.power");
    expect(lockRequests).toHaveLength(1);
    expect(result.current.pendingPowerAction).toBe("lock");

    dispatchSocketEvent(socket, "message", {
      data: JSON.stringify({
        type: "system.power.result",
        operationId: "stale-power-result",
        action: "lock",
        succeeded: true,
        message: "Stale result",
      }),
    });
    expect(result.current.pendingPowerAction).toBe("lock");
    expect(result.current.message).toBe("Connected to PC");
    expect(result.current.powerActionResult).toBeNull();

    dispatchSocketEvent(socket, "message", {
      data: JSON.stringify({
        type: "system.power.result",
        operationId: lockRequests[0]!.operationId,
        action: "lock",
        succeeded: false,
        code: "VAIR-POWER-LOCK-DISABLED",
        message: "Windows locking is disabled. Enable it in the Voltura Air host settings.",
      }),
    });

    expect(result.current.state).toBe("paired");
    expect(result.current.message).toContain("Windows locking is disabled");
    expect(result.current.lastConnectionError?.code).toBe(
      "VAIR-POWER-LOCK-DISABLED",
    );
    expect(result.current.pendingPowerAction).toBeNull();
    expect(result.current.powerActionResult?.succeeded).toBe(false);
    expect(socket.close).not.toHaveBeenCalled();
  });

  it("reports the host unavailable promptly when display off suspends the connection", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    try {
      const pcUrl = "http://pc.local:51395";
      const pc = { customName: false, id: pcUrl, name: "PC", url: pcUrl };
      localStorage.setItem("voltura-air.clientId", "client-a");
      localStorage.setItem("voltura-air.pcProfiles", JSON.stringify([pc]));
      localStorage.setItem("voltura-air.activePcId", pc.id);
      storeReconnectKey("client-a", pc.id);

      const { result } = renderHook(() => useVolturaAirConnection());

      await waitFor(() => { expect(MockWebSocket.instances).toHaveLength(1); });
      const socket = getSocket(0);
      socket.readyState = MockWebSocket.OPEN;
      dispatchSocketEvent(socket, "open");
      dispatchSocketEvent(socket, "message", {
        data: JSON.stringify({
          type: "pair.accepted",
          clientId: "client-a",
          pcName: "PC",
          paired: true,
        }),
      });

      act(() => { result.current.requestPowerAction("displayOff"); });
      const displayOffRequest = socket.send.mock.calls
        .map(
          ([payload]) =>
            JSON.parse(payload as string) as {
              type?: string;
              operationId?: string;
            },
        )
        .find((payload) => payload.type === "system.power");
      dispatchSocketEvent(socket, "message", {
        data: JSON.stringify({
          type: "system.power.result",
          operationId: displayOffRequest?.operationId,
          action: "displayOff",
          succeeded: true,
          message: "Windows accepted the display-off request. Physical input may be required to wake.",
        }),
      });

      await act(() => vi.advanceTimersByTime(7500));
      expect(result.current.state).toBe("unavailable");
      expect(socket.close).toHaveBeenCalledTimes(1);
      expect(socket.send).toHaveBeenCalledWith(
        JSON.stringify({ type: "health.ping" }),
      );
    } finally {
      vi.useRealTimers();
    }
  });

  it("keeps stopped hosts on the stable unavailable screen after a socket close", async () => {
    const pcUrl = "http://pc.local:51395";
    const pc = { customName: false, id: pcUrl, name: "PC", url: pcUrl };
    localStorage.setItem("voltura-air.clientId", "client-a");
    localStorage.setItem("voltura-air.pcProfiles", JSON.stringify([pc]));
    localStorage.setItem("voltura-air.activePcId", pc.id);
    storeReconnectKey("client-a", pc.id);

    const { result } = renderHook(() => useVolturaAirConnection());

    await waitFor(() => { expect(MockWebSocket.instances).toHaveLength(1); });
    const socket = getSocket(0);
    socket.readyState = MockWebSocket.OPEN;
    dispatchSocketEvent(socket, "open");
    dispatchSocketEvent(socket, "message", {
      data: JSON.stringify({
        type: "pair.accepted",
        clientId: "client-a",
        pcName: "PC",
        paired: true,
      }),
    });

    socket.readyState = MockWebSocket.CLOSED;
    dispatchSocketEvent(socket, "close", {
      code: 1008,
      reason: "Invalid message",
    });

    expect(result.current.state).toBe("unavailable");
    expect(socket.removeEventListener).toHaveBeenCalledTimes(4);
    expect(
      [...socket.listeners.values()].every(
        (listeners) => listeners.length === 0,
      ),
    ).toBe(true);
    expect(result.current.lastConnectionError?.code).toBe(
      "VAIR-PAIR-SOCKET-CLOSED",
    );
    expect(result.current.message).toBe(
      "PC is currently not available. Check that Voltura Air is running on the PC. Retrying...",
    );
  });

  it("forgets only an inactive saved PC and ignores unknown or repeated requests", async () => {
    const active = {
      customName: false,
      id: "http://pc-a.local:51395",
      name: "PC A",
      url: "http://pc-a.local:51395",
    };
    const inactive = {
      customName: false,
      id: "http://pc-b.local:51395",
      name: "PC B",
      url: "http://pc-b.local:51395",
    };
    localStorage.setItem("voltura-air.clientId", "client-a");
    localStorage.setItem(
      "voltura-air.pcProfiles",
      JSON.stringify([active, inactive]),
    );
    localStorage.setItem("voltura-air.activePcId", active.id);
    storeReconnectKey("client-a", active.id);
    storeReconnectKey("client-a", inactive.id);

    const { result } = renderHook(() => useVolturaAirConnection());
    await waitFor(() => { expect(MockWebSocket.instances).toHaveLength(1); });
    const socket = getSocket(0);
    socket.readyState = MockWebSocket.OPEN;
    dispatchSocketEvent(socket, "open");
    dispatchSocketEvent(socket, "message", {
      data: JSON.stringify({
        type: "pair.accepted",
        clientId: "client-a",
        pcName: "PC A",
        paired: true,
        capabilities: {
          awake: { canControl: true, active: true, mode: "indefinite" },
          clipboardRead: true,
          power: {
            lock: true,
            blackoutDisplay: true,
            displayOff: true,
            screenSaver: true,
            screenSaverAvailable: true,
            signOut: true,
            restart: true,
            shutdown: true,
          },
          presentation: { canControl: true, canSaveReports: true, laserPointerActive: false },
          remoteLaunch: true,
          textTransfer: true,
          urlOpen: { canOpen: true },
          volume: true,
        },
        host: { hostVersion: "0.6.4", pointerSpeed: 60, pcName: "PC A" },
      }),
    });
    dispatchSocketEvent(socket, "message", {
      data: JSON.stringify({ type: "audio.state", volume: 38, muted: true }),
    });
    await waitFor(() => { expect(result.current.audioState?.volume).toBe(38); });
    const runtimeSnapshot = () => ({
      activePc: result.current.activePc,
      state: result.current.state,
      message: result.current.message,
      audioState: result.current.audioState,
      awakeCapability: result.current.awakeCapability,
      clipboardReadPermission: result.current.clipboardReadPermission,
      hostStatus: result.current.hostStatus,
      powerCapabilities: result.current.powerCapabilities,
      presentationCapability: result.current.presentationCapability,
      supportsRemoteLaunch: result.current.supportsRemoteLaunch,
      supportsTextTransfer: result.current.supportsTextTransfer,
      supportsVolumeControl: result.current.supportsVolumeControl,
      urlOpenCapability: result.current.urlOpenCapability,
    });
    const expectedRuntime = runtimeSnapshot();

    act(() => { result.current.forgetPc("http://missing.local:51395"); });
    expect(runtimeSnapshot()).toEqual(expectedRuntime);
    expect(result.current.pairedPcs).toEqual([active, inactive]);

    act(() => { result.current.forgetPc(inactive.id); });
    await waitFor(() => { expect(result.current.pairedPcs).toEqual([active]); });
    expect(runtimeSnapshot()).toEqual(expectedRuntime);
    expect(
      localStorage.getItem(`voltura-air.reconnect-key.client-a.${inactive.id}`),
    ).toBeNull();
    expect(socket.close).not.toHaveBeenCalled();
    expect(getSocket(1).url).toBe("ws://pc-b.local:51395/ws");

    act(() => { result.current.forgetPc(inactive.id); });
    expect(runtimeSnapshot()).toEqual(expectedRuntime);
    expect(result.current.pairedPcs).toEqual([active]);
    expect(socket.close).not.toHaveBeenCalled();
    expect(MockWebSocket.instances).toHaveLength(2);
  });

  it("forgets the active PC while retaining another saved profile", async () => {
    const active = {
      customName: false,
      id: "http://pc-a.local:51395",
      name: "PC A",
      url: "http://pc-a.local:51395",
    };
    const inactive = {
      customName: false,
      id: "http://pc-b.local:51395",
      name: "PC B",
      url: "http://pc-b.local:51395",
    };
    localStorage.setItem("voltura-air.clientId", "client-a");
    localStorage.setItem(
      "voltura-air.pcProfiles",
      JSON.stringify([active, inactive]),
    );
    localStorage.setItem("voltura-air.activePcId", active.id);
    storeReconnectKey("client-a", active.id);
    storeReconnectKey("client-a", inactive.id);
    const { result } = renderHook(() => useVolturaAirConnection());
    await waitFor(() => { expect(MockWebSocket.instances).toHaveLength(1); });
    const socket = getSocket(0);

    act(() => { result.current.forgetPc(active.id); });

    await waitFor(() => { expect(result.current.state).toBe("needs-pairing"); });
    expect(result.current.activePc).toBeNull();
    expect(result.current.pairedPcs).toEqual([inactive]);
    expect(result.current.hostStatus).toBeNull();
    expect(result.current.powerCapabilities).toBeNull();
    expect(
      localStorage.getItem(`voltura-air.reconnect-key.client-a.${active.id}`),
    ).toBeNull();
    expect(
      localStorage.getItem(`voltura-air.reconnect-key.client-a.${inactive.id}`),
    ).not.toBeNull();
    expect(socket.close).toHaveBeenCalled();
  });

  it("forgets the only active PC and leaves a clean pairing state", async () => {
    const active = {
      customName: false,
      id: "http://pc-a.local:51395",
      name: "PC A",
      url: "http://pc-a.local:51395",
    };
    localStorage.setItem("voltura-air.clientId", "client-a");
    localStorage.setItem("voltura-air.pcProfiles", JSON.stringify([active]));
    localStorage.setItem("voltura-air.activePcId", active.id);
    storeReconnectKey("client-a", active.id);
    const { result } = renderHook(() => useVolturaAirConnection());
    await waitFor(() => { expect(MockWebSocket.instances).toHaveLength(1); });
    const socket = getSocket(0);

    act(() => { result.current.forgetPc(active.id); });

    await waitFor(() => { expect(result.current.state).toBe("needs-pairing"); });
    expect(result.current.activePc).toBeNull();
    expect(result.current.pairedPcs).toEqual([]);
    expect(result.current.audioState).toBeNull();
    expect(result.current.hostStatus).toBeNull();
    expect(localStorage.getItem("voltura-air.activePcId")).toBeNull();
    expect(socket.close).toHaveBeenCalled();
  });

  it("ignores the old socket closing after a new QR pairing begins", async () => {
    const pcUrl = "http://pc.local:51395";
    const pc = { customName: false, id: pcUrl, name: "PC", url: pcUrl };
    localStorage.setItem("voltura-air.clientId", "client-a");
    localStorage.setItem("voltura-air.pcProfiles", JSON.stringify([pc]));
    localStorage.setItem("voltura-air.activePcId", pc.id);
    storeReconnectKey("client-a", pc.id);

    const { result } = renderHook(() => useVolturaAirConnection());

    await waitFor(() => { expect(MockWebSocket.instances).toHaveLength(1); });
    const oldSocket = getSocket(0);
    oldSocket.readyState = MockWebSocket.OPEN;
    dispatchSocketEvent(oldSocket, "open");
    oldSocket.readyState = MockWebSocket.CLOSED;
    dispatchSocketEvent(oldSocket, "close");
    await waitFor(() => { expect(result.current.state).toBe("unavailable"); });

    act(() => {
      result.current.beginNewPairing();
    });
    dispatchSocketEvent(oldSocket, "close");

    await waitFor(() => { expect(result.current.state).toBe("needs-pairing"); });
    expect(result.current.activePc).toBeNull();
    expect(result.current.pairedPcs).toContainEqual(pc);
    expect(result.current.message).toBe("Choose a PC or scan a pairing QR.");
  });
});
