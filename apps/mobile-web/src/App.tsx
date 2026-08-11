import { lazy, Suspense, useCallback, useEffect, useMemo, useRef, useState } from "react";
import { getAvailableToolModeIds, toolModeDefinitions } from "./app/appModeTabs";
import { createSettingsActions, SettingsDrawer } from "./features/settings";
import { parsePairingLink } from "./foundation/pairing/pairingLink";
import { getPcDisplayName } from "./foundation/pairing/pcDisplayName";
import type { RemoteLaunchAction } from "./foundation/protocol/messages";
import { buildMobileDiagnostics } from "./foundation/diagnostics/mobileDiagnostics";
import type { RemoteSettings } from "./foundation/settings/remoteSettings";
import { useVolturaAirConnection } from "./foundation/connection/useVolturaAirConnection";
import { usePcSettings } from "./foundation/settings/usePcSettings";
import { usePwaLifecycle } from "./foundation/pwa/usePwaLifecycle";
import { PairingGate, usePairingController } from "./features/pairing";
import { useManualReconnectFeedback } from "./foundation/connection/useManualReconnectFeedback";
import { AppHeader } from "./app/AppHeader";
import { GlobalOperationFeedback } from "./app/GlobalOperationFeedback";
import { CompactModeSelectorButton, ModeNavigation, ModeSelector } from "./app/ModeNavigation";
import { useAppTheme } from "./app/useAppTheme";
import { useAppNavigation } from "./app/useAppNavigation";
import { InputRecoveryNotice } from "./features/input-recovery";
import { ModeWorkspace } from "./features/modes";
import type { AppToastMessage } from "./ui/feedback/AppToast";
import { AnchoredHint } from "./ui/guidance/AnchoredHint";
import { useOneShotHint } from "./ui/guidance/useOneShotHint";
import { ConfirmationDialog } from "./ui/overlays/ConfirmationDialog";
import { ErrorDialog } from "./ui/overlays/ErrorDialog";
import { CustomScreenWorkspace } from "./features/custom-screens";
import { WorkspaceErrorBoundary } from "./app/WorkspaceErrorBoundary";
import { subscribeFileManagerResults } from "./foundation/connection/fileManagerResultBus";
import { ThirdPartyNoticesWorkspace } from "./features/legal";
import { requestGyroPermission, type GyroActivationRequest } from "./foundation/input/gyroMouse";

const ScreenViewWorkspace = lazy(() => import("./features/screen-view"));
const FileManagerWorkspace = lazy(() => import("./features/file-manager"));

