import { useCallback, type Dispatch, type RefObject, type SetStateAction } from "react";
import { getDefaultDeviceName } from "../platform/clientEnvironment";
import {
  createPcProfile,
  forgetPcProfile,
  renamePcProfile,
  saveActivePcId,
  savePcProfiles,
  upsertPcProfile,
  type PcProfile
} from "./pcProfiles";
import type { ClientMessage, HostStatusMetadata } from "../protocol/messages";
import { deviceNameKey, normalizeDeviceNameInput } from "./clientIdentity";
import { getDisplayPcName, normalizePointerSpeed } from "./connectionProtocol";
import type { ConnectionError, ConnectionState, PairingAttempt } from "./connectionTypes";
import { clearStoredReconnectKey, revokePcPairing } from "./pairingCredentials";

interface PairedPcActionOptions {
  activePcId: string | null;
  clearRuntimeState: () => void;
  clientId: string;
  deviceNameRef: RefObject<string>;
  pairedPcs: PcProfile[];
  screenshotMode: boolean;
  send: (payload: ClientMessage) => void;
  setActivePcId: Dispatch<SetStateAction<string | null>>;
  setDeviceName: Dispatch<SetStateAction<string>>;
  setHostStatus: Dispatch<SetStateAction<HostStatusMetadata | null>>;
  setLastConnectionError: Dispatch<SetStateAction<ConnectionError | null>>;
  setMessage: Dispatch<SetStateAction<string>>;
  setPairedPcs: Dispatch<SetStateAction<PcProfile[]>>;
  setPairingAttempt: Dispatch<SetStateAction<PairingAttempt>>;
  setPendingManualPc: Dispatch<SetStateAction<PcProfile | null>>;
  setState: Dispatch<SetStateAction<ConnectionState>>;
  socketRef: RefObject<WebSocket | null>;
  state: ConnectionState;
}

