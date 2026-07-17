import { useEffect, useState } from "react";
import type { AppTab } from "../../appModeTabs";
import type { AppSettings } from "../../appSettings";
import type { TrackpadSettings } from "../../gestures";
import { triggerHapticFeedback } from "../../hapticFeedback";
import { useKeyboardInput } from "../../input/useKeyboardInput";
import { usePointerInput } from "../../input/usePointerInput";
import { useSpeechDictation } from "../../input/useSpeechDictation";
import type { KeyboardSettings } from "../../keyboardSettings";
import { toLiveKeyboardValue } from "../../keyboardDelta";
import type { RemoteSettings } from "../../remoteSettings";
import type { useVolturaAirConnection } from "../../useVolturaAirConnection";
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
  | "pendingTextTransfer"
  | "pendingUrlOpen"
  | "powerActionResult"
  | "powerCapabilities"
  | "presentationCapability"
  | "presentationResult"
  | "requestAppLaunch"
  | "requestAudioState"
  | "requestAwakeChange"
  | "requestClipboardRead"
  | "requestPowerAction"
  | "requestPresentationCommand"
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
  onRemoteUtilityPanelOpenChange: (isOpen: boolean) => void;
  remoteSettings: RemoteSettings;
  shouldShowSplitMode: boolean;
  showVolumeControl: boolean;
  tab: AppTab;
  trackpadSettings: TrackpadSettings;
}

export function ModeWorkspace({
  appSettings,
  connection,
  keyboardSettings,
  onClearAfterSendingChange,
  onRemoteUtilityPanelOpenChange,
  remoteSettings,
  shouldShowSplitMode,
  showVolumeControl,
  tab,
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
  const { canUseSpeech, dictationText, isListening, setDictationText, startSpeech, stopSpeech } = useSpeechDictation(sendText);
  const { requestAudioState, state: connectionState, supportsVolumeControl } = connection;

  useEffect(() => {
    const trackpadVolumeVisible = tab === "trackpad" && showVolumeControl && !isTrackpadExpanded;
    if (connectionState === "paired" && supportsVolumeControl && (trackpadVolumeVisible || tab === "remote")) {
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
        capability: connection.presentationCapability,
        connected: connection.state === "paired",
        pending: connection.pendingPresentationCommand,
        result: connection.presentationResult,
        onCommand: connection.requestPresentationCommand,
        onPowerAction: connection.requestPowerAction
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
        onGetText: connection.requestClipboardRead,
        onLoadSnippet: (snippet) => { connection.setClipboardText(snippet.text); }
      }}
      gestureDebugMode={{ trackpadSettings }}
    />
  );
}
