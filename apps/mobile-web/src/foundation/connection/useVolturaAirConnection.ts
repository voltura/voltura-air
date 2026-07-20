import { useEffect, useMemo, useRef, useState } from "react";
import { upsertPcProfile, type PcProfile } from "./pcProfiles";
import { loadDeviceName } from "./clientIdentity";
import { getDisplayPcName } from "./connectionProtocol";
import { useConnectionRuntimeState } from "./useConnectionRuntimeState";
import { usePairedPcActions } from "./usePairedPcActions";
import type { ConnectionError, ConnectionState, PairingAttempt } from "./connectionTypes";
import { useConnectionSender, type PendingMovementAck } from "./useConnectionSender";
import { useConnectionPersistence } from "./useConnectionPersistence";
import { useInitialConnectionProfileState } from "./useInitialConnectionProfileState";
import { useAwakeControl } from "./useAwakeControl";
import { usePowerControl } from "./usePowerControl";
import { useAppLaunch } from "./useAppLaunch";
import { useTextTransfer } from "./useTextTransfer";
import { useClipboardRead } from "./useClipboardRead";
import { useUrlOpen } from "./useUrlOpen";
import { usePresentationControl } from "./usePresentationControl";
import { useConnectionSocketLifecycle } from "./useConnectionSocketLifecycle";
import { hasStoredReconnectKey } from "./pairingCredentials";

export type { PcProfile } from "./pcProfiles";
export type { ConnectionError, ConnectionState } from "./connectionTypes";

