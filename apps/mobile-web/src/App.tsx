import { useEffect, useMemo, useRef, useState } from "react";
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
    presentationAvailable,
    supportsGestureDebug,
    trackpadSettings,
    showModeButtons
  });
  const {
    forgetPcAndSettings,
    updateAppSetting,
    updateKeyboardSetting,
    updateRemoteSetting,
    updateTrackpadSetting
  } = createSettingsActions({
    clientId,
    effectiveTrackpadSettings,
    forgetPc,
    onSelectRemoteMode: (mode, nextSettings) => {
      selectModeTab("remote", "settings");
      setIsSettingsOpen(false);
      requestRemoteModeLaunch(mode, nextSettings);
    },
    remoteSettings,
    setHostPointerSpeed,
    settingsState: pcSettings
  });
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
    pairingQrInputRef,
    pairingScanMessage,
    pairingStatusMessage,
    pendingPairing,
    scanPairingQr,
    setPairingDeviceName
  } = usePairingController({ beginNewPairing, connectManualPc, deviceName, initialPairing, message, pairWithToken, setIsSettingsOpen });

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

  const showClipboardCopyFeedback = (feedback: AppToastMessage) => {
    setSuppressedClipboardResultId(clipboardReadResult?.operationId ?? null);
    setTransientFeedback(feedback);
  };

  const tryReconnectPc = (pcId: string) => {
    dismissModeSwitchHint();
    closeTransientSurfaces();
    reconnectPc(pcId);
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
            selectModeTab(nextTab, "selector");
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
          disconnectActivePc={disconnectActivePc}
          forgetPc={forgetPcAndSettings}
          installApp={installApp}
          installPrompt={installPrompt}
          isInstalled={isInstalled}
          isOpen={isSettingsOpen}
          keyboardSettings={keyboardSettings}
          onClose={() => { setIsSettingsOpen(false); }}
          onOpenGestureDebug={supportsGestureDebug ? openGestureDebug : undefined}
          onOpenMode={(mode) => {
            dismissModeSwitchHint();
            openModeFromMenu(mode);
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
          selectPc={selectPc}
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

        {isModeButtonsVisible && <ModeNavigation className="tabs top-mode-tabs" modeTabs={modeTabs} tab={tab} onSelect={selectModeTab} />}

        <ModeWorkspace
          appSettings={appSettings}
          connection={connection}
          keyboardSettings={keyboardSettings}
          onClearAfterSendingChange={(value) => { updateAppSetting("clearTextAfterSending", value); }}
          onClipboardCopyFeedback={showClipboardCopyFeedback}
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
                    selectModeTab(nextTab, "selector");
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
      </main>

      {isBottomModeNavigationVisible && <ModeNavigation className="tabs bottom-mode-tabs" modeTabs={modeTabs} tab={tab} onSelect={selectModeTab} />}
    </div>
  );
}
