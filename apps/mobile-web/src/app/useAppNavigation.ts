import { useEffect, useMemo, useState, type Dispatch, type SetStateAction } from "react";
import { getModeDefinition, getModeTabs, type AppTab, type MainAppTab, type ToolAppTab } from "./appModeTabs";
import type { AppSettings } from "../foundation/settings/appSettings";
import type { TrackpadSettings } from "../foundation/input/gestures";
import { supportsSplitModeLayout } from "./splitModeLayout";

type NavigationTrackpadSettings = Pick<
  TrackpadSettings,
  "enableSplitMode" | "splitShowModeButtons" | "splitShowStatusRow"
>;

interface AppNavigationOptions {
  fourthMode: AppSettings["fourthMode"];
  isPaired: boolean;
  onEnterRemote: () => void;
  presentationAvailable: boolean;
  supportsGestureDebug: boolean;
  trackpadSettings: NavigationTrackpadSettings;
}

export interface AppNavigation {
  activeModeTab: ReturnType<typeof getModeDefinition> | undefined;
  canShowModeNavigation: boolean;
  closeModeSelector: () => void;
  closeTransientSurfaces: () => void;
  isBottomModeNavigationVisible: boolean;
  isModeSelectorOpen: boolean;
  isRemoteUtilityPanelOpen: boolean;
  isSettingsOpen: boolean;
  modeTabs: ReturnType<typeof getModeTabs>;
  openGestureDebug: () => void;
  openSettings: () => void;
  openToolFromMenu: (tool: ToolAppTab) => void;
  selectModeTab: (tab: MainAppTab, source?: "tabs" | "selector") => void;
  setIsRemoteUtilityPanelOpen: Dispatch<SetStateAction<boolean>>;
  setIsSettingsOpen: Dispatch<SetStateAction<boolean>>;
  shellClassName: string;
  shouldShowSplitMode: boolean;
  tab: AppTab;
  toggleModeSelector: () => void;
}

export function useAppNavigation({
  fourthMode,
  isPaired,
  onEnterRemote,
  presentationAvailable,
  supportsGestureDebug,
  trackpadSettings
}: AppNavigationOptions): AppNavigation {
  const [requestedTab, setRequestedTab] = useState<AppTab>("trackpad");
  const [isSettingsOpen, setIsSettingsOpen] = useState(false);
  const [canUseSplitMode, setCanUseSplitMode] = useState(() => supportsSplitModeLayout(window.innerWidth, window.innerHeight));
  const [areModeTabsCollapsed, setAreModeTabsCollapsed] = useState(false);
  const [isModeSelectorOpen, setIsModeSelectorOpen] = useState(false);
  const [isRemoteUtilityPanelOpen, setIsRemoteUtilityPanelOpen] = useState(false);
  const modeTabs = useMemo(() => getModeTabs(fourthMode, presentationAvailable), [fourthMode, presentationAvailable]);

  useEffect(() => {
    const onResize = () => { setCanUseSplitMode(supportsSplitModeLayout(window.innerWidth, window.innerHeight)); };
    onResize();
    window.addEventListener("resize", onResize);
    return () => { window.removeEventListener("resize", onResize); };
  }, []);

  const tab = requestedTab === "debug" && !supportsGestureDebug
    ? "trackpad"
    : requestedTab === "presentation" && !presentationAvailable
      ? "dictation"
      : requestedTab;
  const effectiveModeTabsCollapsed = tab === "debug" ? false : areModeTabsCollapsed;
  const effectiveModeSelectorOpen = tab === "debug" ? false : isModeSelectorOpen;
  const effectiveRemoteUtilityPanelOpen = tab === "remote" && isRemoteUtilityPanelOpen;

  const selectModeTab = (nextTab: MainAppTab, source: "tabs" | "selector" = "tabs") => {
    if (nextTab === "remote") {
      onEnterRemote();
    }

    if (tab === nextTab) {
      setAreModeTabsCollapsed(source === "selector" ? false : true);
      setIsModeSelectorOpen(false);
      return;
    }

    setRequestedTab(nextTab);
    setIsRemoteUtilityPanelOpen(false);
    setAreModeTabsCollapsed(false);
    setIsModeSelectorOpen(false);
  };

  const openToolFromMenu = (tool: ToolAppTab) => {
    if (tool === "presentation" && !presentationAvailable) {
      return;
    }

    setRequestedTab(tool);
    setIsRemoteUtilityPanelOpen(false);
    setAreModeTabsCollapsed(false);
    setIsModeSelectorOpen(false);
    setIsSettingsOpen(false);
  };

  const openGestureDebug = () => {
    setRequestedTab("debug");
    setIsModeSelectorOpen(false);
    setIsRemoteUtilityPanelOpen(false);
    setAreModeTabsCollapsed(false);
    setIsSettingsOpen(false);
  };

  const closeTransientSurfaces = () => {
    setIsModeSelectorOpen(false);
    setIsSettingsOpen(false);
  };

  const shouldShowSplitMode = canUseSplitMode && trackpadSettings.enableSplitMode && (tab === "trackpad" || tab === "keyboard");
  const canShowModeNavigation = isPaired;
  const isBottomModeNavigationVisible = canShowModeNavigation && !effectiveModeTabsCollapsed && !effectiveRemoteUtilityPanelOpen;
  const shellClassName = [
    "app-shell",
    isBottomModeNavigationVisible && "has-mode-navigation",
    canShowModeNavigation && !isBottomModeNavigationVisible && "bottom-mode-navigation-hidden",
    tab === "trackpad" && "trackpad-active",
    tab === "remote" && "remote-active",
    tab === "presentation" && "presentation-active",
    tab === "text-transfer" && "text-transfer-active",
    tab === "clipboard-read" && "clipboard-read-active",
    effectiveRemoteUtilityPanelOpen && "remote-utility-open",
    shouldShowSplitMode && "split-mode-active",
    shouldShowSplitMode && trackpadSettings.splitShowModeButtons && "split-show-mode-buttons",
    shouldShowSplitMode && trackpadSettings.splitShowStatusRow && "split-show-status-row",
    effectiveModeTabsCollapsed && "mode-tabs-collapsed",
    effectiveModeSelectorOpen && "mode-selector-open"
  ].filter(Boolean).join(" ");

  return {
    activeModeTab: tab === "debug" ? undefined : getModeDefinition(tab),
    canShowModeNavigation,
    closeModeSelector: () => { setIsModeSelectorOpen(false); },
    closeTransientSurfaces,
    isBottomModeNavigationVisible,
    isModeSelectorOpen: effectiveModeSelectorOpen,
    isRemoteUtilityPanelOpen: effectiveRemoteUtilityPanelOpen,
    isSettingsOpen,
    modeTabs,
    openGestureDebug,
    openSettings: () => { setIsSettingsOpen(true); },
    openToolFromMenu,
    selectModeTab,
    setIsRemoteUtilityPanelOpen,
    setIsSettingsOpen,
    shellClassName,
    shouldShowSplitMode,
    tab,
    toggleModeSelector: () => { setIsModeSelectorOpen((current) => !current); }
  };
}
