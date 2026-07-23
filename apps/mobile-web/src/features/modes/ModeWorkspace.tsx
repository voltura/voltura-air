import { useEffect, useState, type ReactNode } from "react";
import type { AppTab } from "./modeTypes";
import type { AppSettings } from "../../foundation/settings/appSettings";
import type { TrackpadSettings } from "../../foundation/input/gestures";
import { triggerHapticFeedback } from "../../foundation/input/hapticFeedback";
import { useKeyboardInput } from "../../foundation/input/useKeyboardInput";
import { usePointerInput } from "../../foundation/input/usePointerInput";
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
  | "awakeCapability"
  | "awakeResult"
  | "clipboardReadPermission"
  | "clipboardReadResult"
  | "clipboardText"
  | "clientId"
  | "hostStatus"
  | "pendingAppLaunchId"
  | "pendingAwakeChange"
  | "pendingClipboardRead"
  | "pendingPowerAction"
  | "pendingPresentationCommand"
  | "pendingPresentationReportSave"
  | "pendingTextTransfer"
  | "pendingUrlOpen"
  | "powerActionResult"
  | "powerCapabilities"
  | "presentationCapability"
  | "presentationResult"
  | "presentationReportSaveResult"
  | "requestAppLaunch"
  | "requestAudioState"
  | "requestAwakeChange"
  | "requestClipboardRead"
  | "requestPowerAction"
  | "requestPresentationCommand"
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
  keyboardSettings: KeyboardSettings;
  onClearAfterSendingChange: (value: boolean) => void;
  onClipboardCopyFeedback: (feedback: AppToastMessage) => void;
  onPresentationSessionActiveChange: (active: boolean) => void;
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
  keyboardSettings,
  onClearAfterSendingChange,
  onClipboardCopyFeedback,
  onPresentationSessionActiveChange,
  onRemoteUtilityPanelOpenChange,
  remoteSettings,
  shouldShowSplitMode,
  showTrackpadCompactModeSelector,
  showVolumeControl,
  tab,
  trackpadCompactModeSelector,
  trackpadSettings
}: ModeWorkspaceProps) {
  const [optimisticAudioState, setOptimisticAudioState] = useState<{
    source: typeof connection.audioState;
    value: typeof connection.audioState;
  } | null>(null);
  const displayedAudioState = optimisticAudioState?.source === connection.audioState
    ? optimisticAudioState.value
    : connection.audioState;
  const [isTrackpadExpanded, setIsTrackpadExpanded] = useState(false);
  const [textTransferDraft, setTextTransferDraft] = useState("");
  const { emit, onTouchCancel, onTouchEnd, onTouchMove, onTouchStart, sendSpecial, sendText, sleepPc } = usePointerInput({
    send: connection.send,
    state: connection.state,
    trackpadSettings
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
    setLiveTyping
  } = useKeyboardInput(emit);
  const { canUseSpeech, dictationText, isListening, setDictationText, startSpeech, stopSpeech } = useSpeechDictation();
  const { requestAudioState, state: connectionState, supportsVolumeControl } = connection;

  useEffect(() => {
    const trackpadVolumeVisible = tab === "trackpad" && showVolumeControl && !isTrackpadExpanded;
    if (connectionState === "paired" && supportsVolumeControl && (trackpadVolumeVisible || tab === "remote" || tab === "presentation")) {
      requestAudioState();
    }
  }, [connectionState, isTrackpadExpanded, requestAudioState, showVolumeControl, supportsVolumeControl, tab]);

  const toggleMute = () => {
    if (connection.supportsVolumeControl) {
      emit({ type: "audio.mute.toggle" });
    }
  };

  const setVolume = (volume: number) => {
    if (!connection.supportsVolumeControl) {
      return;
    }

    const nextVolume = Math.max(0, Math.min(100, Math.round(volume)));
    setOptimisticAudioState({
      source: connection.audioState,
      value: { type: "audio.state", volume: nextVolume, muted: false }
    });
    emit({ type: "audio.volume.set", volume: nextVolume });
  };

  return (
    <AppModeContent
      tab={tab}
      shouldShowSplitMode={shouldShowSplitMode}
      supportsGestureDebug={connection.supportsGestureDebug}
      trackpadMode={{
        audioState: displayedAudioState,
        isExpanded: isTrackpadExpanded,
        onMouseButtonDown: (button) => {
          triggerHapticFeedback(trackpadSettings);
          emit({ type: "pointer.button", button, action: "down" });
        },
        onMouseButtonUp: (button) => { emit({ type: "pointer.button", button, action: "up" }); },
        onSetVolume: setVolume,
        onToggleExpanded: () => { setIsTrackpadExpanded((current) => !current); },
        onToggleMute: toggleMute,
        onTouchCancel,
        onTouchEnd,
        onTouchMove,
        onTouchStart,
        supportsVolumeControl: connection.supportsVolumeControl,
        compactModeSelector: showTrackpadCompactModeSelector ? trackpadCompactModeSelector : undefined,
        trackpadSettings
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
        toLiveKeyboardValue
      }}
      presentationMode={{
        audioState: displayedAudioState,
        blackoutAvailable: connection.powerCapabilities?.blackoutDisplay === true,
        capability: connection.presentationCapability,
        connected: connection.state === "paired",
        pending: connection.pendingPresentationCommand,
        pendingPowerAction: connection.pendingPowerAction,
        reportSavePending: connection.pendingPresentationReportSave !== null,
        reportSaveResult: connection.presentationReportSaveResult,
        reportSavingAvailable: connection.presentationCapability?.canSaveReports === true,
        result: connection.presentationResult,
        onCommand: connection.requestPresentationCommand,
        onMute: () => { sendSpecial("VolumeMute"); },
        onPowerAction: connection.requestPowerAction,
        onSaveReport: connection.requestPresentationReportSave,
        onSessionActiveChange: onPresentationSessionActiveChange,
        onVolumeDown: () => { sendSpecial("VolumeDown"); },
        onVolumeUp: () => { sendSpecial("VolumeUp"); }
      }}
      remoteMode={{
        appLaunchActions: connection.hostStatus?.appLaunchActions ?? [],
        audioState: displayedAudioState,
        awakeControl: {
          awake: connection.awakeCapability,
          awakeResult: connection.awakeResult,
          onAwakeChange: connection.requestAwakeChange,
          pendingAwakeChange: connection.pendingAwakeChange
        },
        isConnected: connection.state === "paired",
        onPointerButtonClick: (button) => { emit({ type: "pointer.button", button, action: "click" }); },
        onPointerMove: (dx, dy) => { emit({ type: "pointer.move", dx, dy }); },
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
        sendSpecial
      }}
      dictationMode={{ canUseSpeech, dictationText, isListening, sendText, setDictationText, startSpeech, stopSpeech }}
      textTransferMode={{
        clearAfterSending: appSettings.clearTextAfterSending,
        clientId: connection.clientId,
        draft: textTransferDraft,
        leftHandedButtons: trackpadSettings.leftHandedButtons,
        onClearAfterSendingChange,
        onDraftChange: setTextTransferDraft,
        onPointerButtonClick: (button) => { emit({ type: "pointer.button", button, action: "click" }); },
        onTouchCancel,
        onTouchEnd,
        onTouchMove,
        onTouchStart,
        pending: connection.pendingTextTransfer,
        requestTextTransfer: connection.requestTextTransfer,
        result: connection.textTransferResult,
        supported: connection.supportsTextTransfer,
        target: connection.hostStatus?.textTransferTarget
      }}
      clipboardReadMode={{
        clientId: connection.clientId,
        permission: connection.clipboardReadPermission,
        pending: connection.pendingClipboardRead,
        result: connection.clipboardReadResult,
        text: connection.clipboardText,
        onCopyFeedback: onClipboardCopyFeedback,
        onGetText: connection.requestClipboardRead,
        onLoadSnippet: (snippet) => { connection.setClipboardText(snippet.text); },
        onTextChange: connection.setClipboardText
      }}
      gestureDebugMode={{ trackpadSettings }}
    />
  );
}
