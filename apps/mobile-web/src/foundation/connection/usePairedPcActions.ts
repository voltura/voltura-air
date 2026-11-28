import { useCallback, useRef, type Dispatch, type RefObject, type SetStateAction } from "react";
import { getDefaultDeviceName } from "../platform/clientEnvironment";
import {
  createPcProfile,
  forgetPcProfile,
  renamePcProfile,
  selectPcProfile,
  type PcProfile,
} from "./pcProfiles";
import type {
  ClientMessage,
  HostStatusMetadata,
  ScreenViewSoundQuality,
} from "../protocol/messages";
import { deviceNameKey, normalizeDeviceNameInput } from "./clientIdentity";
import { getDisplayPcName, normalizePointerSpeed } from "./connectionProtocol";
import type { ConnectionError, ConnectionState, PairingAttempt } from "./connectionTypes";
import { captureReconnectKeyOwnership, clearStoredReconnectKey } from "./pairingCredentials";
import {
  clearAppSettings,
  clearRemoteSettings,
  clearTrackpadSettings,
} from "../settings/appStorage";
import type { RelayEncryptedSend } from "./relaySessionCrypto";
import type { ControllerSocket } from "./controllerSocket";
import { revokePcPairing } from "./relayPairingRevocation";
import { writeLocalStorage } from "../platform/browserStorage";
import { normalizeAccentColor } from "../protocol/accentColorProtocol";