export function useVolturaAirConnection() {
  const {
    addressPcProfile,
    clientId,
    effectiveStoredActivePcId,
    initialPairing,
    screenshotMode,
    shouldActivateAddressPc,
    shouldStoreAddressPc,
    storedPcProfiles
  } = useInitialConnectionProfileState();
  const [deviceName, setDeviceName] = useState(() => loadDeviceName(window.location.href));
  const [pairedPcs, setPairedPcs] = useState<PcProfile[]>(() =>
    shouldStoreAddressPc ? upsertPcProfile(storedPcProfiles, addressPcProfile) : storedPcProfiles);

  const [activePcId, setActivePcId] = useState<string | null>(() => initialPairing !== null ? null : shouldActivateAddressPc ? addressPcProfile.id : effectiveStoredActivePcId);
  const [pendingManualPc, setPendingManualPc] = useState<PcProfile | null>(null);
  const activePc = useMemo(() => pairedPcs.find((pc) => pc.id === activePcId) ?? null, [activePcId, pairedPcs]);
  const reconnectablePcs = pairedPcs.filter((pc) => hasStoredReconnectKey(clientId, pc.id));
  const connectionPc = pendingManualPc ?? activePc;
  const hasStoredPcWithoutConnection = connectionPc === null && pairedPcs.length > 0;
  const [state, setState] = useState<ConnectionState>(() => connectionPc ? "connecting" : "needs-pairing");
  const [message, setMessage] = useState(() => connectionPc
    ? "Connecting to PC..."
    : hasStoredPcWithoutConnection ? "Choose a PC or scan a pairing QR." : "Scan the PC pairing QR to pair this app.");
  const [lastConnectionError, setLastConnectionError] = useState<ConnectionError | null>(null);
  const [pairingAttempt, setPairingAttempt] = useState<PairingAttempt>(() => ({ token: undefined, id: 0 }));
  const socketRef = useRef<WebSocket | null>(null);
  const deviceNameRef = useRef(deviceName);
  const pairingAttemptRef = useRef(pairingAttempt);
  const lastHealthyAtRef = useRef(0);
  const lastUserActivityAtRef = useRef(0);
  const lastMovementAckAtRef = useRef(0);
  const reconnectRef = useRef<(() => void) | null>(null);
  const rescheduleHealthCheckRef = useRef<(() => void) | null>(null);

  useEffect(() => {
    lastUserActivityAtRef.current = Date.now();
  }, []);
  const nextInputSequenceRef = useRef(1);
  const pendingInputAcksRef = useRef<Map<number, number>>(new Map());
  const pendingMovementAckRef = useRef<PendingMovementAck | null>(null);
  const {
    audioState, awakeCapability, clipboardReadPermission, clearRuntimeState, hostStatus, powerCapabilities, presentationCapability, setAudioState,
    setHostStatus, supportsGestureDebug, supportsInputAckRef, supportsRemoteLaunch, supportsSleep, supportsTextTransfer,
    supportsVolumeControl, supportsVolumeControlRef, updateCapabilities, updateHostStatus, urlOpenCapability
  } = useConnectionRuntimeState(pendingInputAcksRef, pendingMovementAckRef);
  const { requestAudioState, send } = useConnectionSender({
    lastMovementAckAtRef, lastUserActivityAtRef, nextInputSequenceRef, pendingInputAcksRef, pendingMovementAckRef,
    reconnectRef, rescheduleHealthCheckRef, socketRef, supportsInputAckRef, supportsVolumeControlRef
  });
  const { awakeResult, completeAwakeChange: completeAwakeChangeState, pendingAwakeChange, requestAwakeChange } = useAwakeControl(state, send);
  const { completePowerAction: completePowerActionState, pendingPowerAction, powerActionResult, requestPowerAction } = usePowerControl(state, send);
  const { appLaunchResult, completeAppLaunch: completeAppLaunchState, pendingAppLaunchId, requestAppLaunch } = useAppLaunch(state, send);
  const { completePresentationCommand: completePresentationCommandState, pendingPresentationCommand, presentationResult, requestPresentationCommand } = usePresentationControl(state, send);
  const { completeUrlOpen: completeUrlOpenState, pendingUrlOpen, requestUrlOpen, urlOpenResult } = useUrlOpen(state, send);
  const { completeTextTransfer: completeTextTransferState, pendingTextTransfer, requestTextTransfer, textTransferResult } = useTextTransfer(state, send);
  const { clipboardReadResult, clipboardText, completeClipboardRead: completeClipboardReadState, pendingClipboardRead, requestClipboardRead, setClipboardText } = useClipboardRead(state, send);
  useConnectionPersistence({
    activePcId,
    clientId,
    deviceName,
    deviceNameRef,
    hasInitialPairing: initialPairing !== null,
    pairedPcs,
    pairingAttempt,
    pairingAttemptRef
  });

  const displayMessage = state === "paired" && activePc && message.startsWith("Connected to ")
    ? `Connected to ${getDisplayPcName(activePc, "", screenshotMode)}`
    : state === "needs-pairing" && !connectionPc
      ? hasStoredPcWithoutConnection ? "Choose a PC or scan a pairing QR." : "Scan the PC pairing QR to pair this app."
      : message;
  const connectionPcId = connectionPc?.id ?? null;
  const connectionPcUrl = connectionPc?.url ?? null;
  useConnectionSocketLifecycle({
    clearRuntimeStateFromSocket: clearRuntimeState,
    clientId,
    completeAppLaunch: completeAppLaunchState,
    completeAwakeChange: completeAwakeChangeState,
    completeClipboardRead: completeClipboardReadState,
    completePowerAction: completePowerActionState,
    completePresentationCommand: completePresentationCommandState,
    completeTextTransfer: completeTextTransferState,
    completeUrlOpen: completeUrlOpenState,
    connectionPcId,
    connectionPcUrl,
    deviceNameRef,
    getLatestConnectionPc: (fallback) => connectionPc?.id === fallback.id && connectionPc.url === fallback.url ? connectionPc : fallback,
    lastHealthyAtRef,
    lastUserActivityAtRef,
    pairingAttemptId: pairingAttempt.id,
    pairingAttemptRef,
    pendingInputAcksRef,
    pendingManualPc,
    pendingMovementAckRef,
    reconnectRef,
    rescheduleHealthCheckRef,
    screenshotMode,
    setActivePcId,
    setAudioStateFromSocket: setAudioState,
    setLastConnectionError,
    setMessage,
    setPairedPcs,
    setPairingAttempt,
    setPendingManualPc,
    setState,
    socketRef,
    supportsInputAckRef,
    supportsVolumeControlRef,
    updateCapabilitiesFromSocket: updateCapabilities,
    updateHostStatusFromSocket: updateHostStatus
  });

  const {
    addManualPc, beginNewPairing, connectManualPc, disconnectActivePc, forgetPc,
    pairWithToken, renameDevice, renamePc, selectPc, setHostCustomPointer, setHostShowModeButtons, setHostPointerSpeed
  } = usePairedPcActions({
    activePcId, clearRuntimeState, clientId, deviceNameRef, pairedPcs, screenshotMode, send,
    setActivePcId, setDeviceName, setHostStatus, setLastConnectionError, setMessage, setPairedPcs,
    setPairingAttempt, setPendingManualPc, setState, socketRef, state
  });

  return { state, message: displayMessage, send, requestAudioState, requestPowerAction, requestAwakeChange, requestAppLaunch, requestPresentationCommand, requestUrlOpen, requestTextTransfer, requestClipboardRead, pendingPresentationCommand, presentationResult, presentationCapability, pendingTextTransfer, pendingClipboardRead, textTransferResult, clipboardReadResult, clipboardText, setClipboardText, clipboardReadPermission, pendingAppLaunchId, appLaunchResult, pendingUrlOpen, urlOpenResult, urlOpenCapability, pendingPowerAction, powerActionResult, pendingAwakeChange, awakeResult, clientId, deviceName, activePc, pairedPcs, reconnectablePcs, audioState, awakeCapability, powerCapabilities, supportsGestureDebug, supportsSleep, supportsVolumeControl, supportsRemoteLaunch, supportsTextTransfer, lastConnectionError, hostStatus, pairWithToken, selectPc, addManualPc, beginNewPairing, connectManualPc, disconnectActivePc, forgetPc, renamePc, renameDevice, setHostCustomPointer, setHostShowModeButtons, setHostPointerSpeed };
}


export { shouldClearStoredReconnectKeyForRejection } from "./pairingCredentials";
