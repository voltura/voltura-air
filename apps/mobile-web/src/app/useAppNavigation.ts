import { useEffect, useMemo, useState, type Dispatch, type SetStateAction } from "react";
import { getModeDefinition, getModeTabs, type AppTab, type MainAppTab } from "./appModeTabs";
import type { AppSettings } from "../foundation/settings/appSettings";
import type { TrackpadSettings } from "../foundation/input/gestures";
import { getStableScreenOrientation, supportsSplitModeLayout } from "./splitModeLayout";

type NavigationTrackpadSettings = Pick<
  TrackpadSettings,
  "enableSplitMode" | "splitShowStatusRow"
>;

interface AppNavigationOptions {
  fourthMode: AppSettings["fourthMode"];
  isPaired: boolean;
  onEnterRemote: () => void;
  presentationAvailable: boolean;
  showModeButtons?: boolean;
  supportsGestureDebug: boolean;
  trackpadSettings: NavigationTrackpadSettings;
}

export type ModeSelectorAnchor = "header" | "trackpad";

export interface AppNavigation {
  activeModeTab: ReturnType<typeof getModeDefinition> | undefined;
  canShowModeNavigation: boolean;
  closeModeSelector: () => void;
  closeTransientSurfaces: () => void;
  isBottomModeNavigationVisible: boolean;
  isModeButtonsVisible: boolean;
  isModeSelectorOpen: boolean;
  modeSelectorAnchor: ModeSelectorAnchor | null;
  isRemoteUtilityPanelOpen: boolean;
  isSettingsOpen: boolean;
  modeTabs: ReturnType<typeof getModeTabs>;
  openGestureDebug: () => void;
  openSettings: () => void;
  openModeFromMenu: (mode: MainAppTab) => void;
  selectModeTab: (tab: MainAppTab, source?: "tabs" | "selector" | "settings" | "menu") => void;
  setIsRemoteUtilityPanelOpen: Dispatch<SetStateAction<boolean>>;
  setIsSettingsOpen: Dispatch<SetStateAction<boolean>>;
  shellClassName: string;
  shouldShowSplitMode: boolean;
  showTrackpadCompactModeSelector: boolean;
  tab: AppTab;
  toggleModeSelector: (anchor?: ModeSelectorAnchor) => void;
}