export function usePairedPcActions(options: PairedPcActionOptions) {
  const {
    activePcId,
    clearRuntimeState,
    clientId,
    deviceNameRef,
    pairedPcs,
    screenshotMode,
    send,
    setActivePcId,
    setDeviceName,
    setHostStatus,
    setLastConnectionError,
    setMessage,
    setPairedPcs,
    setPairingAttempt,
    setPendingManualPc,
    setState,
    socketRef,
    state
  } = options;

  const pairWithToken = useCallback((token: string, pcUrl = window.location.origin, requestedDeviceName?: string) => {
    const nextDeviceName = normalizeDeviceNameInput(requestedDeviceName ?? deviceNameRef.current) ?? getDefaultDeviceName();
    deviceNameRef.current = nextDeviceName;
    localStorage.setItem(deviceNameKey, nextDeviceName);
    setDeviceName(nextDeviceName);

    const profile = createPcProfile(pcUrl);
    setPendingManualPc(null);
    clearRuntimeState();
    setLastConnectionError(null);
    setHostStatus(null);
    setPairedPcs((current) => {
      const next = upsertPcProfile(current, profile);
      savePcProfiles(next);
      return next;
    });
    saveActivePcId(profile.id);
    setActivePcId(profile.id);
    setState("connecting");
    setMessage(`Pairing with ${getDisplayPcName(profile, "", screenshotMode)}...`);
    setPairingAttempt((current) => ({ token, id: current.id + 1 }));
  }, [clearRuntimeState, deviceNameRef, screenshotMode, setActivePcId, setDeviceName, setHostStatus, setLastConnectionError, setMessage, setPairedPcs, setPairingAttempt, setPendingManualPc, setState]);

  const selectPc = useCallback((pcId: string) => {
    setPendingManualPc(null);
    clearRuntimeState();
    setLastConnectionError(null);
    setHostStatus(null);
    setPairingAttempt((current) => ({ token: undefined, id: current.id + 1 }));
    setActivePcId(pcId);
  }, [clearRuntimeState, setActivePcId, setHostStatus, setLastConnectionError, setPairingAttempt, setPendingManualPc]);

  const addManualPc = useCallback((pcUrl: string) => {
    const profile = createPcProfile(pcUrl);
    clearRuntimeState();
    setLastConnectionError(null);
    setHostStatus(null);
    setPendingManualPc(profile);
    setPairingAttempt((current) => ({ token: undefined, id: current.id + 1 }));
    setState("connecting");
    setMessage(`Connecting to ${getDisplayPcName(profile, "", screenshotMode)}...`);
  }, [clearRuntimeState, screenshotMode, setHostStatus, setLastConnectionError, setMessage, setPairingAttempt, setPendingManualPc, setState]);

  const beginNewPairing = useCallback(() => {
    setPendingManualPc(null);
    clearRuntimeState();
    setLastConnectionError(null);
    setHostStatus(null);
    const previousSocket = socketRef.current;
    socketRef.current = null;
    previousSocket?.close();
    setActivePcId(null);
    setPairingAttempt((current) => ({ token: undefined, id: current.id + 1 }));
    setState("needs-pairing");
    setMessage("Confirm the newly scanned pairing QR code.");
  }, [clearRuntimeState, setActivePcId, setHostStatus, setLastConnectionError, setMessage, setPairingAttempt, setPendingManualPc, setState, socketRef]);

  const disconnectActivePc = useCallback(() => {
    if (!activePcId) {
      return;
    }

    setPendingManualPc(null);
    clearRuntimeState();
    setLastConnectionError(null);
    socketRef.current?.close();
    setActivePcId(null);
    setPairingAttempt((current) => ({ token: undefined, id: current.id + 1 }));
    setState("disconnected");
    setMessage("Disconnected. Choose a saved PC or scan a pairing QR.");
  }, [activePcId, clearRuntimeState, setActivePcId, setLastConnectionError, setMessage, setPairingAttempt, setPendingManualPc, setState, socketRef]);

  const forgetPc = useCallback((pcId: string) => {
    const pc = pairedPcs.find((profile) => profile.id === pcId) ?? null;
    if (!pc) {
      return;
    }

    const isActivePc = activePcId === pcId;
    revokePcPairing(pc, clientId, deviceNameRef.current, isActivePc ? socketRef.current : null);
    clearStoredReconnectKey(clientId, pcId);
    setPairedPcs((current) => forgetPcProfile(current, activePcId, pcId).profiles);
    if (!isActivePc) {
      return;
    }

    setPendingManualPc(null);
    clearRuntimeState();
    setLastConnectionError(null);
    socketRef.current?.close();
    setActivePcId(null);
    setPairingAttempt((current) => ({ token: undefined, id: current.id + 1 }));
    setState("needs-pairing");
    setMessage("Disconnected. Choose a saved PC or scan a pairing QR.");
  }, [activePcId, clearRuntimeState, clientId, deviceNameRef, pairedPcs, setActivePcId, setLastConnectionError, setMessage, setPairedPcs, setPairingAttempt, setPendingManualPc, setState, socketRef]);

  const renamePc = useCallback((pcId: string, name: string) => {
    setPairedPcs((current) => renamePcProfile(current, pcId, name));
  }, [setPairedPcs]);

  const renameDevice = useCallback((name: string) => {
    setDeviceName(name);
    if (state === "paired") {
      send({ type: "device.rename", deviceName: name.trim() || getDefaultDeviceName() });
    }
  }, [send, setDeviceName, state]);

  const setHostPointerSpeed = useCallback((pointerSpeed: number) => {
    const normalized = normalizePointerSpeed(pointerSpeed);
    if (normalized === undefined) {
      return;
    }

    setHostStatus((current) => (current ? { ...current, pointerSpeed: normalized } : current));
    if (state === "paired") {
      send({ type: "pointer.speed.set", pointerSpeed: normalized });
    }
  }, [send, setHostStatus, state]);

  const setHostCustomPointer = useCallback((enabled: boolean) => {
    setHostStatus((current) => (current ? { ...current, customPointerEnabled: enabled } : current));
    if (state === "paired") {
      send({ type: "custom.pointer.set", enabled });
    }
  }, [send, setHostStatus, state]);

  return {
    addManualPc,
    beginNewPairing,
    connectManualPc: addManualPc,
    disconnectActivePc,
    forgetPc,
    pairWithToken,
    renameDevice,
    renamePc,
    selectPc,
    setHostCustomPointer,
    setHostPointerSpeed
  };
}
