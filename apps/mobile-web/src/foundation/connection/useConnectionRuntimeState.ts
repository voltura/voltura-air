import { useCallback, useRef, useState, type RefObject } from "react";
import type {
  AudioStateMessage,
  AwakeCapability,
  CustomScreensCapability,
  FileManagerCapability,
  TerminalCapability,
  AiAssistantCapability,
  HostStatusMetadata,
  PhoneWebcamCapability,
  PowerCapabilities,
  PresentationCapability,
  ScreenViewCapability,
  ServerCapabilities,
  UrlOpenCapability,
} from "../protocol/messages";
import {
  getPowerCapabilities,
  getAwakeCapability,
  getPresentationCapability,
  getCustomScreensCapability,
  getScreenViewCapability,
  getPhoneWebcamCapability,
  getFileManagerCapability,
  getTerminalCapability,
  getAiAssistantCapability,
  hasGestureDebugCapability,
  getClipboardReadPermission,
  getDiagnosticsPermission,
  getUrlOpenCapability,
  hasInputAckCapability,
  hasRemoteLaunchCapability,
  hasSleepCapability,
  hasTextTransferCapability,
  hasVolumeCapability,
  normalizeHostStatus,
} from "./connectionProtocol";
import type { PendingMovementAck } from "./useConnectionSender";

export function useConnectionRuntimeState(
  pendingInputAcksRef: RefObject<Map<number, number>>,
  pendingMovementAckRef: RefObject<PendingMovementAck | null>,
) {
  const [audioState, setAudioState] = useState<AudioStateMessage | null>(null);
  const [awakeCapability, setAwakeCapability] = useState<AwakeCapability | null>(null);
  const [supportsGestureDebug, setSupportsGestureDebug] = useState(false);
  const [supportsSleep, setSupportsSleep] = useState(false);
  const [supportsVolumeControl, setSupportsVolumeControl] = useState(false);
  const [supportsRemoteLaunch, setSupportsRemoteLaunch] = useState(false);
  const [supportsTextTransfer, setSupportsTextTransfer] = useState(false);
  const [clipboardReadPermission, setClipboardReadPermission] = useState<boolean | undefined>(
    undefined,
  );
  const [diagnosticsPermission, setDiagnosticsPermission] = useState<boolean | undefined>(
    undefined,
  );
  const [urlOpenCapability, setUrlOpenCapability] = useState<UrlOpenCapability | undefined>(
    undefined,
  );
  const [powerCapabilities, setPowerCapabilities] = useState<PowerCapabilities | null>(null);
  const [presentationCapability, setPresentationCapability] = useState<
    PresentationCapability | undefined
  >(undefined);
  const [customScreensCapability, setCustomScreensCapability] = useState<
    CustomScreensCapability | undefined
  >(undefined);
  const [screenViewCapability, setScreenViewCapability] = useState<
    ScreenViewCapability | undefined
  >(undefined);
  const [phoneWebcamCapability, setPhoneWebcamCapability] = useState<
    PhoneWebcamCapability | undefined
  >(undefined);
  const [fileManagerCapability, setFileManagerCapability] = useState<
    FileManagerCapability | undefined
  >(undefined);
  const [terminalCapability, setTerminalCapability] = useState<TerminalCapability | undefined>(
    undefined,
  );
  const [aiAssistantCapability, setAiAssistantCapability] = useState<
    AiAssistantCapability | undefined
  >(undefined);
  const [hostStatus, setHostStatus] = useState<HostStatusMetadata | null>(null);
  const supportsVolumeControlRef = useRef(false);
  const supportsInputAckRef = useRef(false);
  const supportsInputContextV1Ref = useRef(false);

  const clearRuntimeState = useCallback(
    (preserveTerminal = false) => {
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
      setDiagnosticsPermission(undefined);
      setUrlOpenCapability(undefined);
      setPowerCapabilities(null);
      setPresentationCapability(undefined);
      setCustomScreensCapability(undefined);
      setScreenViewCapability(undefined);
      setPhoneWebcamCapability(undefined);
      setFileManagerCapability(undefined);
      if (!preserveTerminal) {
        setTerminalCapability(undefined);
      }
      setAiAssistantCapability(undefined);
      setHostStatus(null);
      supportsVolumeControlRef.current = false;
      supportsInputAckRef.current = false;
      supportsInputContextV1Ref.current = false;
    },
    [pendingInputAcksRef, pendingMovementAckRef],
  );

  const updateCapabilities = useCallback(
    (capabilities: ServerCapabilities | undefined, connected = true) => {
      const nextSupportsVolumeControl = connected && hasVolumeCapability(capabilities);
      const nextSupportsInputAck = connected && hasInputAckCapability(capabilities);
      const nextSupportsInputContextV1 = connected && capabilities?.inputContextV1 === true;
      setSupportsGestureDebug(connected && hasGestureDebugCapability(capabilities));
      setSupportsSleep(connected && hasSleepCapability(capabilities));
      setSupportsVolumeControl(nextSupportsVolumeControl);
      setSupportsRemoteLaunch(connected && hasRemoteLaunchCapability(capabilities));
      setSupportsTextTransfer(connected && hasTextTransferCapability(capabilities));
      setClipboardReadPermission(connected ? getClipboardReadPermission(capabilities) : undefined);
      setDiagnosticsPermission(connected ? getDiagnosticsPermission(capabilities) : undefined);
      setUrlOpenCapability(connected ? getUrlOpenCapability(capabilities) : undefined);
      setPowerCapabilities(connected ? getPowerCapabilities(capabilities) : null);
      setPresentationCapability(connected ? getPresentationCapability(capabilities) : undefined);
      setCustomScreensCapability(connected ? getCustomScreensCapability(capabilities) : undefined);
      setScreenViewCapability(connected ? getScreenViewCapability(capabilities) : undefined);
      setPhoneWebcamCapability(connected ? getPhoneWebcamCapability(capabilities) : undefined);
      setFileManagerCapability(connected ? getFileManagerCapability(capabilities) : undefined);
      if (connected) {
        setTerminalCapability(getTerminalCapability(capabilities));
      }
      setAiAssistantCapability(connected ? getAiAssistantCapability(capabilities) : undefined);
      setAwakeCapability(connected ? getAwakeCapability(capabilities) : null);
      supportsVolumeControlRef.current = nextSupportsVolumeControl;
      supportsInputAckRef.current = nextSupportsInputAck;
      supportsInputContextV1Ref.current = nextSupportsInputContextV1;
      if (!nextSupportsVolumeControl) {
        setAudioState(null);
      }
      if (!nextSupportsInputAck) {
        pendingInputAcksRef.current.clear();
        pendingMovementAckRef.current = null;
      }
    },
    [pendingInputAcksRef, pendingMovementAckRef],
  );

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
    diagnosticsPermission,
    customScreensCapability,
    clearRuntimeState,
    hostStatus,
    powerCapabilities,
    presentationCapability,
    phoneWebcamCapability,
    screenViewCapability,
    fileManagerCapability,
    terminalCapability,
    aiAssistantCapability,
    setAudioState,
    setHostStatus,
    supportsGestureDebug,
    supportsInputAckRef,
    supportsInputContextV1Ref,
    supportsRemoteLaunch,
    supportsTextTransfer,
    supportsSleep,
    supportsVolumeControl,
    supportsVolumeControlRef,
    updateCapabilities,
    updateHostStatus,
    urlOpenCapability,
  };
}