export function useAppNavigation({
  fourthMode,
  isPaired,
  onEnterRemote,
  presentationAvailable,
  showModeButtons = true,
  supportsGestureDebug,
  trackpadSettings
}: AppNavigationOptions): AppNavigation {
  const [requestedTab, setRequestedTab] = useState<AppTab>("trackpad");
  const [isSettingsOpen, setIsSettingsOpen] = useState(false);
  const [canUseSplitMode, setCanUseSplitMode] = useState(readSplitModeSupport);
  const [areModeTabsCollapsed, setAreModeTabsCollapsed] = useState(false);
  const [modeSelectorAnchor, setModeSelectorAnchor] = useState<ModeSelectorAnchor | null>(null);
  const [isRemoteUtilityPanelOpen, setIsRemoteUtilityPanelOpen] = useState(false);
  const modeTabs = useMemo(() => getModeTabs(fourthMode, presentationAvailable), [fourthMode, presentationAvailable]);

  useEffect(() => {
    const onResize = () => { setCanUseSplitMode(readSplitModeSupport()); };
    const orientation = screen.orientation;
    onResize();
    window.addEventListener("resize", onResize);
    orientation?.addEventListener("change", onResize);
    return () => {
      window.removeEventListener("resize", onResize);
      orientation?.removeEventListener("change", onResize);
    };
  }, []);

  const tab = requestedTab === "debug" && !supportsGestureDebug
    ? "trackpad"
    : requestedTab === "presentation" && !presentationAvailable
      ? "dictation"
      : requestedTab;
  const effectiveModeSelectorAnchor = tab === "debug" ? null : modeSelectorAnchor;
  const effectiveModeSelectorOpen = effectiveModeSelectorAnchor !== null;
  const effectiveModeTabsCollapsed = tab === "debug" ? false : areModeTabsCollapsed;
  const effectiveRemoteUtilityPanelOpen = tab === "remote" && isRemoteUtilityPanelOpen;

  const selectModeTab = (nextTab: MainAppTab, source: "tabs" | "selector" | "settings" | "menu" = "tabs") => {
    if (tab === nextTab) {
      if (source !== "settings" && source !== "menu") {
        setAreModeTabsCollapsed(source === "tabs");
      }
      setModeSelectorAnchor(null);
      return;
    }

    if (nextTab === "remote" && source !== "settings") {
      onEnterRemote();
    }

    setRequestedTab(nextTab);
    setIsRemoteUtilityPanelOpen(false);
    setAreModeTabsCollapsed(false);
    setModeSelectorAnchor(null);
  };

  const openModeFromMenu = (mode: MainAppTab) => {
    if (mode === "presentation" && !presentationAvailable) {
      return;
    }

    selectModeTab(mode, "menu");
    setIsSettingsOpen(false);
  };

  const openGestureDebug = () => {
    setRequestedTab("debug");
    setModeSelectorAnchor(null);
    setIsRemoteUtilityPanelOpen(false);
    setAreModeTabsCollapsed(false);
    setIsSettingsOpen(false);
  };

  const closeTransientSurfaces = () => {
    setModeSelectorAnchor(null);
    setIsSettingsOpen(false);
  };

  const shouldShowSplitMode = canUseSplitMode && trackpadSettings.enableSplitMode && (tab === "trackpad" || tab === "keyboard");
  const canShowModeNavigation = isPaired;
  const isModeButtonsVisible = canShowModeNavigation && showModeButtons && !effectiveModeTabsCollapsed && !effectiveRemoteUtilityPanelOpen;
  const isBottomModeNavigationVisible = isModeButtonsVisible;
  const showTrackpadCompactModeSelector = shouldShowSplitMode && !trackpadSettings.splitShowStatusRow && !isModeButtonsVisible && canShowModeNavigation;
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
    !showModeButtons && "mode-buttons-hidden",
    effectiveModeTabsCollapsed && "mode-tabs-collapsed",
    shouldShowSplitMode && trackpadSettings.splitShowStatusRow && "split-show-header",
    shouldShowSplitMode && isModeButtonsVisible && "split-show-mode-buttons",
    effectiveModeSelectorOpen && "mode-selector-open"
  ].filter(Boolean).join(" ");

  return {
    activeModeTab: tab === "debug" ? undefined : getModeDefinition(tab),
    canShowModeNavigation,
    closeModeSelector: () => { setModeSelectorAnchor(null); },
    closeTransientSurfaces,
    isBottomModeNavigationVisible,
    isModeButtonsVisible,
    isModeSelectorOpen: effectiveModeSelectorOpen,
    isRemoteUtilityPanelOpen: effectiveRemoteUtilityPanelOpen,
    isSettingsOpen,
    modeTabs,
    modeSelectorAnchor: effectiveModeSelectorAnchor,
    openGestureDebug,
    openSettings: () => { setIsSettingsOpen(true); },
    openModeFromMenu,
    selectModeTab,
    setIsRemoteUtilityPanelOpen,
    setIsSettingsOpen,
    shellClassName,
    shouldShowSplitMode,
    showTrackpadCompactModeSelector,
    tab,
    toggleModeSelector: (anchor = "header") => {
      setModeSelectorAnchor((current) => current === anchor ? null : anchor);
    }
  };
}

function readSplitModeSupport(): boolean {
  const isTouchDevice = navigator.maxTouchPoints > 0;
  return supportsSplitModeLayout(
    window.innerWidth,
    window.innerHeight,
    isTouchDevice ? getStableScreenOrientation(screen) : null,
    isTouchDevice
  );
}
