import { act, renderHook } from "@testing-library/react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { usePairedPcActions } from "./usePairedPcActions";
import { createPairingKeyMaterial } from "./pairingCredentials";
import { appSettingsKey, remoteSettingsKey, trackpadSettingsKey } from "../settings/appStorage";
const revocation = vi.hoisted(() => vi.fn<() => Promise<boolean>>());
vi.mock("./relayPairingRevocation", () => ({ revokePcPairing: revocation }));
import { readLocalStorage, writeLocalStorage } from "../platform/browserStorage";
afterEach(() => vi.restoreAllMocks());
const key = "voltura-air.reconnect-key.client-a.pc";
const settings = [
  appSettingsKey("client-a", "pc"),
  remoteSettingsKey("client-a", "pc"),
  trackpadSettingsKey("client-a", "pc"),
];
beforeEach(() => {
  revocation.mockReset();
  writeLocalStorage(key, createPairingKeyMaterial()!.privateKey);
  settings.forEach((name) => writeLocalStorage(name, "saved"));
});
function setup(overrides: Partial<Parameters<typeof usePairedPcActions>[0]> = {}) {
  const props: Parameters<typeof usePairedPcActions>[0] = {
    activePcId: null,
    clearRuntimeState: vi.fn(),
    clientId: "client-a",
    deviceNameRef: { current: "Phone" },
    pairedPcs: [{ customName: false, id: "pc", name: "PC", url: "https://pc.invalid" }],
    relayEncryptedSendRef: { current: null },
    screenshotMode: false,
    send: vi.fn(),
    setActivePcId: vi.fn(),
    setDeviceName: vi.fn(),
    setHostStatus: vi.fn(),
    setLastConnectionError: vi.fn(),
    setMessage: vi.fn(),
    setPairedPcs: vi.fn(),
    setPairingAttempt: vi.fn(),
    setPendingManualPc: vi.fn(),
    setState: vi.fn(),
    socketRef: { current: null },
    state: "paired",
    ...overrides,
  };
  return { ...renderHook(() => usePairedPcActions(props)), props };
}
it.each([false, true])(
  "keeps setting cleanup conditional on confirmed revocation=%s",
  async (confirmed) => {
    revocation.mockResolvedValue(confirmed);
    const hook = setup();
    await act(async () => {
      hook.result.current.forgetPc("pc");
      await Promise.resolve();
    });
    settings.forEach((name) => expect(localStorage.getItem(name)).toBe(confirmed ? null : "saved"));
    expect(localStorage.getItem(key) === null).toBe(confirmed);
  },
);
it("retains settings and pairing after revocation throws", async () => {
  revocation.mockRejectedValue(new Error("Revocation failed"));
  const hook = setup();
  await act(async () => {
    hook.result.current.forgetPc("pc");
    await Promise.resolve();
  });
  settings.forEach((name) => expect(localStorage.getItem(name)).toBe("saved"));
  expect(localStorage.getItem(key)).not.toBeNull();
  expect(hook.props.setPairedPcs).not.toHaveBeenCalled();
});
it("does not let an old Forget completion remove a replacement pairing", async () => {
  let finish!: (value: boolean) => void;
  revocation.mockImplementation(
    () =>
      new Promise((resolve) => {
        finish = resolve;
      }),
  );
  const hook = setup();
  act(() => hook.result.current.forgetPc("pc"));
  const replacement = createPairingKeyMaterial()!.privateKey;
  localStorage.setItem(key, replacement);
  await act(async () => {
    finish(true);
    await Promise.resolve();
  });
  expect(localStorage.getItem(key) === replacement).toBe(true);
  settings.forEach((name) => expect(localStorage.getItem(name)).toBe("saved"));
  expect(hook.props.setPairedPcs).not.toHaveBeenCalled();
});

it("keeps data while revocation is pending and deduplicates repeated Forget requests", async () => {
  let finish!: (value: boolean) => void;
  revocation.mockImplementation(
    () =>
      new Promise((resolve) => {
        finish = resolve;
      }),
  );
  const hook = setup();
  act(() => {
    hook.result.current.forgetPc("pc");
    hook.result.current.forgetPc("pc");
  });
  expect(revocation).toHaveBeenCalledOnce();
  settings.forEach((name) => expect(readLocalStorage(name)).toBe("saved"));
  expect(hook.props.setPairedPcs).not.toHaveBeenCalled();
  await act(async () => {
    finish(true);
    await Promise.resolve();
  });
  settings.forEach((name) => expect(readLocalStorage(name)).toBeNull());
});
it("does not close a replacement active socket when old revocation completes", async () => {
  let finish!: (value: boolean) => void;
  revocation.mockImplementation(
    () =>
      new Promise((resolve) => {
        finish = resolve;
      }),
  );
  const oldSocket = Object.assign(new EventTarget(), { close: vi.fn() }) as unknown as WebSocket;
  const nextSocket = Object.assign(new EventTarget(), { close: vi.fn() }) as unknown as WebSocket;
  const socketRef = { current: oldSocket };
  const hook = setup({ activePcId: "pc", socketRef });
  act(() => hook.result.current.forgetPc("pc"));
  socketRef.current = nextSocket;
  await act(async () => {
    finish(true);
    await Promise.resolve();
  });
  expect(nextSocket.close).not.toHaveBeenCalled();
  expect(hook.props.setState).not.toHaveBeenCalled();
  expect(hook.props.setActivePcId).not.toHaveBeenCalled();
});
it.each([key, ...settings])(
  "contains a rejected local cleanup boundary for %s",
  async (blocked) => {
    revocation.mockResolvedValue(true);
    const remove = Storage.prototype.removeItem;
    vi.spyOn(Storage.prototype, "removeItem").mockImplementation(function (
      this: Storage,
      name: string,
    ) {
      if (name === blocked) {
        throw new DOMException("Storage blocked", "SecurityError");
      }
      remove.call(this, name);
    });
    const hook = setup();
    await act(async () => {
      hook.result.current.forgetPc("pc");
      await Promise.resolve();
    });
    expect(readLocalStorage(key)).toBeNull();
    settings.forEach((name) => expect(readLocalStorage(name)).toBeNull());
    expect(hook.props.setPairedPcs).toHaveBeenCalledOnce();
  },
);
