import { useMemo, useState } from "react";
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
import { ModeNavigation } from "./app/ModeNavigation";
import { useAppTheme } from "./app/useAppTheme";
import { useAppNavigation } from "./app/useAppNavigation";
import { InputRecoveryNotice } from "./features/input-recovery";
import { ModeWorkspace } from "./features/modes";

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
    setHostPointerSpeed
  } = connection;
  const { setThemeMode, themeMode } = useAppTheme();
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
  const pcSettings = usePcSettings(clientId, activePc?.id ?? null, hostDefaultRemoteMode, hostPointerSpeed);
  const {
    appSettings,
    effectiveTrackpadSettings,
    keyboardSettings,
    remoteSettings,
    trackpadSettings
  } = pcSettings;
  const presentationAvailable = presentationCapability !== undefined;

  const launchRemoteAction = (action: RemoteLaunchAction) => {
    if (supportsRemoteLaunch && state === "paired") {
      send({ type: "remote.launch", action });
    }
  };

  const maybeLaunchRemoteMode = (mode: unknown, settings: RemoteSettings) => {
    if (!supportsRemoteLaunch) {
      return;
    }

    if (mode === "youtube" && settings.openYoutube) {
      launchRemoteAction("openYoutube");
      return;
    }

    if (mode === "kodi" && settings.startKodi) {
      launchRemoteAction("startOrActivateKodi");
    }
  };

  const {
    activeModeTab,
    canShowModeNavigation,
    closeModeSelector,
    closeTransientSurfaces,
    isBottomModeNavigationVisible,
    isModeSelectorOpen,
    isSettingsOpen,
    modeTabs,
    openGestureDebug,
    openSettings,
    openToolFromMenu,
    selectModeTab,
    setIsRemoteUtilityPanelOpen,
    setIsSettingsOpen,
    shellClassName,
    shouldShowSplitMode,
    tab,
    toggleModeSelector
  } = useAppNavigation({
    fourthMode: appSettings.fourthMode,
    isPaired: state === "paired",
    onEnterRemote: () => { maybeLaunchRemoteMode(remoteSettings.mode, remoteSettings); },
    presentationAvailable,
    supportsGestureDebug,
    trackpadSettings
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
    onLaunchRemoteMode: maybeLaunchRemoteMode,
    onOpenRemote: () => {
      selectModeTab("remote");
      setIsSettingsOpen(false);
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

  const tryReconnectPc = (pcId: string) => {
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
          connectionPcName={connectionPcName}
          developerMode={developerMode}
          isModeSelectorOpen={isModeSelectorOpen}
          message={message}
          modeTabs={modeTabs}
          onCloseModeSelector={closeModeSelector}
          onOpenSettings={openSettings}
          onSelectMode={(nextTab) => { selectModeTab(nextTab, "selector"); }}
          onToggleModeSelector={toggleModeSelector}
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
          onOpenTool={openToolFromMenu}
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
          setThemeMode={setThemeMode}
          showGestureDebug={supportsGestureDebug}
          supportsRemoteLaunch={supportsRemoteLaunch}
          themeMode={themeMode}
          toolOptions={getAvailableToolModeIds(presentationAvailable).map((id) => ({
            id,
            label: toolModeDefinitions[id].ariaLabel,
            Icon: toolModeDefinitions[id].Icon
          }))}
          trackpadSettings={effectiveTrackpadSettings}
          updateKeyboardSetting={updateKeyboardSetting}
          updateRemoteSetting={updateRemoteSetting}
          updateAppSetting={updateAppSetting}
          updateTrackpadSetting={updateTrackpadSetting}
        />

        {canShowModeNavigation && <ModeNavigation className="tabs top-mode-tabs" modeTabs={modeTabs} tab={tab} onSelect={selectModeTab} />}

        <ModeWorkspace
          appSettings={appSettings}
          connection={connection}
          keyboardSettings={keyboardSettings}
          onClearAfterSendingChange={(value) => { updateAppSetting("clearTextAfterSending", value); }}
          onRemoteUtilityPanelOpenChange={setIsRemoteUtilityPanelOpen}
          remoteSettings={remoteSettings}
          shouldShowSplitMode={shouldShowSplitMode}
          showVolumeControl={trackpadSettings.showVolumeControl}
          tab={tab}
          trackpadSettings={effectiveTrackpadSettings}
        />

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
          clipboardReadResult={clipboardReadResult}
          pendingAppLaunchId={pendingAppLaunchId}
          pendingClipboardRead={pendingClipboardRead}
          pendingTextTransfer={pendingTextTransfer}
          tab={tab}
          textTransferResult={textTransferResult}
        />
      </main>

      {isBottomModeNavigationVisible && <ModeNavigation className="tabs bottom-mode-tabs" modeTabs={modeTabs} tab={tab} onSelect={selectModeTab} />}
    </div>
  );
}
