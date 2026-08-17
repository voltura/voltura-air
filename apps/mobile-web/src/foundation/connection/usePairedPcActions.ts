import { useCallback, useRef, type Dispatch, type RefObject, type SetStateAction } from "react";
import { getDefaultDeviceName } from "../platform/clientEnvironment";
import {
  createPcProfile,
  forgetPcProfile,
  renamePcProfile,
  type PcProfile
} from "./pcProfiles";
import type { ClientMessage, HostStatusMetadata } from "../protocol/messages";
import { deviceNameKey, normalizeDeviceNameInput } from "./clientIdentity";
import { getDisplayPcName, normalizePointerSpeed } from "./connectionProtocol";
import type { ConnectionError, ConnectionState, PairingAttempt } from "./connectionTypes";
import { clearStoredReconnectKey } from "./pairingCredentials";
import type { RelayEncryptedSend } from "./relaySessionCrypto";
import type { ControllerSocket } from "./controllerSocket";
import { revokePcPairing } from "./relayPairingRevocation";
import { writeLocalStorage } from "../platform/browserStorage";

interface PairedPcActionOptions {
  activePcId: string | null;
  clearRuntimeState: () => void;
  clientId: string;
  deviceNameRef: RefObject<string>;
  pairedPcs: PcProfile[];
  relayEncryptedSendRef: RefObject<RelayEncryptedSend | null>;
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
  socketRef: RefObject<ControllerSocket | null>;
  state: ConnectionState;
}

export function usePairedPcActions(options: PairedPcActionOptions) {
  const {
    activePcId,
    clearRuntimeState,
    clientId,
    deviceNameRef,
    pairedPcs,
    relayEncryptedSendRef,
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
  const forgettingPcIdsRef = useRef(new Set<string>());

  const pairWithToken = useCallback((token: string, pcUrl = window.location.origin, requestedDeviceName?: string) => {
    const nextDeviceName = normalizeDeviceNameInput(requestedDeviceName ?? deviceNameRef.current) ?? getDefaultDeviceName();
    deviceNameRef.current = nextDeviceName;
    writeLocalStorage(deviceNameKey, nextDeviceName);
    setDeviceName(nextDeviceName);

    const profile = createPcProfile(pcUrl);
    setPendingManualPc(profile);
    clearRuntimeState();
    setLastConnectionError(null);
    setHostStatus(null);
    setState("connecting");
    setMessage(`Pairing with ${getDisplayPcName(profile, "", screenshotMode)}...`);
    setPairingAttempt((current) => ({ token, id: current.id + 1 }));
  }, [clearRuntimeState, deviceNameRef, screenshotMode, setDeviceName, setHostStatus, setLastConnectionError, setMessage, setPairingAttempt, setPendingManualPc, setState]);

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
    if (!pc || forgettingPcIdsRef.current.has(pcId)) {
      return;
    }

    const isActivePc = activePcId === pcId;
    const activeSocket = isActivePc ? socketRef.current : null;
    forgettingPcIdsRef.current.add(pcId);
    if (!isActivePc) {
      setPairedPcs((current) => forgetPcProfile(current, activePcId, pcId).profiles);
    }
    void revokePcPairing(pc, clientId, deviceNameRef.current, activeSocket, isActivePc ? relayEncryptedSendRef.current : null)
      .finally(() => {
        forgettingPcIdsRef.current.delete(pcId);
        clearStoredReconnectKey(clientId, pcId);
        if (!isActivePc) {return;}
        setPairedPcs((current) => forgetPcProfile(current, activePcId, pcId).profiles);
        if (socketRef.current !== activeSocket) {return;}

        setPendingManualPc(null);
        clearRuntimeState();
        setLastConnectionError(null);
        socketRef.current?.close();
        setActivePcId((current) => current === pcId ? null : current);
        setPairingAttempt((current) => ({ token: undefined, id: current.id + 1 }));
        setState("needs-pairing");
        setMessage("Disconnected. Choose a saved PC or scan a pairing QR.");
      })
      .catch(() => undefined);
  }, [activePcId, clearRuntimeState, clientId, deviceNameRef, pairedPcs, relayEncryptedSendRef, setActivePcId, setLastConnectionError, setMessage, setPairedPcs, setPairingAttempt, setPendingManualPc, setState, socketRef]);

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

  const setHostShowModeButtons = useCallback((showModeButtons: boolean) => {
    setHostStatus((current) => (current ? { ...current, showModeButtons } : current));
    if (state === "paired") {
      send({ type: "appearance.mode-buttons.set", showModeButtons });
    }
  }, [send, setHostStatus, state]);

  const setHostControlDepth = useCallback((controlDepth: boolean) => {
    setHostStatus((current) => (current ? { ...current, controlDepth } : current));
    if (state === "paired") {
      send({ type: "appearance.control-depth.set", controlDepth });
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
    setHostControlDepth,
    setHostShowModeButtons,
    setHostPointerSpeed
  };
}