interface PairedPcActionOptions {
  activePcId: string | null;
  authenticatedSocketRef: RefObject<ControllerSocket | null>;
  clearRuntimeState: () => void;
  clientId: string;
  connectionPc: PcProfile | null;
  deviceNameRef: RefObject<string>;
  pairedPcs: PcProfile[];
  relayEncryptedSendRef: RefObject<RelayEncryptedSend | null>;
  retireConnectionRef: RefObject<(() => void) | null>;
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
    authenticatedSocketRef,
    clearRuntimeState,
    clientId,
    connectionPc,
    deviceNameRef,
    pairedPcs,
    relayEncryptedSendRef,
    retireConnectionRef,
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
    state,
  } = options;
  const forgettingPcIdsRef = useRef(new Set<string>());

  const pairWithToken = useCallback(
    (token: string, pcUrl = window.location.origin, requestedDeviceName?: string) => {
      const nextDeviceName =
        normalizeDeviceNameInput(requestedDeviceName ?? deviceNameRef.current) ??
        getDefaultDeviceName();
      deviceNameRef.current = nextDeviceName;
      writeLocalStorage(deviceNameKey, nextDeviceName);
      setDeviceName(nextDeviceName);

      const profile = createPcProfile(pcUrl);
      retireConnectionRef.current?.();
      setPendingManualPc(profile);
      clearRuntimeState();
      setLastConnectionError(null);
      setHostStatus(null);
      setState("connecting");
      setMessage(`Pairing with ${getDisplayPcName(profile, "", screenshotMode)}...`);
      setPairingAttempt((current) => ({ token, id: current.id + 1 }));
    },
    [
      clearRuntimeState,
      deviceNameRef,
      retireConnectionRef,
      screenshotMode,
      setDeviceName,
      setHostStatus,
      setLastConnectionError,
      setMessage,
      setPairingAttempt,
      setPendingManualPc,
      setState,
    ],
  );

  const selectPc = useCallback(
    (pcId: string) => {
      const selectedPc = selectPcProfile(pairedPcs, pcId);
      if (!selectedPc) {
        return;
      }

      retireConnectionRef.current?.();
      setPendingManualPc(null);
      clearRuntimeState();
      setLastConnectionError(null);
      setHostStatus(null);
      setPairingAttempt((current) => ({ token: undefined, id: current.id + 1 }));
      setActivePcId(pcId);
      setState("connecting");
      setMessage(`Connecting to ${getDisplayPcName(selectedPc, "", screenshotMode)}...`);
    },
    [
      clearRuntimeState,
      pairedPcs,
      retireConnectionRef,
      screenshotMode,
      setActivePcId,
      setHostStatus,
      setLastConnectionError,
      setMessage,
      setPairingAttempt,
      setPendingManualPc,
      setState,
    ],
  );

  const addManualPc = useCallback(
    (pcUrl: string) => {
      const target = createPcProfile(pcUrl);
      const saved = pairedPcs.find((pc) => pc.id === target.id && pc.url === target.url);
      if (saved?.id === activePcId) {
        selectPc(saved.id);
        return;
      }

      const profile = saved ?? target;
      retireConnectionRef.current?.();
      clearRuntimeState();
      setLastConnectionError(null);
      setHostStatus(null);
      setPendingManualPc(profile);
      setPairingAttempt((current) => ({ token: undefined, id: current.id + 1 }));
      setState("connecting");
      setMessage(`Connecting to ${getDisplayPcName(profile, "", screenshotMode)}...`);
    },
    [
      activePcId,
      clearRuntimeState,
      pairedPcs,
      retireConnectionRef,
      screenshotMode,
      selectPc,
      setHostStatus,
      setLastConnectionError,
      setMessage,
      setPairingAttempt,
      setPendingManualPc,
      setState,
    ],
  );

  const beginNewPairing = useCallback(() => {
    retireConnectionRef.current?.();
    setPendingManualPc(null);
    clearRuntimeState();
    setLastConnectionError(null);
    setHostStatus(null);
    setActivePcId(null);
    setPairingAttempt((current) => ({ token: undefined, id: current.id + 1 }));
    setState("needs-pairing");
    setMessage("Confirm the newly scanned pairing QR code.");
  }, [
    clearRuntimeState,
    retireConnectionRef,
    setActivePcId,
    setHostStatus,
    setLastConnectionError,
    setMessage,
    setPairingAttempt,
    setPendingManualPc,
    setState,
  ]);

  const disconnectActivePc = useCallback(() => {
    if (!activePcId) {
      return;
    }

    retireConnectionRef.current?.();
    setPendingManualPc(null);
    clearRuntimeState();
    setLastConnectionError(null);
    setActivePcId(null);
    setPairingAttempt((current) => ({ token: undefined, id: current.id + 1 }));
    setState("disconnected");
    setMessage("Disconnected. Choose a saved PC or scan a pairing QR.");
  }, [
    activePcId,
    clearRuntimeState,
    retireConnectionRef,
    setActivePcId,
    setLastConnectionError,
    setMessage,
    setPairingAttempt,
    setPendingManualPc,
    setState,
  ]);

  const forgetPc = useCallback(
    (pcId: string) => {
      const pc = pairedPcs.find((profile) => profile.id === pcId) ?? null;
      if (!pc || forgettingPcIdsRef.current.has(pcId)) {
        return;
      }

      // A pending candidate may reuse a saved ID without being the committed profile.
      const ownsSelectedConnection = activePcId === pcId && connectionPc === pc;
      const connectionSocket = socketRef.current;
      const retireConnection = retireConnectionRef.current;
      const activeSocket =
        ownsSelectedConnection && authenticatedSocketRef.current === connectionSocket
          ? connectionSocket
          : null;
      const ownsReconnectKey = captureReconnectKeyOwnership(clientId, pcId);
      forgettingPcIdsRef.current.add(pcId);
      void revokePcPairing(
        pc,
        clientId,
        deviceNameRef.current,
        activeSocket,
        activeSocket ? relayEncryptedSendRef.current : null,
      )
        .then((revoked) => {
          forgettingPcIdsRef.current.delete(pcId);
          if (!revoked || !ownsReconnectKey()) {
            return;
          }

          clearStoredReconnectKey(clientId, pcId);
          clearTrackpadSettings(clientId, pcId);
          clearRemoteSettings(clientId, pcId);
          clearAppSettings(clientId, pcId);
          setPairedPcs((current) => forgetPcProfile(current, activePcId, pcId).profiles);
          if (
            activePcId !== pcId ||
            retireConnectionRef.current !== retireConnection ||
            (ownsSelectedConnection && socketRef.current !== connectionSocket)
          ) {
            return;
          }

          setActivePcId((current) => (current === pcId ? null : current));
          if (!ownsSelectedConnection) {
            return;
          }
          retireConnection?.();
          setPendingManualPc(null);
          clearRuntimeState();
          setLastConnectionError(null);
          setPairingAttempt((current) => ({ token: undefined, id: current.id + 1 }));
          setState("needs-pairing");
          setMessage("Disconnected. Choose a saved PC or scan a pairing QR.");
        })
        .catch(() => {
          forgettingPcIdsRef.current.delete(pcId);
        });
    },
    [
      activePcId,
      authenticatedSocketRef,
      clearRuntimeState,
      clientId,
      connectionPc,
      deviceNameRef,
      pairedPcs,
      relayEncryptedSendRef,
      retireConnectionRef,
      setActivePcId,
      setLastConnectionError,
      setMessage,
      setPairedPcs,
      setPairingAttempt,
      setPendingManualPc,
      setState,
      socketRef,
    ],
  );

  const renamePc = useCallback(
    (pcId: string, name: string) => {
      setPairedPcs((current) => renamePcProfile(current, pcId, name));
    },
    [setPairedPcs],
  );

  const renameDevice = useCallback(
    (name: string) => {
      setDeviceName(name);
      if (state === "paired") {
        send({ type: "device.rename", deviceName: name.trim() || getDefaultDeviceName() });
      }
    },
    [send, setDeviceName, state],
  );

  const setHostPointerSpeed = useCallback(
    (pointerSpeed: number) => {
      const normalized = normalizePointerSpeed(pointerSpeed);
      if (normalized === undefined) {
        return;
      }

      setHostStatus((current) => (current ? { ...current, pointerSpeed: normalized } : current));
      if (state === "paired") {
        send({ type: "pointer.speed.set", pointerSpeed: normalized });
      }
    },
    [send, setHostStatus, state],
  );

  const setHostShowModeButtons = useCallback(
    (showModeButtons: boolean) => {
      setHostStatus((current) => (current ? { ...current, showModeButtons } : current));
      if (state === "paired") {
        send({ type: "appearance.mode-buttons.set", showModeButtons });
      }
    },
    [send, setHostStatus, state],
  );

  const setHostControlDepth = useCallback(
    (controlDepth: boolean) => {
      setHostStatus((current) => (current ? { ...current, controlDepth } : current));
      if (state === "paired") {
        send({ type: "appearance.control-depth.set", controlDepth });
      }
    },
    [send, setHostStatus, state],
  );

  const setHostAccentColor = useCallback(
    (accentColor: string | null) => {
      const normalized = accentColor === null ? null : normalizeAccentColor(accentColor);
      if (accentColor !== null && !normalized) {
        return;
      }

      setHostStatus((current) =>
        current
          ? {
              ...current,
              ...(normalized === null ? {} : { accentColor: normalized }),
              accentColorOverridden: normalized !== null,
            }
          : current,
      );
      if (state === "paired") {
        send({ type: "appearance.accent-color.set", accentColor: normalized });
      }
    },
    [send, setHostStatus, state],
  );

  const setHostScreenSoundQuality = useCallback(
    (soundQuality: ScreenViewSoundQuality | null) => {
      if (state === "paired") {
        send({ type: "screen.view.sound-quality.set", soundQuality });
      }
    },
    [send, state],
  );

  const setHostCustomPointer = useCallback(
    (enabled: boolean) => {
      setHostStatus((current) =>
        current ? { ...current, customPointerEnabled: enabled } : current,
      );
      if (state === "paired") {
        send({ type: "custom.pointer.set", enabled });
      }
    },
    [send, setHostStatus, state],
  );

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
    setHostAccentColor,
    setHostScreenSoundQuality,
    setHostControlDepth,
    setHostShowModeButtons,
    setHostPointerSpeed,
  };
}
