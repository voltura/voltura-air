import { act, renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { shouldClearStoredSecretForRejection, useVolturaAirConnection } from "./useVolturaAirConnection";

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

  addEventListener = vi.fn((type: string, listener: (event: { code?: number; data?: string; reason?: string }) => void) => {
    this.listeners.set(type, [...(this.listeners.get(type) ?? []), listener]);
  });
  removeEventListener = vi.fn((type: string, listener: (event: { code?: number; data?: string; reason?: string }) => void) => {
    this.listeners.set(type, (this.listeners.get(type) ?? []).filter((candidate) => candidate !== listener));
  });
  close = vi.fn(() => {
    this.readyState = MockWebSocket.CLOSED;
  });
  send = vi.fn();

  dispatch(type: string, event: { code?: number; data?: string; reason?: string } = {}) {
    for (const listener of this.listeners.get(type) ?? []) {
      listener(event);
    }
  }
}

function dispatchSocketEvent(socket: MockWebSocket, type: string, event: { code?: number; data?: string; reason?: string } = {}) {
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
    window.history.pushState(null, "", `/pair?t=${"a".repeat(32)}&v=0.6.1&h=http%3A%2F%2F192.168.1.50%3A51395`);

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
    localStorage.setItem(`voltura-air.secret.client-a.${pc.id}`, "stored-credential");
    window.history.pushState(null, "", "/pair?t=short&v=0.6.1&h=http%3A%2F%2Fpc-two.local%3A51395");

    const { result } = renderHook(() => useVolturaAirConnection());

    await waitFor(() => { expect(MockWebSocket.instances).toHaveLength(1); });
    expect(getSocket(0).url).toBe("ws://pc.local:51395/ws");
    expect(result.current.activePc).toEqual(pc);
    expect(result.current.pairedPcs).toEqual([pc]);
    expect(JSON.parse(localStorage.getItem("voltura-air.pcProfiles") ?? "[]")).toEqual([pc]);
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
    expect([...socket.listeners.values()].every((listeners) => listeners.length === 0)).toBe(true);
    expect(socket.close).toHaveBeenCalledOnce();
  });

  it("keeps the active WebSocket when PC display metadata changes", async () => {
    const pcUrl = "http://pc.local:51395";
    const pc = { customName: false, id: pcUrl, name: "PC", url: pcUrl };
    localStorage.setItem("voltura-air.clientId", "client-a");
    localStorage.setItem("voltura-air.pcProfiles", JSON.stringify([pc]));
    localStorage.setItem("voltura-air.activePcId", pc.id);
    localStorage.setItem(`voltura-air.secret.client-a.${pc.id}`, "stored-credential");

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
        secret: "fresh-credential",
        paired: true
      })
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

  it("clears a revoked reconnect secret and returns to pairing instead of staying rejected", async () => {
    const pcUrl = "http://pc.local:51395";
    const pc = { customName: false, id: pcUrl, name: "PC", url: pcUrl };
    localStorage.setItem("voltura-air.clientId", "client-a");
    localStorage.setItem("voltura-air.pcProfiles", JSON.stringify([pc]));
    localStorage.setItem("voltura-air.activePcId", pc.id);
    localStorage.setItem(`voltura-air.secret.client-a.${pc.id}`, "stored-credential");

    const { result } = renderHook(() => useVolturaAirConnection());

    await waitFor(() => { expect(MockWebSocket.instances).toHaveLength(1); });
    const socket = getSocket(0);
    socket.readyState = MockWebSocket.OPEN;
    dispatchSocketEvent(socket, "open");
    expect(socket.send).toHaveBeenCalledWith(expect.stringContaining("\"secret\":\"stored-credential\""));

    dispatchSocketEvent(socket, "message", { data: JSON.stringify({ type: "pair.rejected", reason: "secret-revoked" }) });

    await waitFor(() => { expect(result.current.state).toBe("needs-pairing"); });
    expect(result.current.message).toBe("Saved pairing was removed on the PC. Scan a fresh QR code to pair again.");
    expect(localStorage.getItem(`voltura-air.secret.client-a.${pc.id}`)).toBeNull();
    expect(result.current.reconnectablePcs).toHaveLength(0);
  });

  it("keeps an intentionally disconnected PC available for saved reconnect", async () => {
    const pcUrl = "http://pc.local:51395";
    const pc = { customName: true, id: pcUrl, name: "Office PC", url: pcUrl };
    localStorage.setItem("voltura-air.clientId", "client-a");
    localStorage.setItem("voltura-air.pcProfiles", JSON.stringify([pc]));
    localStorage.setItem("voltura-air.activePcId", pc.id);
    localStorage.setItem(`voltura-air.secret.client-a.${pc.id}`, "stored-credential");

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
    localStorage.setItem(`voltura-air.secret.client-a.${pc.id}`, "stored-credential");

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
        secret: "fresh-credential",
        paired: true,
        host: { pointerSpeed: 65 }
      })
    });

    await waitFor(() => { expect(result.current.hostStatus?.pointerSpeed).toBe(65); });
    expect(socket.send).toHaveBeenCalledWith(expect.stringContaining("\"type\":\"status.get\""));
    expect(socket.send).not.toHaveBeenCalledWith(expect.stringContaining("pointerSpeed"));

    act(() => {
      result.current.setHostPointerSpeed(45);
    });

    await waitFor(() => { expect(result.current.hostStatus?.pointerSpeed).toBe(45); });
    expect(socket.send).toHaveBeenCalledWith(JSON.stringify({ type: "pointer.speed.set", pointerSpeed: 45 }));

    act(() => {
      result.current.setHostCustomPointer(true);
    });

    await waitFor(() => { expect(result.current.hostStatus?.customPointerEnabled).toBe(true); });
    expect(socket.send).toHaveBeenCalledWith(JSON.stringify({ type: "custom.pointer.set", enabled: true }));

  });

  it("ignores state-changing messages from obsolete sockets", async () => {
    const pcUrl = "http://pc.local:51395";
    const pc = { customName: false, id: pcUrl, name: "PC", url: pcUrl };
    localStorage.setItem("voltura-air.clientId", "client-a");
    localStorage.setItem("voltura-air.pcProfiles", JSON.stringify([pc]));
    localStorage.setItem("voltura-air.activePcId", pc.id);
    localStorage.setItem(`voltura-air.secret.client-a.${pc.id}`, "stored-credential");

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
        secret: "stale-secret",
        paired: true
      })
    });

    expect(result.current.state).toBe("connecting");
    expect(result.current.message).not.toContain("Stale PC");
    expect(localStorage.getItem(`voltura-air.secret.client-a.${pc.id}`)).toBe("stored-credential");
  });

  it("discards an unreachable manual address without changing the saved PC", async () => {
    const pcUrl = "http://pc.local:51395";
    const pc = { customName: false, id: pcUrl, name: "PC", url: pcUrl };
    localStorage.setItem("voltura-air.clientId", "client-a");
    localStorage.setItem("voltura-air.pcProfiles", JSON.stringify([pc]));
    localStorage.setItem("voltura-air.activePcId", pc.id);
    localStorage.setItem(`voltura-air.secret.client-a.${pc.id}`, "stored-credential");

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
    expect(JSON.parse(localStorage.getItem("voltura-air.pcProfiles") ?? "[]")).toEqual([pc]);
    expect(localStorage.getItem("voltura-air.activePcId")).toBe(pc.id);

    dispatchSocketEvent(candidateSocket, "error");

    await waitFor(() => { expect(MockWebSocket.instances).toHaveLength(3); });
    expect(getSocket(2).url).toBe("ws://pc.local:51395/ws");
    expect(result.current.activePc).toEqual(pc);
    expect(result.current.pairedPcs).toEqual([pc]);
    expect(JSON.parse(localStorage.getItem("voltura-air.pcProfiles") ?? "[]")).toEqual([pc]);
    expect(localStorage.getItem("voltura-air.activePcId")).toBe(pc.id);
    expect(localStorage.getItem(`voltura-air.secret.client-a.${pc.id}`)).toBe("stored-credential");
    expect(localStorage.getItem("voltura-air.secret.client-a.http://jjjjjjjj")).toBeNull();
  });

  it("commits a manual address only after the host accepts it", async () => {
    const pcUrl = "http://pc.local:51395";
    const pc = { customName: false, id: pcUrl, name: "PC", url: pcUrl };
    const candidateUrl = "http://pc-two.local:51395";
    localStorage.setItem("voltura-air.clientId", "client-a");
    localStorage.setItem("voltura-air.pcProfiles", JSON.stringify([pc]));
    localStorage.setItem("voltura-air.activePcId", pc.id);
    localStorage.setItem(`voltura-air.secret.client-a.${pc.id}`, "stored-credential");

    const { result } = renderHook(() => useVolturaAirConnection());

    await waitFor(() => { expect(MockWebSocket.instances).toHaveLength(1); });
    act(() => {
      result.current.addManualPc(candidateUrl);
    });

    await waitFor(() => { expect(MockWebSocket.instances).toHaveLength(2); });
    const candidateSocket = getSocket(1);
    expect(result.current.activePc).toEqual(pc);
    expect(result.current.pairedPcs).toEqual([pc]);
    expect(localStorage.getItem("voltura-air.activePcId")).toBe(pc.id);
    expect(localStorage.getItem(`voltura-air.secret.client-a.${candidateUrl}`)).toBeNull();

    candidateSocket.readyState = MockWebSocket.OPEN;
    dispatchSocketEvent(candidateSocket, "open");
    dispatchSocketEvent(candidateSocket, "message", {
      data: JSON.stringify({
        type: "pair.accepted",
        clientId: "client-a",
        pcName: "Second PC",
        secret: "fresh-credential",
        paired: true
      })
    });

    await waitFor(() => { expect(result.current.activePc?.id).toBe(candidateUrl); });
    expect(result.current.pairedPcs.map((profile) => profile.id)).toEqual([pc.id, candidateUrl]);
    const storedProfiles: unknown = JSON.parse(localStorage.getItem("voltura-air.pcProfiles") ?? "[]");
    expect(Array.isArray(storedProfiles)
      ? (storedProfiles as unknown[]).flatMap((profile) => typeof profile === "object" && profile !== null && typeof (profile as { id?: unknown }).id === "string" ? [(profile as { id: string }).id] : [])
      : []).toEqual([pc.id, candidateUrl]);
    expect(localStorage.getItem("voltura-air.activePcId")).toBe(candidateUrl);
    expect(localStorage.getItem(`voltura-air.secret.client-a.${candidateUrl}`)).toBe("fresh-credential");
  });

  it("marks the connection unavailable when an input action is attempted on a closed socket", async () => {
    const pcUrl = "http://pc.local:51395";
    const pc = { customName: false, id: pcUrl, name: "PC", url: pcUrl };
    localStorage.setItem("voltura-air.clientId", "client-a");
    localStorage.setItem("voltura-air.pcProfiles", JSON.stringify([pc]));
    localStorage.setItem("voltura-air.activePcId", pc.id);
    localStorage.setItem(`voltura-air.secret.client-a.${pc.id}`, "stored-credential");

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
        secret: "fresh-credential",
        paired: true,
        capabilities: { inputAck: true }
      })
    });

    await waitFor(() => { expect(result.current.state).toBe("paired"); });
    socket.readyState = MockWebSocket.CLOSED;

    act(() => {
      result.current.send({ type: "pointer.move", dx: 4, dy: 2 });
    });

    expect(result.current.state).toBe("unavailable");
    expect(result.current.lastConnectionError?.code).toBe("VAIR-PAIR-CLIENT-SEND-FAILED");
    expect(socket.send).not.toHaveBeenCalledWith(expect.stringContaining("\"type\":\"pointer.move\""));
  });

  it("surfaces current input errors without disconnecting the socket", async () => {
    const pcUrl = "http://pc.local:51395";
    const pc = { customName: false, id: pcUrl, name: "PC", url: pcUrl };
    localStorage.setItem("voltura-air.clientId", "client-a");
    localStorage.setItem("voltura-air.pcProfiles", JSON.stringify([pc]));
    localStorage.setItem("voltura-air.activePcId", pc.id);
    localStorage.setItem(`voltura-air.secret.client-a.${pc.id}`, "stored-credential");

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
        secret: "fresh-credential",
        paired: true,
        capabilities: { inputAck: true }
      })
    });

    dispatchSocketEvent(socket, "message", {
      data: JSON.stringify({ type: "input.error", seq: 1, code: "VAIR-INPUT-NATIVE-SEND-FAILED", message: "Windows did not accept this input action. Try again." })
    });

    expect(result.current.state).toBe("paired");
    expect(result.current.message).toBe("Windows did not accept this input action. Try again.");
    expect(result.current.lastConnectionError?.code).toBe("VAIR-INPUT-NATIVE-SEND-FAILED");
    expect(socket.close).not.toHaveBeenCalled();
  });

  it("prevents duplicate power requests and surfaces failures without disconnecting the socket", async () => {
    const pcUrl = "http://pc.local:51395";
    const pc = { customName: false, id: pcUrl, name: "PC", url: pcUrl };
    localStorage.setItem("voltura-air.clientId", "client-a");
    localStorage.setItem("voltura-air.pcProfiles", JSON.stringify([pc]));
    localStorage.setItem("voltura-air.activePcId", pc.id);
    localStorage.setItem(`voltura-air.secret.client-a.${pc.id}`, "stored-credential");

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
        secret: "fresh-credential",
        paired: true
      })
    });

    act(() => {
      result.current.requestPowerAction("lock");
      result.current.requestPowerAction("lock");
    });

    const lockRequests = socket.send.mock.calls
      .map(([payload]) => JSON.parse(payload as string) as { type?: string })
      .filter((payload) => payload.type === "system.power");
    expect(lockRequests).toHaveLength(1);
    expect(result.current.pendingPowerAction).toBe("lock");

    dispatchSocketEvent(socket, "message", {
      data: JSON.stringify({
        type: "system.power.result",
        action: "lock",
        succeeded: false,
        code: "VAIR-POWER-LOCK-DISABLED",
        message: "Windows locking is disabled. Enable it in the Voltura Air host settings."
      })
    });

    expect(result.current.state).toBe("paired");
    expect(result.current.message).toContain("Windows locking is disabled");
    expect(result.current.lastConnectionError?.code).toBe("VAIR-POWER-LOCK-DISABLED");
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
      localStorage.setItem(`voltura-air.secret.client-a.${pc.id}`, "stored-credential");

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
          secret: "fresh-credential",
          paired: true
        })
      });

      act(() => { result.current.requestPowerAction("displayOff"); });
      dispatchSocketEvent(socket, "message", {
        data: JSON.stringify({
          type: "system.power.result",
          action: "displayOff",
          succeeded: true,
          message: "Windows accepted the display-off request. Physical input may be required to wake."
        })
      });

      await act(() => vi.advanceTimersByTime(7500));
      expect(result.current.state).toBe("unavailable");
      expect(socket.close).toHaveBeenCalledTimes(1);
      expect(socket.send).toHaveBeenCalledWith(JSON.stringify({ type: "health.ping" }));
    }
    finally {
      vi.useRealTimers();
    }
  });

  it("keeps stopped hosts on the stable unavailable screen after a socket close", async () => {
    const pcUrl = "http://pc.local:51395";
    const pc = { customName: false, id: pcUrl, name: "PC", url: pcUrl };
    localStorage.setItem("voltura-air.clientId", "client-a");
    localStorage.setItem("voltura-air.pcProfiles", JSON.stringify([pc]));
    localStorage.setItem("voltura-air.activePcId", pc.id);
    localStorage.setItem(`voltura-air.secret.client-a.${pc.id}`, "stored-credential");

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
        secret: "fresh-credential",
        paired: true
      })
    });

    socket.readyState = MockWebSocket.CLOSED;
    dispatchSocketEvent(socket, "close", { code: 1008, reason: "Invalid message" });

    expect(result.current.state).toBe("unavailable");
    expect(socket.removeEventListener).toHaveBeenCalledTimes(4);
    expect([...socket.listeners.values()].every((listeners) => listeners.length === 0)).toBe(true);
    expect(result.current.lastConnectionError?.code).toBe("VAIR-PAIR-SOCKET-CLOSED");
    expect(result.current.message).toBe("PC is currently not available. Check that Voltura Air is running on the PC. Retrying...");
  });

  it("ignores the old socket closing after a new QR pairing begins", async () => {
    const pcUrl = "http://pc.local:51395";
    const pc = { customName: false, id: pcUrl, name: "PC", url: pcUrl };
    localStorage.setItem("voltura-air.clientId", "client-a");
    localStorage.setItem("voltura-air.pcProfiles", JSON.stringify([pc]));
    localStorage.setItem("voltura-air.activePcId", pc.id);
    localStorage.setItem(`voltura-air.secret.client-a.${pc.id}`, "stored-credential");

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
