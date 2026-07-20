import type { RefObject } from "react";
import { Circle, Menu, MousePointer2 } from "lucide-react";
import type { MainAppTab, ModeDefinition } from "./appModeTabs";
import type { ConnectionState } from "../foundation/connection/connectionTypes";
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
  modeTabs: ModeDefinition[];
  onCloseModeSelector: () => void;
  onOpenSettings: () => void;
  onSelectMode: (tab: MainAppTab) => void;
  onToggleModeSelector: () => void;
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
  modeTabs,
  onCloseModeSelector,
  onOpenSettings,
  onSelectMode,
  onToggleModeSelector,
  refreshInstalledApp,
  state,
  tab
}: AppHeaderProps) {
  const developerBrandLongPress = useDeveloperRefreshLongPress(developerMode, refreshInstalledApp);

  return (
    <>
      <header className="top-bar">
        <div className="brand-group">
          <button className="icon-button" type="button" aria-label="Open menu" onClick={onOpenSettings}>
            <Menu aria-hidden="true" />
          </button>
          <div {...developerBrandLongPress} className={`brand ${developerBrandLongPress.className}`}>
            <MousePointer2 aria-hidden="true" />
            <span>Voltura Air</span>
          </div>
          {canShowModeNavigation && activeMode && (
            <CompactModeSelectorButton buttonRef={compactModeButtonRef} activeMode={activeMode} isOpen={isModeSelectorOpen} onToggle={onToggleModeSelector} />
          )}
        </div>
        <div className={`status ${state}`} title={message}>
          <Circle aria-hidden="true" />
          <span className="status-full">{message}</span>
          <span className="status-compact">{connectionPcName}</span>
        </div>
      </header>

      {canShowModeNavigation && isModeSelectorOpen && (
        <ModeSelector modeTabs={modeTabs} tab={tab} onClose={onCloseModeSelector} onSelect={onSelectMode} />
      )}
    </>
  );
}
