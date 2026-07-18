import type { ComponentProps } from "react";
import { ClipboardReadMode } from "./clipboard-read/ClipboardReadMode";
import { DictationMode } from "./dictation/DictationMode";
import { GestureDebugMode } from "./gesture-debug/GestureDebugMode";
import { KeyboardMode } from "./keyboard/KeyboardMode";
import { PresentationMode } from "./presentation/PresentationMode";
import { RemoteMode } from "./remote/RemoteMode";
import { TrackpadMode } from "./trackpad/TrackpadMode";
import { TextTransferMode } from "./text-transfer/TextTransferMode";
import type { AppTab as Tab } from "./modeTypes";

interface AppModeContentProps {
  tab: Tab;
  shouldShowSplitMode: boolean;
  supportsGestureDebug: boolean;
  trackpadMode: ComponentProps<typeof TrackpadMode>;
  keyboardMode: ComponentProps<typeof KeyboardMode>;
  presentationMode: ComponentProps<typeof PresentationMode>;
  remoteMode: ComponentProps<typeof RemoteMode>;
  dictationMode: ComponentProps<typeof DictationMode>;
  textTransferMode: ComponentProps<typeof TextTransferMode>;
  clipboardReadMode: ComponentProps<typeof ClipboardReadMode>;
  gestureDebugMode: ComponentProps<typeof GestureDebugMode>;
}

export function AppModeContent({
  tab,
  shouldShowSplitMode,
  supportsGestureDebug,
  trackpadMode,
  keyboardMode,
  presentationMode,
  remoteMode,
  dictationMode,
  textTransferMode,
  clipboardReadMode,
  gestureDebugMode
}: AppModeContentProps) {
  const trackpad = <TrackpadMode {...trackpadMode} />;
  const keyboard = <KeyboardMode {...keyboardMode} />;

  if (tab === "trackpad") {
    return shouldShowSplitMode ? (
      <div className={`split-mode-shell split-trackpad-${trackpadMode.trackpadSettings.splitTrackpadPlacement}`}>
        <div className="split-keyboard-pane" aria-label="Split keyboard panel">{keyboard}</div>
        <div className="split-trackpad-pane" aria-label="Split trackpad panel">{trackpad}</div>
      </div>
    ) : trackpad;
  }

  if (tab === "keyboard") {
    return shouldShowSplitMode ? (
      <div className={`split-mode-shell split-trackpad-${trackpadMode.trackpadSettings.splitTrackpadPlacement}`}>
        <div className="split-keyboard-pane" aria-label="Split keyboard panel">{keyboard}</div>
        <div className="split-trackpad-pane" aria-label="Split trackpad panel">{trackpad}</div>
      </div>
    ) : keyboard;
  }

  if (tab === "remote") {
    return <RemoteMode {...remoteMode} />;
  }

  if (tab === "presentation") {
    return <PresentationMode {...presentationMode} />;
  }

  if (tab === "dictation") {
    return <DictationMode {...dictationMode} />;
  }

  if (tab === "text-transfer") {
    return <TextTransferMode {...textTransferMode} />;
  }

  if (tab === "clipboard-read") {
    return <ClipboardReadMode {...clipboardReadMode} />;
  }

  return supportsGestureDebug ? <GestureDebugMode {...gestureDebugMode} /> : null;
}
