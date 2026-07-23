import { useCallback, useEffect, useMemo, useRef, useState } from "react";
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

export function App() {
  const initialPairing = useMemo(() => parsePairingLink(window.location.href), []);
  const connection = useVolturaAirConnection();
  const {
    state,
    message,
    send,
    pendingTextTransfer,
    presentationCapability,
    pendingClipboardRead,
    textTransferResult,
    clipboardReadResult,
    pendingAppLaunchId,
    appLaunchResult,
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
    setHostShowModeButtons,
    setHostPointerSpeed
  } = connection;
  const { setThemeMode, themeMode } = useAppTheme();
  const [transientFeedback, setTransientFeedback] = useState<AppToastMessage | null>(null);
  const [pendingRemoteLaunch, setPendingRemoteLaunch] = useState<RemoteLaunchAction | null>(null);
  const [suppressedClipboardResultId, setSuppressedClipboardResultId] = useState<string | null>(null);
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
  const pcSettings = usePcSettings(clientId, activePc?.id ?? null, hostDefaultRemoteMode, hostPointerSpeed);
  const {
    appSettings,
    effectiveTrackpadSettings,
    keyboardSettings,
    remoteSettings,
    trackpadSettings
  } = pcSettings;
  const presentationAvailable = presentationCapability !== undefined;
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
  const [isPresentationExitOpen, setIsPresentationExitOpen] = useState(false);
  const [presentationConnectionIntent, setPresentationConnectionIntent] = useState<"connect" | "disconnect" | null>(null);
  const handlePresentationSessionActiveChange = useCallback((active: boolean) => {
    setPresentationSessionActive(active);
  }, []);

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
    supportsGestureDebug,
    trackpadSettings,
    showModeButtons
  });
  useEffect(() => {
    if (state === "paired" &&
        tab !== "presentation" &&
        presentationCapability?.laserPointerActive === true) {
      requestPresentationCommand("powerpoint", "pointer", false);
    }
  }, [presentationCapability?.laserPointerActive, requestPresentationCommand, state, tab]);

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
    if (nextTab === tab) {
      selectModeTab(nextTab, source);
      return;
    }

    requestPresentationExit(() => { selectModeTab(nextTab, source); });
  };
  const openModeFromMenuWithPresentationGuard: typeof openModeFromMenu = (mode) => {
    if (mode === tab) {
      openModeFromMenu(mode);
      return;
    }

    requestPresentationExit(() => { openModeFromMenu(mode); });
  };
  const openGestureDebugWithPresentationGuard = () => {
    requestPresentationExit(openGestureDebug);
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
    if (key === "mode" &&
        value !== remoteSettings.mode &&
        (value === "youtube" || value === "kodi")) {
      requestPresentationExit(() => {
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

  const connectionPcName = state === "paired" && activePc ? getPcDisplayName(activePc) : message;
  const modeSwitchHintAnchorRef = showTrackpadCompactModeSelector ? trackpadCompactModeButtonRef : headerCompactModeButtonRef;

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

    const timeout = window.setTimeout(() => { setTransientFeedback(null); }, 3000);
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
    <div className="app-frame">
      <main className={shellClassName}>
        <AppHeader
          activeMode={activeModeTab}
          canShowModeNavigation={canShowModeNavigation}
          compactModeButtonRef={headerCompactModeButtonRef}
          connectionPcName={connectionPcName}
          developerMode={developerMode}
          isModeSelectorOpen={isModeSelectorOpen && modeSelectorAnchor === "header"}
          message={message}
          modeTabs={modeTabs}
          onCloseModeSelector={closeModeSelector}
          onOpenSettings={() => {
            dismissModeSwitchHint();
            openSettings();
          }}
          onSelectMode={(nextTab) => {
            dismissModeSwitchHint();
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

        <SettingsDrawer
          activePc={activePc}
          appSettings={appSettings}
          customPointerEnabled={hostStatus?.customPointerEnabled}
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
          isOpen={isSettingsOpen}
          keyboardSettings={keyboardSettings}
          onClose={() => { setIsSettingsOpen(false); }}
          onOpenGestureDebug={supportsGestureDebug ? openGestureDebugWithPresentationGuard : undefined}
          onOpenMode={(mode) => {
            dismissModeSwitchHint();
            openModeFromMenuWithPresentationGuard(mode);
          }}
          onPairingQrSelected={onPairingQrSelected}
          onManualHostSubmit={connectManualHost}
          pairedPcs={pairedPcs}
          pairingQrInputRef={pairingQrInputRef}
          pairingScanMessage={pairingScanMessage}
          presentationAvailable={presentationAvailable}
          refreshInstalledApp={refreshInstalledApp}
          refreshMessage={refreshMessage}
          renameDevice={renameDevice}
          renamePc={renamePc}
          remoteSettings={remoteSettings}
          scanPairingQr={scanPairingQr}
          selectPc={(pcId) => {
            requestPresentationConnectionChange("connect", () => { selectPc(pcId); });
          }}
          setHostCustomPointer={setHostCustomPointer}
          setHostShowModeButtons={setHostShowModeButtons}
          setThemeMode={setThemeMode}
          showGestureDebug={supportsGestureDebug}
          supportsRemoteLaunch={supportsRemoteLaunch}
          themeMode={themeMode}
          showModeButtons={showModeButtons}
          toolOptions={[
            ...modeTabs,
            ...getAvailableToolModeIds(presentationAvailable)
              .filter((id) => !modeTabs.some((mode) => mode.id === id))
              .map((id) => toolModeDefinitions[id])
          ].map(({ id, label, ariaLabel, Icon }) => ({ id, label: id === "trackpad" || id === "keyboard" || id === "remote" ? label : ariaLabel, Icon }))}
          trackpadSettings={effectiveTrackpadSettings}
          updateKeyboardSetting={updateKeyboardSetting}
          updateRemoteSetting={updateRemoteSetting}
          updateAppSetting={updateAppSetting}
          updateTrackpadSetting={updateTrackpadSetting}
        />

        {isModeButtonsVisible && <ModeNavigation className="tabs top-mode-tabs" modeTabs={modeTabs} tab={tab} onSelect={selectModeTabWithPresentationGuard} />}

        <ModeWorkspace
          appSettings={appSettings}
          connection={connection}
          keyboardSettings={keyboardSettings}
          onClearAfterSendingChange={(value) => { updateAppSetting("clearTextAfterSending", value); }}
          onClipboardCopyFeedback={showClipboardCopyFeedback}
          onPresentationSessionActiveChange={handlePresentationSessionActiveChange}
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
        />

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
          tab={tab}
          textTransferResult={textTransferResult}
          transientFeedback={transientFeedback}
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

      {isBottomModeNavigationVisible && <ModeNavigation className="tabs bottom-mode-tabs" modeTabs={modeTabs} tab={tab} onSelect={selectModeTabWithPresentationGuard} />}
    </div>
  );
}
