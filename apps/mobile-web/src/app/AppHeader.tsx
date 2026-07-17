import { ChevronDown, Circle, Menu, MousePointer2 } from "lucide-react";
import type { MainAppTab, ModeDefinition } from "../appModeTabs";
import type { ConnectionState } from "../connection/connectionTypes";
import { useDeveloperRefreshLongPress } from "./useDeveloperRefreshLongPress";
import { ModeSelector } from "./ModeNavigation";

interface AppHeaderProps {
  activeMode?: ModeDefinition | undefined;
  canShowModeNavigation: boolean;
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
  const ActiveModeIcon = activeMode?.Icon;

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
          {canShowModeNavigation && ActiveModeIcon && activeMode && (
            <button
              className="compact-mode-button"
              type="button"
              aria-expanded={isModeSelectorOpen}
              aria-haspopup="menu"
              aria-label="Change mode"
              title={`Change mode (${activeMode.label})`}
              onClick={onToggleModeSelector}
            >
              <ActiveModeIcon aria-hidden="true" />
              <ChevronDown aria-hidden="true" />
            </button>
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
