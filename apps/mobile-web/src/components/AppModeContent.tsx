import type { ComponentProps } from "react";
import { ClipboardReadMode } from "./ClipboardReadMode";
import { DictationMode } from "./DictationMode";
import { GestureDebugMode } from "./GestureDebugMode";
import { KeyboardMode } from "./KeyboardMode";
import { PresentationMode } from "./PresentationMode";
import { RemoteMode } from "./RemoteMode";
import { TrackpadMode } from "./TrackpadMode";
import { TextTransferMode } from "./TextTransferMode";
import type { AppTab as Tab } from "../appModeTabs";

type AppModeContentProps = {
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
};

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
