import { useCallback, useRef, useState, type RefObject } from "react";
import type { AudioStateMessage, AwakeCapability, HostStatusMetadata, PowerCapabilities, PresentationCapability, ServerCapabilities, UrlOpenCapability } from "../protocol/messages";
import {
  getPowerCapabilities,
  getAwakeCapability,
  getPresentationCapability,
  hasGestureDebugCapability,
  getClipboardReadPermission,
  getUrlOpenCapability,
  hasInputAckCapability,
  hasRemoteLaunchCapability,
  hasSleepCapability,
  hasTextTransferCapability,
  hasVolumeCapability,
  normalizeHostStatus
} from "./connectionProtocol";
import type { PendingMovementAck } from "./useConnectionSender";

export function useConnectionRuntimeState(
  pendingInputAcksRef: RefObject<Map<number, number>>,
  pendingMovementAckRef: RefObject<PendingMovementAck | null>
) {
  const [audioState, setAudioState] = useState<AudioStateMessage | null>(null);
  const [awakeCapability, setAwakeCapability] = useState<AwakeCapability | null>(null);
  const [supportsGestureDebug, setSupportsGestureDebug] = useState(false);
  const [supportsSleep, setSupportsSleep] = useState(false);
  const [supportsVolumeControl, setSupportsVolumeControl] = useState(false);
  const [supportsRemoteLaunch, setSupportsRemoteLaunch] = useState(false);
  const [supportsTextTransfer, setSupportsTextTransfer] = useState(false);
  const [clipboardReadPermission, setClipboardReadPermission] = useState<boolean | undefined>(undefined);
  const [urlOpenCapability, setUrlOpenCapability] = useState<UrlOpenCapability | undefined>(undefined);
  const [powerCapabilities, setPowerCapabilities] = useState<PowerCapabilities | null>(null);
  const [presentationCapability, setPresentationCapability] = useState<PresentationCapability | undefined>(undefined);
  const [hostStatus, setHostStatus] = useState<HostStatusMetadata | null>(null);
  const supportsVolumeControlRef = useRef(false);
  const supportsInputAckRef = useRef(false);

  const clearRuntimeState = useCallback(() => {
    pendingInputAcksRef.current.clear();
    pendingMovementAckRef.current = null;
    setAudioState(null);
    setAwakeCapability(null);
    setSupportsGestureDebug(false);
    setSupportsSleep(false);
    setSupportsVolumeControl(false);
    setSupportsRemoteLaunch(false);
    setSupportsTextTransfer(false);
    setClipboardReadPermission(undefined);
    setUrlOpenCapability(undefined);
    setPowerCapabilities(null);
    setPresentationCapability(undefined);
    setHostStatus(null);
    supportsVolumeControlRef.current = false;
    supportsInputAckRef.current = false;
  }, [pendingInputAcksRef, pendingMovementAckRef]);

  const updateCapabilities = useCallback((capabilities: ServerCapabilities | undefined, connected = true) => {
    const nextSupportsVolumeControl = connected && hasVolumeCapability(capabilities);
    const nextSupportsInputAck = connected && hasInputAckCapability(capabilities);
    setSupportsGestureDebug(connected && hasGestureDebugCapability(capabilities));
    setSupportsSleep(connected && hasSleepCapability(capabilities));
    setSupportsVolumeControl(nextSupportsVolumeControl);
    setSupportsRemoteLaunch(connected && hasRemoteLaunchCapability(capabilities));
    setSupportsTextTransfer(connected && hasTextTransferCapability(capabilities));
    setClipboardReadPermission(connected ? getClipboardReadPermission(capabilities) : undefined);
    setUrlOpenCapability(connected ? getUrlOpenCapability(capabilities) : undefined);
    setPowerCapabilities(connected ? getPowerCapabilities(capabilities) : null);
    setPresentationCapability(connected ? getPresentationCapability(capabilities) : undefined);
    setAwakeCapability(connected ? getAwakeCapability(capabilities) : null);
    supportsVolumeControlRef.current = nextSupportsVolumeControl;
    supportsInputAckRef.current = nextSupportsInputAck;
    if (!nextSupportsVolumeControl) {
      setAudioState(null);
    }
    if (!nextSupportsInputAck) {
      pendingInputAcksRef.current.clear();
      pendingMovementAckRef.current = null;
    }
  }, [pendingInputAcksRef, pendingMovementAckRef]);

  const updateHostStatus = useCallback((metadata: HostStatusMetadata | undefined) => {
    const normalized = normalizeHostStatus(metadata);
    if (normalized) {
      setHostStatus(normalized);
    }
  }, []);

  return {
    audioState,
    awakeCapability,
    clipboardReadPermission,
    clearRuntimeState,
    hostStatus,
    powerCapabilities,
    presentationCapability,
    setAudioState,
    setHostStatus,
    supportsGestureDebug,
    supportsInputAckRef,
    supportsRemoteLaunch,
    supportsTextTransfer,
    supportsSleep,
    supportsVolumeControl,
    supportsVolumeControlRef,
    updateCapabilities,
    updateHostStatus,
    urlOpenCapability
  };
}
