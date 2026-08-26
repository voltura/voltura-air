import { useEffect, useState, type ReactNode } from "react";
import type { AppTab } from "./modeTypes";
import type { AppSettings } from "../../foundation/settings/appSettings";
import type { TrackpadSettings, TwoFingerMode } from "../../foundation/input/gestures";
import { triggerHapticFeedback } from "../../foundation/input/hapticFeedback";
import { useKeyboardInput } from "../../foundation/input/useKeyboardInput";
import { usePointerInput } from "../../foundation/input/usePointerInput";
import { useGyroMouse } from "../../foundation/input/useGyroMouse";
import type { GyroActivationRequest } from "../../foundation/input/gyroMouse";
import { useSpeechDictation } from "../../foundation/input/useSpeechDictation";
import type { KeyboardSettings } from "../../foundation/settings/keyboardSettings";
import { toLiveKeyboardValue } from "../../foundation/input/keyboardDelta";
import type { RemoteSettings } from "../../foundation/settings/remoteSettings";
import type { useVolturaAirConnection } from "../../foundation/connection/useVolturaAirConnection";
import type { AppToastMessage } from "../../ui/feedback/AppToast";
import { AppModeContent } from "./AppModeContent";

type ConnectionContract = Pick<
  ReturnType<typeof useVolturaAirConnection>,
  | "audioState"
  | "appLaunchResult"
  | "awakeCapability"
  | "awakeResult"
  | "clipboardReadPermission"
  | "clipboardReadResult"
  | "clipboardText"
  | "cancelClipboardReadForDevice"
  | "clientId"
  | "hostStatus"
  | "pendingAppLaunchId"
  | "pendingAwakeChange"
  | "pendingClipboardRead"
  | "pendingPowerAction"
  | "pendingPowerPointLaunch"
  | "pendingPowerPointRefresh"
  | "pendingPresentationCommand"
  | "pendingPresentationSession"
  | "pendingPresentationReportSave"
  | "pendingTextTransfer"
  | "pendingUrlOpen"
  | "powerActionResult"
  | "powerCapabilities"
  | "presentationCapability"
  | "presentationResult"
  | "powerPointLaunchResult"
  | "presentationReportSaveResult"
  | "requestAppLaunch"
  | "requestAudioState"
  | "requestAwakeChange"
  | "requestClipboardRead"
  | "requestClipboardReadForDevice"
  | "requestPowerAction"
  | "requestPowerPointRefresh"
  | "requestPowerPointLaunch"
  | "requestPresentationCommand"
  | "requestPresentationSession"
  | "requestPresentationReportSave"
  | "requestTextTransfer"
  | "requestUrlOpen"
  | "send"
  | "setClipboardText"
  | "state"
  | "supportsGestureDebug"
  | "supportsSleep"
  | "supportsTextTransfer"
  | "supportsVolumeControl"
  | "textTransferResult"
  | "urlOpenCapability"
  | "urlOpenResult"
>;

interface ModeWorkspaceProps {
  appSettings: AppSettings;
  connection: ConnectionContract;
  connectionEpoch: number;
  gyroActivationRequest: GyroActivationRequest | null;
  keyboardSettings: KeyboardSettings;
  onClearAfterSendingChange: (value: boolean) => void;
  onGyroSelectedChange: (selected: boolean) => void;
  onClipboardCopyFeedback: (feedback: AppToastMessage) => void;
  onPresentationSessionActiveChange: (active: boolean) => void;
  onPresentationActivationRequestHandled: () => void;
  presentationActivationRequestId: number;
  onRemoteUtilityPanelOpenChange: (isOpen: boolean) => void;
  remoteSettings: RemoteSettings;
  shouldShowSplitMode: boolean;
  showVolumeControl: boolean;
  showTrackpadCompactModeSelector: boolean;
  tab: AppTab;
  trackpadCompactModeSelector?: ReactNode | undefined;
  trackpadSettings: TrackpadSettings;
}