export function App() {
  const initialPairing = useMemo(() => parsePairingLink(window.location.href), []);
  const connection = useVolturaAirConnection();
  const {
    state,
    connectionEpoch,
    message,
    send,
    pendingTextTransfer,
    presentationCapability,
    pendingClipboardRead,
    textTransferResult,
    clipboardReadResult,
    pendingAppLaunchId,
    appLaunchResult,
    customScreenDefinition,
    customScreenGetResult,
    customScreenInvokeResult,
    customScreensCapability,
    screenViewCapability,
    fileManagerCapability,
    invokeCustomScreenButton,
    pendingCustomScreenButtonIds,
    requestCustomScreen,
    clientId,
    deviceName,
    activePc,
    pairedPcs,
    reconnectablePcs,
    requestPresentationCommand,
    supportsGestureDebug,
    supportsRemoteLaunch,
    lastConnectionError,
    hostStatus,
    pairWithToken,
    selectPc,
    beginNewPairing,
    connectManualPc,
    disconnectActivePc,
    forgetPc,
    renamePc,
    renameDevice,
    setHostCustomPointer,
    setHostControlDepth,
    setHostShowModeButtons,
    setHostPointerSpeed
  } = connection;
  const { setThemeMode, themeMode } = useAppTheme();
  const [transientFeedback, setTransientFeedback] = useState<AppToastMessage | null>(null);
  const [pendingRemoteLaunch, setPendingRemoteLaunch] = useState<RemoteLaunchAction | null>(null);
  const [suppressedClipboardResultId, setSuppressedClipboardResultId] = useState<string | null>(null);
  const [activeCustomScreenId, setActiveCustomScreenId] = useState<string | null>(null);
  const [isScreenViewOpen, setIsScreenViewOpen] = useState(false);
  const [gyroSelected, setGyroSelected] = useState(false);
  const [gyroActivationRequest, setGyroActivationRequest] = useState<GyroActivationRequest | null>(null);
  const gyroActivationIdRef = useRef(0);
  const handleGyroSelectedChange = useCallback((selected: boolean) => {
    setGyroSelected(selected);
    if (selected) {
      setGyroActivationRequest(null);
    }
  }, []);
  const [activeFileJobCount, setActiveFileJobCount] = useState(0);
  const fileJobStatesRef = useRef(new Map<string, string>());
  useEffect(() => subscribeFileManagerResults((result) => {
    if (result.type !== "file.jobs.status") {return;}
    setActiveFileJobCount(result.jobs.filter((job) => !["completed", "failed", "canceled", "interrupted"].includes(job.state)).length);
    for (const job of result.jobs) {
      const previous = fileJobStatesRef.current.get(job.jobId);
      if (previous && previous !== job.state && job.state === "completed") {
        setTransientFeedback({ tone: "success", message: `${job.operation} completed on the PC.` });
      } else if (previous && previous !== job.state && (job.state === "failed" || job.state === "interrupted")) {
        setTransientFeedback({ tone: "error", message: job.message ?? `${job.operation} did not complete.` });
      }
      fileJobStatesRef.current.set(job.jobId, job.state);
    }
  }), []);
  const [isThirdPartyNoticesOpen, setIsThirdPartyNoticesOpen] = useState(false);
  const connectionErrorKey = lastConnectionError
    ? `${lastConnectionError.code}\n${lastConnectionError.message}`
    : null;
  const [connectionErrorDialog, setConnectionErrorDialog] = useState({
    key: connectionErrorKey,
    dismissed: false
  });
  if (connectionErrorDialog.key !== connectionErrorKey) {
    setConnectionErrorDialog({ key: connectionErrorKey, dismissed: false });
  }
  const inputBlockedByElevation = hostStatus?.inputBlockedByElevation === true;
  const [inputRecoveryDialog, setInputRecoveryDialog] = useState({
    blocked: inputBlockedByElevation,
    dismissed: false
  });
  if (inputRecoveryDialog.blocked !== inputBlockedByElevation) {
    setInputRecoveryDialog({ blocked: inputBlockedByElevation, dismissed: false });
  }
  const isInputRecoveryDialogDismissed = inputRecoveryDialog.dismissed;
  const developerMode = hostStatus?.developerMode === true;
  const { progress: manualReconnectProgress, reconnect: reconnectPc } = useManualReconnectFeedback(activePc?.id ?? null, state, selectPc);

  const hostPointerSpeed = hostStatus?.pointerSpeed;
  const hostDefaultRemoteMode = hostStatus?.defaultRemoteMode;
  const showModeButtons = hostStatus?.showModeButtons ?? true;
  const controlDepth = hostStatus?.controlDepth ?? true;
  const pcSettings = usePcSettings(clientId, activePc?.id ?? null, hostDefaultRemoteMode, hostPointerSpeed);
  const {
    appSettings,
    effectiveTrackpadSettings,
    keyboardSettings,
    remoteSettings,
    trackpadSettings
  } = pcSettings;
  const presentationAvailable = presentationCapability !== undefined;
  const filesAvailable = fileManagerCapability !== undefined;
  const canMirrorFileView = screenViewCapability?.canView === true && screenViewCapability.requiresRepair === false;
  const mirrorFileViewUnavailableMessage = !screenViewCapability
    ? "View requires PC Screen, which is unavailable on this host."
    : screenViewCapability.requiresRepair
      ? "View requires a trusted PC Screen connection. Scan this PC's pairing QR again."
      : !screenViewCapability.enabled
        ? "View requires PC Screen, which is unavailable on this PC."
        : !screenViewCapability.permissionGranted
          ? "Allow this device to view the PC screen in PC permissions before using View."
          : "PC Screen is not currently available.";
  const {
    dismiss: dismissModeSwitchHint,
    open: isModeSwitchHintOpen,
    showOnce: showModeSwitchHintOnce
  } = useOneShotHint({ autoHideMs: 4000 });
  const headerCompactModeButtonRef = useRef<HTMLButtonElement | null>(null);
  const trackpadCompactModeButtonRef = useRef<HTMLButtonElement | null>(null);
  const previousTabRef = useRef<string | null>(null);
  const pendingPresentationExitRef = useRef<(() => void) | null>(null);
  const pendingPresentationConnectionRef = useRef<(() => void) | null>(null);
  const [presentationSessionActive, setPresentationSessionActive] = useState(false);
  const [presentationActivationRequest, setPresentationActivationRequest] = useState({
    connectionEpoch,
    id: 0
  });
  const [isPresentationExitOpen, setIsPresentationExitOpen] = useState(false);
  const [presentationConnectionIntent, setPresentationConnectionIntent] = useState<"connect" | "disconnect" | null>(null);
  const handlePresentationSessionActiveChange = useCallback((active: boolean) => {
    setPresentationSessionActive(active);
  }, []);
  const handlePresentationActivationRequestHandled = useCallback(() => {
    setPresentationActivationRequest({ connectionEpoch, id: 0 });
  }, [connectionEpoch]);
  const openScreenViewFromFiles = useCallback(() => {
    setActiveCustomScreenId(null);
    setIsThirdPartyNoticesOpen(false);
    setIsScreenViewOpen(true);
  }, []);
  const requestPresentationActivation = () => {
    setPresentationActivationRequest((current) => ({
      connectionEpoch,
      id: current.connectionEpoch === connectionEpoch ? current.id + 1 : 1
    }));
  };

  const launchRemoteAction = (action: RemoteLaunchAction) => {
    if (supportsRemoteLaunch && state === "paired") {
      send({ type: "remote.launch", action });
    }
  };

  const requestRemoteModeLaunch = (mode: unknown, settings: RemoteSettings) => {
    if (!supportsRemoteLaunch) {
      return;
    }

    if (mode === "youtube" && settings.openYoutube) {
      setPendingRemoteLaunch("openYoutube");
      return;
    }

    if (mode === "kodi" && settings.startKodi) {
      setPendingRemoteLaunch("startOrActivateKodi");
    }
  };

  const {
    activeModeTab,
    canShowModeNavigation,
    closeModeSelector,
    closeTransientSurfaces,
    isBottomModeNavigationVisible,
    isModeButtonsVisible,
    isModeSelectorOpen,
    isSettingsOpen,
    modeSelectorAnchor,
    modeTabs,
    openGestureDebug,
    openSettings,
    openModeFromMenu,
    selectModeTab,
    setIsRemoteUtilityPanelOpen,
    setIsSettingsOpen,
    shellClassName,
    shouldShowSplitMode,
    showTrackpadCompactModeSelector,
    tab,
    toggleModeSelector
  } = useAppNavigation({
    fourthMode: appSettings.fourthMode,
    isPaired: state === "paired",
    onActiveModeTabCollapse: showModeSwitchHintOnce,
    onEnterRemote: () => { requestRemoteModeLaunch(remoteSettings.mode, remoteSettings); },
    presentationAvailable: presentationAvailable || presentationSessionActive,
    filesAvailable,
    supportsGestureDebug,
    trackpadSettings,
    suppressSplitMode: gyroSelected,
    showModeButtons: showModeButtons && !isThirdPartyNoticesOpen
  });
  useEffect(() => {
    if (state === "paired" &&
        tab !== "presentation" &&
        activeCustomScreenId === null &&
        presentationCapability?.laserPointerActive === true) {
      requestPresentationCommand("powerpoint", "pointer", false);
    }
  }, [activeCustomScreenId, presentationCapability?.laserPointerActive, requestPresentationCommand, state, tab]);
  const requestPresentationExit = (action: () => void) => {
    if (tab === "presentation" && presentationSessionActive) {
      pendingPresentationExitRef.current = action;
      closeTransientSurfaces();
      setIsPresentationExitOpen(true);
      return;
    }

    action();
  };
  const selectModeTabWithPresentationGuard: typeof selectModeTab = (nextTab, source) => {
    const selectMode = () => {
      setActiveCustomScreenId(null);
      setIsScreenViewOpen(false);
      setIsThirdPartyNoticesOpen(false);
      if (nextTab === "presentation") {
        requestPresentationActivation();
      }
      selectModeTab(nextTab, source);
    };
    if (nextTab === tab) {
      selectMode();
      return;
    }

    requestPresentationExit(selectMode);
  };
  const openModeFromMenuWithPresentationGuard: typeof openModeFromMenu = (mode) => {
    const openMode = () => {
      setActiveCustomScreenId(null);
      setIsScreenViewOpen(false);
      setIsThirdPartyNoticesOpen(false);
      if (mode === "presentation") {
        requestPresentationActivation();
      }
      openModeFromMenu(mode);
    };
    if (mode === tab) {
      openMode();
      return;
    }

    requestPresentationExit(openMode);
  };
  const openGestureDebugWithPresentationGuard = () => {
    requestPresentationExit(() => {
      setActiveCustomScreenId(null);
      setIsScreenViewOpen(false);
      setIsThirdPartyNoticesOpen(false);
      openGestureDebug();
    });
  };
  const openGyroMouse = () => {
    const permission = requestGyroPermission();
    const request = { id: ++gyroActivationIdRef.current, permission };
    requestPresentationExit(() => {
      setActiveCustomScreenId(null);
      setIsScreenViewOpen(false);
      setIsThirdPartyNoticesOpen(false);
      setGyroActivationRequest(request);
      openModeFromMenu("trackpad");
    });
  };
  const requestPresentationConnectionChange = (
    intent: "connect" | "disconnect",
    action: () => void
  ) => {
    if (!presentationSessionActive) {
      action();
      return;
    }

    pendingPresentationConnectionRef.current = action;
    closeTransientSurfaces();
    setPresentationConnectionIntent(intent);
  };
  const {
    forgetPcAndSettings,
    updateAppSetting,
    updateKeyboardSetting,
    updateRemoteSetting: persistRemoteSetting,
    updateTrackpadSetting
  } = createSettingsActions({
    clientId,
    effectiveTrackpadSettings,
    forgetPc,
    setHostPointerSpeed,
    settingsState: pcSettings
  });
  const updateRemoteSetting = <Key extends keyof RemoteSettings>(
    key: Key,
    value: RemoteSettings[Key]
  ) => {
    const nextSettings = { ...remoteSettings, [key]: value };
    if (key === "mode") {
      requestPresentationExit(() => {
        setActiveCustomScreenId(null);
        setIsScreenViewOpen(false);
        setIsThirdPartyNoticesOpen(false);
        selectModeTab("remote", "settings");
        setIsSettingsOpen(false);
        requestRemoteModeLaunch(value, nextSettings);
      });
    }

    persistRemoteSetting(key, value);
  };
  const { installApp, installPrompt, isInstalled, refreshInstalledApp, refreshMessage } = usePwaLifecycle({
    activePc,
    autoRefresh: appSettings.autoRefresh,
    clientId,
    hostStatus,
    state
  });
  const {
    confirmPendingPairing,
    connectManualHost,
    isPairingQrReading,
    onPairingQrSelected,
    pairingDeviceName,
    pairingDeviceNamePlaceholder,
    pairingQrInputRef,
    pairingScanMessage,
    pairingStatusMessage,
    pendingPairing,
    scanPairingQr,
    setPairingDeviceName
  } = usePairingController({
    beginNewPairing: () => { requestPresentationConnectionChange("connect", beginNewPairing); },
    connectManualPc: (target) => {
      requestPresentationConnectionChange("connect", () => { connectManualPc(target); });
    },
    deviceName,
    initialPairing,
    message,
    pairWithToken: (token, pcUrl, requestedDeviceName) => {
      requestPresentationConnectionChange("connect", () => {
        pairWithToken(token, pcUrl, requestedDeviceName);
      });
    },
    setIsSettingsOpen
  });

  const mobileDiagnostics = useMemo(() => buildMobileDiagnostics({
    activePc,
    connectionState: state,
    lastErrorCode: lastConnectionError?.code ?? null,
    lastErrorMessage: lastConnectionError?.message ?? null,
    message,
    pairedPcCount: pairedPcs.length,
    hostStatus
  }), [activePc, hostStatus, lastConnectionError?.code, lastConnectionError?.message, message, pairedPcs.length, state]);

  const connectionStatusMessage = lastConnectionError ? "Connection issue" : message;
  const connectionPcName = lastConnectionError
    ? connectionStatusMessage
    : state === "paired" && activePc
      ? getPcDisplayName(activePc)
      : message;
  const modeSwitchHintAnchorRef = showTrackpadCompactModeSelector ? trackpadCompactModeButtonRef : headerCompactModeButtonRef;

  const activeCustomScreenSummary = customScreensCapability?.screens.find(
    (screen) => screen.id === activeCustomScreenId) ?? null;
  const activeCustomScreenRevision = activeCustomScreenSummary?.revision;
  const customScreensCatalogRevision = customScreensCapability?.catalogRevision;
  const staleCustomScreenOperationId = customScreenInvokeResult?.code === "stale-screen"
    ? customScreenInvokeResult.operationId
    : null;

  useEffect(() => {
    if (activeCustomScreenId === null || activeCustomScreenRevision === undefined || state !== "paired") {
      return;
    }

    requestCustomScreen(activeCustomScreenId);
  }, [
    activeCustomScreenId,
    activeCustomScreenRevision,
    customScreensCatalogRevision,
    requestCustomScreen,
    staleCustomScreenOperationId,
    state
  ]);

  useEffect(() => {
    const tabChanged = previousTabRef.current !== null && previousTabRef.current !== tab;
    previousTabRef.current = tab;
    if (isModeSwitchHintOpen && (tabChanged || isModeSelectorOpen || isSettingsOpen || !canShowModeNavigation || !activeModeTab)) {
      dismissModeSwitchHint();
    }
  }, [activeModeTab, canShowModeNavigation, dismissModeSwitchHint, isModeSelectorOpen, isModeSwitchHintOpen, isSettingsOpen, tab]);

  useEffect(() => {
    if (!transientFeedback) {
      return;
    }

    const timeout = window.setTimeout(() => { setTransientFeedback(null); }, transientFeedback.tone === "error" ? 8000 : 4000);
    return () => { window.clearTimeout(timeout); };
  }, [transientFeedback]);

  useEffect(() => {
    if (!presentationSessionActive) {
      return;
    }

    const warnBeforeUnload = (event: BeforeUnloadEvent) => {
      event.preventDefault();
    };
    window.addEventListener("beforeunload", warnBeforeUnload);
    return () => { window.removeEventListener("beforeunload", warnBeforeUnload); };
  }, [presentationSessionActive]);

  const stayInPresentation = () => {
    pendingPresentationExitRef.current = null;
    setPendingRemoteLaunch(null);
    setIsPresentationExitOpen(false);
    closeTransientSurfaces();
  };

  const leavePresentation = () => {
    const pendingExit = pendingPresentationExitRef.current;
    pendingPresentationExitRef.current = null;
    setIsPresentationExitOpen(false);
    pendingExit?.();
  };

  const keepPresentationConnection = () => {
    pendingPresentationConnectionRef.current = null;
    setPresentationConnectionIntent(null);
    closeTransientSurfaces();
  };

  const confirmPresentationConnectionChange = () => {
    const pendingChange = pendingPresentationConnectionRef.current;
    pendingPresentationConnectionRef.current = null;
    setPresentationConnectionIntent(null);
    pendingChange?.();
  };

  const showClipboardCopyFeedback = (feedback: AppToastMessage) => {
    setSuppressedClipboardResultId(clipboardReadResult?.operationId ?? null);
    setTransientFeedback(feedback);
  };

  const tryReconnectPc = (pcId: string) => {
    dismissModeSwitchHint();
    closeTransientSurfaces();
    requestPresentationConnectionChange("connect", () => { reconnectPc(pcId); });
  };

  const tryManualReconnect = () => {
    if (activePc) {
      tryReconnectPc(activePc.id);
    }
  };

  return (
    <div className={`app-frame${controlDepth ? " control-depth" : ""}`}>
      <main className={`${shellClassName}${controlDepth ? " control-depth" : ""}${isScreenViewOpen ? " screen-view-active" : ""}${tab === "files" ? " files-active" : ""}`}>
        <AppHeader
          activeMode={activeModeTab}
          canShowModeNavigation={canShowModeNavigation}
          compactModeButtonRef={headerCompactModeButtonRef}
          connectionPcName={connectionPcName}
          developerMode={developerMode}
          isModeSelectorOpen={isModeSelectorOpen && modeSelectorAnchor === "header"}
          message={connectionStatusMessage}
          fileJobCount={activeFileJobCount}
          hasConnectionError={lastConnectionError !== null}
          modeTabs={modeTabs}
          onCloseModeSelector={closeModeSelector}
          onOpenSettings={() => {
            dismissModeSwitchHint();
            openSettings();
          }}
          {...(filesAvailable ? { onOpenFileJobs: () => {
            setActiveCustomScreenId(null);
            setIsScreenViewOpen(false);
            selectModeTabWithPresentationGuard("files", "selector");
          } } : {})}
          onSelectMode={(nextTab) => {
            dismissModeSwitchHint();
            setIsScreenViewOpen(false);
            selectModeTabWithPresentationGuard(nextTab, "selector");
          }}
          onToggleModeSelector={() => {
            dismissModeSwitchHint();
            toggleModeSelector("header");
          }}
          refreshInstalledApp={refreshInstalledApp}
          state={state}
          tab={tab}
        />

        <PairingGate
          activePc={activePc}
          connectManualHost={connectManualHost}
          confirmPendingPairing={confirmPendingPairing}
          diagnostics={mobileDiagnostics}
          isSettingsOpen={isSettingsOpen}
          isPairingQrReading={isPairingQrReading}
          manualReconnectProgress={manualReconnectProgress}
          message={message}
          pairingDeviceName={pairingDeviceName}
          pairingDeviceNamePlaceholder={pairingDeviceNamePlaceholder}
          pairingStatusMessage={pairingStatusMessage}
          pendingPairing={pendingPairing !== null}
          reconnectablePcs={reconnectablePcs}
          scanPairingQr={scanPairingQr}
          setPairingDeviceName={setPairingDeviceName}
          state={state}
          tryManualReconnect={tryManualReconnect}
          tryReconnectPc={tryReconnectPc}
        />

        <ErrorDialog
          code={lastConnectionError?.code}
          isOpen={state === "paired" && lastConnectionError !== null && !connectionErrorDialog.dismissed}
          message={lastConnectionError?.message ?? ""}
          onClose={() => {
            setConnectionErrorDialog((current) => ({ ...current, dismissed: true }));
          }}
          title="Connection issue"
        />

        <SettingsDrawer
          activePc={activePc}
          appSettings={appSettings}
          customPointerEnabled={hostStatus?.customPointerEnabled}
          customScreens={customScreensCapability?.screens ?? []}
          diagnostics={mobileDiagnostics}
          deviceName={deviceName}
          disconnectActivePc={() => {
            requestPresentationConnectionChange("disconnect", disconnectActivePc);
          }}
          forgetPc={(pcId) => {
            requestPresentationConnectionChange("disconnect", () => { forgetPcAndSettings(pcId); });
          }}
          installApp={installApp}
          installPrompt={installPrompt}
          isInstalled={isInstalled}
          isPairingQrReading={isPairingQrReading}
          isOpen={isSettingsOpen}
          keyboardSettings={keyboardSettings}
          onClose={() => { setIsSettingsOpen(false); }}
          onOpenGestureDebug={supportsGestureDebug ? openGestureDebugWithPresentationGuard : undefined}
          onOpenCustomScreen={(screenId) => {
            requestPresentationExit(() => {
              setActiveCustomScreenId(screenId);
              setIsScreenViewOpen(false);
              setIsThirdPartyNoticesOpen(false);
              setIsSettingsOpen(false);
            });
          }}
          onOpenScreenView={() => {
            requestPresentationExit(() => {
              setActiveCustomScreenId(null);
              setIsThirdPartyNoticesOpen(false);
              setIsScreenViewOpen(true);
              setIsSettingsOpen(false);
            });
          }}
          onOpenMode={(mode) => {
            dismissModeSwitchHint();
            openModeFromMenuWithPresentationGuard(mode);
          }}
          onOpenThirdPartyNotices={() => {
            closeTransientSurfaces();
            setActiveCustomScreenId(null);
            setIsScreenViewOpen(false);
            setIsThirdPartyNoticesOpen(true);
          }}
          onOpenGyroMouse={openGyroMouse}
          onPairingQrSelected={onPairingQrSelected}
          onManualHostSubmit={connectManualHost}
          pairedPcs={pairedPcs}
          pairingQrInputRef={pairingQrInputRef}
          pairingScanMessage={pairingScanMessage}
          presentationAvailable={presentationAvailable}
          filesAvailable={filesAvailable}
          refreshInstalledApp={refreshInstalledApp}
          refreshMessage={refreshMessage}
          renameDevice={renameDevice}
          renamePc={renamePc}
          remoteSettings={remoteSettings}
          scanPairingQr={scanPairingQr}
          screenViewCapability={screenViewCapability}
          selectPc={(pcId) => {
            requestPresentationConnectionChange("connect", () => { selectPc(pcId); });
          }}
          setHostCustomPointer={setHostCustomPointer}
          setHostControlDepth={setHostControlDepth}
          setHostShowModeButtons={setHostShowModeButtons}
          setThemeMode={setThemeMode}
          showGestureDebug={supportsGestureDebug}
          supportsRemoteLaunch={supportsRemoteLaunch}
          themeMode={themeMode}
          controlDepth={controlDepth}
          showModeButtons={showModeButtons}
          toolOptions={[
            ...modeTabs,
            ...getAvailableToolModeIds(presentationAvailable, filesAvailable)
              .filter((id) => !modeTabs.some((mode) => mode.id === id))
              .map((id) => toolModeDefinitions[id])
          ].map(({ id, label, ariaLabel, Icon }) => ({ id, label: id === "trackpad" || id === "keyboard" || id === "remote" ? label : ariaLabel, Icon }))}
          trackpadSettings={effectiveTrackpadSettings}
          updateKeyboardSetting={updateKeyboardSetting}
          updateRemoteSetting={updateRemoteSetting}
          updateAppSetting={updateAppSetting}
          updateTrackpadSetting={updateTrackpadSetting}
        />

        {activeCustomScreenId === null && !isScreenViewOpen && tab !== "files" && isModeButtonsVisible && <ModeNavigation className="tabs top-mode-tabs" modeTabs={modeTabs} tab={tab} onSelect={selectModeTabWithPresentationGuard} />}

        {isThirdPartyNoticesOpen ? (
          <ThirdPartyNoticesWorkspace onBack={() => { setIsThirdPartyNoticesOpen(false); }} />
        ) : isScreenViewOpen && activePc && screenViewCapability ? (
          <WorkspaceErrorBoundary featureName="Screen" onBack={() => { setIsScreenViewOpen(false); }}>
            <Suspense fallback={<div className="workspace-loading">Opening Screen…</div>}>
              <ScreenViewWorkspace
                activePc={activePc}
                capability={screenViewCapability}
                clientId={clientId}
                onBack={() => { setIsScreenViewOpen(false); }}
                onOpenKeyboard={() => { setIsScreenViewOpen(false); selectModeTabWithPresentationGuard("keyboard", "selector"); }}
                send={send}
                state={state}
                trackpadSettings={effectiveTrackpadSettings}
              />
            </Suspense>
          </WorkspaceErrorBoundary>
        ) : activeCustomScreenId !== null ? (
          <CustomScreenWorkspace
            audioState={connection.audioState}
            definition={customScreenDefinition?.id === activeCustomScreenId ? customScreenDefinition : null}
            error={customScreenGetResult?.succeeded === false ? customScreenGetResult.message ?? "The custom screen could not be loaded." : null}
            invoke={invokeCustomScreenButton}
            onBack={() => { setActiveCustomScreenId(null); }}
            pendingButtonIds={pendingCustomScreenButtonIds}
            presentationCapability={presentationCapability}
            requestedName={activeCustomScreenSummary?.name ?? "Custom screen"}
            send={send}
            state={state}
            trackpadSettings={effectiveTrackpadSettings}
          />
        ) : tab === "files" && fileManagerCapability ? (
          <WorkspaceErrorBoundary featureName="Files" onBack={() => { selectModeTabWithPresentationGuard("trackpad", "selector"); }}>
            <Suspense fallback={<div className="workspace-loading">Opening Files…</div>}>
              <FileManagerWorkspace
                key={`${connectionEpoch}-${String(fileManagerCapability.canBrowse)}-${String(fileManagerCapability.canModify)}-${String(fileManagerCapability.hidesProtectedSystemItems)}`}
                capability={fileManagerCapability}
                canMirrorView={canMirrorFileView}
                connectionEpoch={connectionEpoch}
                mirrorViewUnavailableMessage={mirrorFileViewUnavailableMessage}
                onMirrorView={openScreenViewFromFiles}
                send={send}
                state={state}
              />
            </Suspense>
          </WorkspaceErrorBoundary>
        ) : <ModeWorkspace
          appSettings={appSettings}
          connection={connection}
          connectionEpoch={connectionEpoch}
          gyroActivationRequest={gyroActivationRequest}
          keyboardSettings={keyboardSettings}
          onClearAfterSendingChange={(value) => { updateAppSetting("clearTextAfterSending", value); }}
          onClipboardCopyFeedback={showClipboardCopyFeedback}
          onPresentationSessionActiveChange={handlePresentationSessionActiveChange}
          onGyroSelectedChange={handleGyroSelectedChange}
          onPresentationActivationRequestHandled={handlePresentationActivationRequestHandled}
          presentationActivationRequestId={
            state === "paired" &&
            tab === "presentation" &&
            presentationActivationRequest.connectionEpoch === connectionEpoch
              ? presentationActivationRequest.id
              : 0
          }
          onRemoteUtilityPanelOpenChange={setIsRemoteUtilityPanelOpen}
          remoteSettings={remoteSettings}
          shouldShowSplitMode={shouldShowSplitMode}
          showTrackpadCompactModeSelector={showTrackpadCompactModeSelector}
          trackpadCompactModeSelector={showTrackpadCompactModeSelector && activeModeTab ? (
            <>
              <CompactModeSelectorButton
                buttonRef={trackpadCompactModeButtonRef}
                activeMode={activeModeTab}
                isOpen={isModeSelectorOpen && modeSelectorAnchor === "trackpad"}
                onToggle={() => {
                  dismissModeSwitchHint();
                  toggleModeSelector("trackpad");
                }}
              />
              {isModeSelectorOpen && modeSelectorAnchor === "trackpad" && (
                <ModeSelector
                  modeTabs={modeTabs}
                  tab={tab}
                  onClose={closeModeSelector}
                  onSelect={(nextTab) => {
                    dismissModeSwitchHint();
                    selectModeTabWithPresentationGuard(nextTab, "selector");
                  }}
                />
              )}
            </>
          ) : undefined}
          showVolumeControl={trackpadSettings.showVolumeControl}
          tab={tab}
          trackpadSettings={effectiveTrackpadSettings}
        />}

        <AnchoredHint
          anchorRef={modeSwitchHintAnchorRef}
          open={isModeSwitchHintOpen}
          preferredPlacement={showTrackpadCompactModeSelector ? "below-start" : "below-end"}
        >
          Switch modes from here.
        </AnchoredHint>

        {inputBlockedByElevation && (
          <InputRecoveryNotice
            dismissed={isInputRecoveryDialogDismissed}
            onDismiss={() => { setInputRecoveryDialog((current) => ({ ...current, dismissed: true })); }}
            onOpen={() => { setInputRecoveryDialog((current) => ({ ...current, dismissed: false })); }}
            onShowDesktop={() => { send({ type: "keyboard.special", key: "D", modifiers: ["Win"] }); }}
          />
        )}

        <GlobalOperationFeedback
          appLaunchResult={appLaunchResult}
          clipboardReadResult={clipboardReadResult?.operationId === suppressedClipboardResultId ? null : clipboardReadResult}
          pendingAppLaunchId={pendingAppLaunchId}
          pendingClipboardRead={pendingClipboardRead}
          pendingTextTransfer={pendingTextTransfer}
          powerPointRefreshResult={connection.powerPointRefreshResult}
          presentationResult={connection.presentationResult}
          presentationSessionResult={connection.presentationSessionResult}
          tab={tab}
          textTransferResult={textTransferResult}
          transientFeedback={transientFeedback}
          onDismissTransient={() => setTransientFeedback(null)}
        />
        <ConfirmationDialog
          confirmLabel={`Open ${pendingRemoteLaunch === "openYoutube" ? "YouTube" : "Kodi"}`}
          destructive={false}
          description={`This will open ${pendingRemoteLaunch === "openYoutube" ? "YouTube" : "Kodi"} on the PC.`}
          isOpen={pendingRemoteLaunch !== null}
          onCancel={() => { setPendingRemoteLaunch(null); }}
          onConfirm={() => {
            const action = pendingRemoteLaunch;
            setPendingRemoteLaunch(null);
            if (action) {
              launchRemoteAction(action);
            }
          }}
          title={`Open ${pendingRemoteLaunch === "openYoutube" ? "YouTube" : "Kodi"}?`}
        />
        <ConfirmationDialog
          cancelLabel="Stay in presentation"
          confirmLabel="Leave and discard"
          description="Presentation timing is active. Leaving will discard the timer, slide timings, sessions, and breaks."
          isOpen={isPresentationExitOpen}
          onCancel={stayInPresentation}
          onConfirm={leavePresentation}
          title="Leave presentation?"
        />
        <ConfirmationDialog
          cancelLabel={presentationConnectionIntent === "disconnect" ? "Stay connected" : "Keep current connection"}
          confirmLabel={presentationConnectionIntent === "disconnect" ? "Disconnect" : "Change PC"}
          destructive={false}
          description={presentationConnectionIntent === "disconnect"
            ? "Presentation timing is active. Disconnecting will interrupt presentation controls and saving until this PC reconnects."
            : "Presentation timing is active. Changing the PC will interrupt its controls and can prevent this session from being saved to the original PC."}
          initialFocus="cancel"
          isOpen={presentationConnectionIntent !== null}
          onCancel={keepPresentationConnection}
          onConfirm={confirmPresentationConnectionChange}
          title={presentationConnectionIntent === "disconnect"
            ? "Disconnect during presentation?"
            : "Change PC during presentation?"}
        />
      </main>

      {activeCustomScreenId === null && !isScreenViewOpen && tab !== "files" && isBottomModeNavigationVisible && <ModeNavigation className="tabs bottom-mode-tabs" modeTabs={modeTabs} tab={tab} onSelect={selectModeTabWithPresentationGuard} />}
    </div>
  );
}
