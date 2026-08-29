import type { RefObject } from "react";
import { Circle, Files, Menu, MousePointer2 } from "lucide-react";
import type { MainAppTab, ModeDefinition } from "./appModeTabs";
import type { ConnectionState } from "../foundation/connection/connectionTypes";
import type { RemoteModeId } from "../foundation/settings/remoteSettings";
import { useDeveloperRefreshLongPress } from "./useDeveloperRefreshLongPress";
import { CompactModeSelectorButton, ModeSelector } from "./ModeNavigation";

interface AppHeaderProps {
  activeMode?: ModeDefinition | undefined;
  canShowModeNavigation: boolean;
  compactModeButtonRef: RefObject<HTMLButtonElement | null>;
  connectionPcName: string;
  developerMode: boolean;
  isModeSelectorOpen: boolean;
  message: string;
  fileJobCount?: number;
  hasConnectionError?: boolean;
  modeTabs: ModeDefinition[];
  onCloseModeSelector: () => void;
  onOpenSettings: () => void;
  onOpenFileJobs?: () => void;
  onSelectMode: (tab: MainAppTab) => void;
  onSelectRemoteMode: (mode: RemoteModeId) => void;
  onToggleModeSelector: () => void;
  remoteMode: RemoteModeId;
  refreshInstalledApp: () => void | Promise<void>;
  state: ConnectionState;
  tab: MainAppTab | "debug";
}

export function AppHeader({
  activeMode,
  canShowModeNavigation,
  compactModeButtonRef,
  connectionPcName,
  developerMode,
  isModeSelectorOpen,
  message,
  fileJobCount = 0,
  hasConnectionError = false,
  modeTabs,
  onCloseModeSelector,
  onOpenSettings,
  onOpenFileJobs,
  onSelectMode,
  onSelectRemoteMode,
  onToggleModeSelector,
  remoteMode,
  refreshInstalledApp,
  state,
  tab,
}: AppHeaderProps) {
  const developerBrandLongPress = useDeveloperRefreshLongPress(developerMode, refreshInstalledApp);

  return (
    <>
      <header className="top-bar">
        <div className="brand-group">
          <button
            className="icon-button"
            type="button"
            aria-label="Open menu"
            onClick={onOpenSettings}
          >
            <Menu aria-hidden="true" />
          </button>
          <div
            {...developerBrandLongPress}
            className={`brand ${developerBrandLongPress.className}`}
          >
            <MousePointer2 aria-hidden="true" />
            <span>Voltura Air</span>
          </div>
          {canShowModeNavigation && activeMode && (
            <CompactModeSelectorButton
              buttonRef={compactModeButtonRef}
              activeMode={activeMode}
              isOpen={isModeSelectorOpen}
              onToggle={onToggleModeSelector}
            />
          )}
        </div>
        {fileJobCount > 0 && onOpenFileJobs && (
          <button
            className="header-file-job"
            type="button"
            onClick={onOpenFileJobs}
            aria-label={`Open ${fileJobCount} active file operation${fileJobCount === 1 ? "" : "s"}`}
          >
            <Files aria-hidden="true" />
            <span>{fileJobCount}</span>
          </button>
        )}
        <div className={`status ${hasConnectionError ? "error" : state}`} title={message}>
          <Circle aria-hidden="true" />
          <span className="status-full">{message}</span>
          <span className="status-compact">{connectionPcName}</span>
        </div>
      </header>

      {canShowModeNavigation && isModeSelectorOpen && (
        <ModeSelector
          modeTabs={modeTabs}
          remoteMode={remoteMode}
          tab={tab}
          onClose={onCloseModeSelector}
          onSelect={onSelectMode}
          onSelectRemoteMode={onSelectRemoteMode}
        />
      )}
    </>
  );
}
