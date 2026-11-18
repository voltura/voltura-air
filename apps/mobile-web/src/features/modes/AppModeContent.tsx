import { lazy, Suspense, useState, type ComponentProps } from "react";
import { WorkspaceErrorBoundary } from "../../ui/feedback/WorkspaceErrorBoundary";
import { DictationMode } from "./dictation/DictationMode";
import { KeyboardMode } from "./keyboard/KeyboardMode";
import { PresentationMode } from "./presentation/PresentationMode";
import { RemoteMode } from "./remote/RemoteMode";
import { TrackpadMode } from "./trackpad/TrackpadMode";
import type { AppTab as Tab } from "./modeTypes";

const TextTransferMode = lazy(() =>
  import("./text-transfer/TextTransferMode").then((module) => ({
    default: module.TextTransferMode,
  })),
);

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
  onTextTransferBack: () => void;
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
  onTextTransferBack,
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

  if (tab === "trackpad" || tab === "keyboard") {
    if (!shouldShowSplitMode) {
      return tab === "trackpad" ? trackpad : keyboard;
    }
    return (
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
    return (
      <WorkspaceErrorBoundary
        featureName="Send text to PC"
        onBack={onTextTransferBack}
        recoveryMessage="Go back to Trackpad. To reopen the editor, use Refresh app in Settings."
      >
        <Suspense fallback={<div className="workspace-loading">Opening Send text to PC…</div>}>
          <TextTransferMode {...textTransferMode} />
        </Suspense>
      </WorkspaceErrorBoundary>
    );
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