export function ModeWorkspace({
  appSettings,
  connection,
  connectionEpoch,
  gyroActivationRequest,
  keyboardSettings,
  onClearAfterSendingChange,
  onGyroSelectedChange,
  onClipboardCopyFeedback,
  onPresentationSessionActiveChange,
  onPresentationActivationRequestHandled,
  presentationActivationRequestId,
  onRemoteUtilityPanelOpenChange,
  remoteSettings,
  shouldShowSplitMode,
  showTrackpadCompactModeSelector,
  showVolumeControl,
  tab,
  trackpadCompactModeSelector,
  trackpadSettings,
}: ModeWorkspaceProps) {
  const [optimisticAudioState, setOptimisticAudioState] = useState<{
    source: typeof connection.audioState;
    value: typeof connection.audioState;
  } | null>(null);
  const displayedAudioState =
    optimisticAudioState?.source === connection.audioState
      ? optimisticAudioState.value
      : connection.audioState;
  const [isTrackpadExpanded, setIsTrackpadExpanded] = useState(false);
  const [isPresentationTrackpadOpen, setIsPresentationTrackpadOpen] = useState(false);
  const [trackpadTwoFingerMode, setTrackpadTwoFingerMode] = useState<TwoFingerMode>("scroll");
  const [textTransferDraft, setTextTransferDraft] = useState("");
  const effectiveTrackpadTwoFingerMode = trackpadSettings.zoomGestures
    ? trackpadTwoFingerMode
    : "scroll";
  const inputContext =
    tab === "remote"
      ? null
      : tab === "keyboard"
        ? "keyboard"
        : tab === "presentation" || tab === "dictation"
          ? tab
          : "trackpad";
  const {
    cancel,
    emit,
    onTouchCancel,
    onTouchEnd,
    onTouchMove,
    onTouchStart,
    sendSpecial,
    sendText,
    sleepPc,
  } = usePointerInput({
    inputContext,
    send: connection.send,
    state: connection.state,
    trackpadSettings,
    twoFingerMode: effectiveTrackpadTwoFingerMode,
  });
  const gyro = useGyroMouse({
    activationRequest: gyroActivationRequest,
    connected: connection.state === "paired",
    enabledSurface: tab === "trackpad" || (tab === "presentation" && isPresentationTrackpadOpen),
    onMove: (dx, dy) => {
      emit({ type: "pointer.move", inputContext: "gyro-mouse", dx, dy });
    },
    onSelectedChange: onGyroSelectedChange,
    onStop: cancel,
    sensitivity: trackpadSettings.gyroSensitivity,
    sessionKey: connectionEpoch,
  });
  const {
    committedKeyboardTextRef,
    isComposingRef,
    keyboardText,
    keyboardTextareaRef,
    liveKeyboard,
    onKeyboardTextChange,
    placeLiveKeyboardCaret,
    sendEmptyDelete,
    setKeyboardText,
    setLiveTyping,
  } = useKeyboardInput(emit);
  const {
    canUseSpeech,
    dictationText,
    isListening,
    setDictationText,
    speechError,
    startSpeech,
    stopSpeech,
  } = useSpeechDictation(sendText, tab === "dictation");
  const { requestAudioState, state: connectionState, supportsVolumeControl } = connection;

  useEffect(() => {
    const trackpadVolumeVisible = tab === "trackpad" && showVolumeControl && !isTrackpadExpanded;
    if (
      connectionState === "paired" &&
      supportsVolumeControl &&
      (trackpadVolumeVisible || tab === "remote" || tab === "presentation")
    ) {
      requestAudioState();
    }
  }, [
    connectionState,
    isTrackpadExpanded,
    requestAudioState,
    showVolumeControl,
    supportsVolumeControl,
    tab,
  ]);

  const toggleMute = () => {
    if (connection.supportsVolumeControl) {
      emit({ type: "audio.mute.toggle", inputContext: "media-controls" });
    }
  };

  const setVolume = (volume: number) => {
    if (!connection.supportsVolumeControl) {
      return;
    }

    const nextVolume = Math.max(0, Math.min(100, Math.round(volume)));
    setOptimisticAudioState({
      source: connection.audioState,
      value: { type: "audio.state", volume: nextVolume, muted: false },
    });
    emit({ type: "audio.volume.set", inputContext: "media-controls", volume: nextVolume });
  };

  return (
    <AppModeContent
      tab={tab}
      shouldShowSplitMode={shouldShowSplitMode}
      supportsGestureDebug={connection.supportsGestureDebug}
      trackpadMode={{
        audioState: displayedAudioState,
        isExpanded: isTrackpadExpanded,
        gyro,
        onMouseButtonClick: (button) => {
          triggerHapticFeedback(trackpadSettings);
          emit({ type: "pointer.button", button, action: "click" });
        },
        onMouseButtonDown: (button) => {
          triggerHapticFeedback(trackpadSettings);
          emit({ type: "pointer.button", button, action: "down" });
        },
        onMouseButtonUp: (button) => {
          emit({ type: "pointer.button", button, action: "up" });
        },
        onSetVolume: setVolume,
        onToggleExpanded: () => {
          setIsTrackpadExpanded((current) => !current);
        },
        onToggleMute: toggleMute,
        onTwoFingerModeChange: setTrackpadTwoFingerMode,
        onTouchCancel,
        onTouchEnd,
        onTouchMove,
        onTouchStart,
        supportsVolumeControl: connection.supportsVolumeControl,
        compactModeSelector: showTrackpadCompactModeSelector
          ? trackpadCompactModeSelector
          : undefined,
        trackpadSettings,
        twoFingerMode: effectiveTrackpadTwoFingerMode,
      }}
      keyboardMode={{
        committedKeyboardTextRef,
        isComposingRef,
        keyboardText,
        keyboardTextareaRef,
        liveKeyboard,
        onKeyboardTextChange,
        onSleep: sleepPc,
        placeLiveKeyboardCaret,
        sendEmptyDelete,
        sendSpecial,
        sendText,
        setKeyboardText,
        setLiveTyping,
        showArrowKeys: keyboardSettings.showArrowKeys,
        showControlKeys: keyboardSettings.showControlKeys,
        showFunctionKeys: keyboardSettings.showFunctionKeys,
        showSleepButton: keyboardSettings.showSleepButton && connection.supportsSleep,
        toLiveKeyboardValue,
      }}
      presentationMode={{
        activationRequestId: presentationActivationRequestId,
        audioState: displayedAudioState,
        blackoutAvailable: connection.powerCapabilities?.blackoutDisplay === true,
        capability: connection.presentationCapability,
        connected: connection.state === "paired",
        pending: connection.pendingPresentationCommand,
        pendingPowerPointLaunch: connection.pendingPowerPointLaunch,
        powerPointRefreshPending: connection.pendingPowerPointRefresh !== null,
        sessionPending: connection.pendingPresentationSession !== null,
        pendingPowerAction: connection.pendingPowerAction,
        reportSavePending: connection.pendingPresentationReportSave !== null,
        reportSaveResult: connection.presentationReportSaveResult,
        reportSavingAvailable: connection.presentationCapability?.canSaveReports === true,
        result: connection.presentationResult,
        powerPointLaunchResult: connection.powerPointLaunchResult,
        powerPointAppLaunchAction: connection.hostStatus?.appLaunchActions?.find(
          (action) => action.kind === "powerpoint",
        ),
        powerPointAppLaunchResult: connection.appLaunchResult,
        pendingPowerPointAppLaunch:
          connection.pendingAppLaunchId ===
          connection.hostStatus?.appLaunchActions?.find((action) => action.kind === "powerpoint")
            ?.id,
        onActivationRequestHandled: onPresentationActivationRequestHandled,
        onCommand: connection.requestPresentationCommand,
        onSessionCommand: connection.requestPresentationSession,
        onMute: () => {
          sendSpecial("VolumeMute", undefined, "media-controls");
        },
        onPowerAction: connection.requestPowerAction,
        onPowerPointRefresh: connection.requestPowerPointRefresh,
        onPowerPointLaunch: connection.requestPowerPointLaunch,
        onPowerPointAppLaunch: connection.requestAppLaunch,
        onSaveReport: connection.requestPresentationReportSave,
        onSessionActiveChange: onPresentationSessionActiveChange,
        onTrackpadOpenChange: setIsPresentationTrackpadOpen,
        onVolumeDown: () => {
          sendSpecial("VolumeDown", undefined, "media-controls");
        },
        onVolumeUp: () => {
          sendSpecial("VolumeUp", undefined, "media-controls");
        },
      }}
      remoteMode={{
        appLaunchActions: connection.hostStatus?.appLaunchActions ?? [],
        audioState: displayedAudioState,
        awakeControl: {
          awake: connection.awakeCapability,
          awakeResult: connection.awakeResult,
          onAwakeChange: connection.requestAwakeChange,
          pendingAwakeChange: connection.pendingAwakeChange,
        },
        isConnected: connection.state === "paired",
        onPointerButtonClick: (button) => {
          emit({ type: "pointer.button", inputContext: "trackpad", button, action: "click" });
        },
        onPointerMove: (dx, dy) => {
          emit({ type: "pointer.move", inputContext: "trackpad", dx, dy });
        },
        onPowerAction: connection.requestPowerAction,
        onAppLaunch: connection.requestAppLaunch,
        onUrlOpen: connection.requestUrlOpen,
        pendingAppLaunchId: connection.pendingAppLaunchId,
        pendingUrlOpen: connection.pendingUrlOpen,
        pendingPowerAction: connection.pendingPowerAction,
        powerActionResult: connection.powerActionResult,
        powerCapabilities: connection.powerCapabilities,
        urlOpenCapability: connection.urlOpenCapability,
        urlOpenResult: connection.urlOpenResult,
        remoteSettings,
        onUtilityPanelOpenChange: onRemoteUtilityPanelOpenChange,
        sendSpecial,
      }}
      dictationMode={{
        canUseSpeech,
        dictationText,
        isListening,
        sendText,
        setDictationText,
        speechError,
        startSpeech,
        stopSpeech,
      }}
      textTransferMode={{
        clearAfterSending: appSettings.clearTextAfterSending,
        clientId: connection.clientId,
        draft: textTransferDraft,
        leftHandedButtons: trackpadSettings.leftHandedButtons,
        onClearAfterSendingChange,
        onDraftChange: setTextTransferDraft,
        onPointerButtonClick: (button) => {
          emit({ type: "pointer.button", button, action: "click" });
        },
        onTouchCancel,
        onTouchEnd,
        onTouchMove,
        onTouchStart,
        pending: connection.pendingTextTransfer,
        requestTextTransfer: connection.requestTextTransfer,
        result: connection.textTransferResult,
        supported: connection.supportsTextTransfer,
        target: connection.hostStatus?.textTransferTarget,
      }}
      clipboardReadMode={{
        clientId: connection.clientId,
        permission: connection.clipboardReadPermission,
        pending: connection.pendingClipboardRead,
        result: connection.clipboardReadResult,
        text: connection.clipboardText,
        onCopyFeedback: onClipboardCopyFeedback,
        onCancelGetTextForDevice: connection.cancelClipboardReadForDevice,
        onGetText: connection.requestClipboardRead,
        onGetTextForDevice: connection.requestClipboardReadForDevice,
        onLoadSnippet: (snippet) => {
          connection.setClipboardText(snippet.text);
        },
        onTextChange: connection.setClipboardText,
      }}
      gestureDebugMode={{ trackpadSettings }}
    />
  );
}
