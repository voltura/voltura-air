import { lazy, Suspense, useState, type ComponentProps } from "react";
import { DictationMode } from "./dictation/DictationMode";
import { KeyboardMode } from "./keyboard/KeyboardMode";
import { PresentationMode } from "./presentation/PresentationMode";
import { RemoteMode } from "./remote/RemoteMode";
import { TrackpadMode } from "./trackpad/TrackpadMode";
import { TextTransferMode } from "./text-transfer/TextTransferMode";
import type { AppTab as Tab } from "./modeTypes";

const ClipboardReadMode = lazy(() =>
  import("./clipboard-read/ClipboardReadMode").then((module) => ({
    default: module.ClipboardReadMode,
  })),
);

const GestureDebugMode = lazy(() =>
  import("./gesture-debug/GestureDebugMode").then((module) => ({
    default: module.GestureDebugMode,
  })),
);

interface AppModeContentProps {
  tab: Tab;
  shouldShowSplitMode: boolean;
  supportsGestureDebug: boolean;
  trackpadMode: ComponentProps<typeof TrackpadMode>;
  keyboardMode: ComponentProps<typeof KeyboardMode>;
  presentationMode: Omit<ComponentProps<typeof PresentationMode>, "renderTrackpad">;
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
  gestureDebugMode,
}: AppModeContentProps) {
  const [isLiveTypingInfoOpen, setIsLiveTypingInfoOpen] = useState(false);
  const trackpad = <TrackpadMode {...trackpadMode} />;
  const keyboard = (
    <KeyboardMode
      {...keyboardMode}
      liveTypingInfoOpen={isLiveTypingInfoOpen}
      onLiveTypingInfoOpenChange={setIsLiveTypingInfoOpen}
    />
  );

  if (tab === "trackpad") {
    return shouldShowSplitMode ? (
      <div
        className={`split-mode-shell split-trackpad-${trackpadMode.trackpadSettings.splitTrackpadPlacement}`}
      >
        <div className="split-keyboard-pane" aria-label="Split keyboard panel">
          {keyboard}
        </div>
        <div className="split-trackpad-pane" aria-label="Split trackpad panel">
          {trackpad}
        </div>
      </div>
    ) : (
      trackpad
    );
  }

  if (tab === "keyboard") {
    return shouldShowSplitMode ? (
      <div
        className={`split-mode-shell split-trackpad-${trackpadMode.trackpadSettings.splitTrackpadPlacement}`}
      >
        <div className="split-keyboard-pane" aria-label="Split keyboard panel">
          {keyboard}
        </div>
        <div className="split-trackpad-pane" aria-label="Split trackpad panel">
          {trackpad}
        </div>
      </div>
    ) : (
      keyboard
    );
  }

  if (tab === "remote") {
    return <RemoteMode {...remoteMode} />;
  }

  if (tab === "presentation") {
    return (
      <PresentationMode
        {...presentationMode}
        renderTrackpad={({ isFullscreen, onToggleFullscreen }) => (
          <TrackpadMode
            {...trackpadMode}
            audioState={null}
            compactModeSelector={undefined}
            isExpanded={isFullscreen}
            onToggleExpanded={onToggleFullscreen}
            showRestoredMouseButtons={false}
            supportsVolumeControl={false}
          />
        )}
      />
    );
  }

  if (tab === "dictation") {
    return <DictationMode {...dictationMode} />;
  }

  if (tab === "text-transfer") {
    return <TextTransferMode {...textTransferMode} />;
  }

  if (tab === "clipboard-read") {
    return (
      <Suspense fallback={<div className="workspace-loading">Opening Get text from PC…</div>}>
        <ClipboardReadMode {...clipboardReadMode} />
      </Suspense>
    );
  }

  return supportsGestureDebug ? (
    <Suspense fallback={<div className="workspace-loading">Opening Gesture debug…</div>}>
      <GestureDebugMode {...gestureDebugMode} />
    </Suspense>
  ) : null;
}
