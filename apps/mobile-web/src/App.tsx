import { useEffect, useMemo, useState } from "react";
import { ChevronDown, Circle, Menu, MousePointer2 } from "lucide-react";
import { DictationMode } from "./components/DictationMode";
import { GestureDebugMode } from "./components/GestureDebugMode";
import { KeyboardMode } from "./components/KeyboardMode";
import { PairingStatus } from "./components/PairingStatus";
import { RemoteMode } from "./components/RemoteMode";
import { SettingsDrawer } from "./components/SettingsDrawer";
import { TrackpadMode } from "./components/TrackpadMode";
import { TextTransferMode } from "./components/TextTransferMode";
import type { AppSettings } from "./appSettings";
import { clearAppSettings, clearRemoteSettings, clearTrackpadSettings, loadThemeMode, resolveTheme, saveThemeMode, type ThemeMode } from "./appStorage";
import type { TrackpadSettings } from "./gestures";
import type { KeyboardSettings } from "./keyboardSettings";
import { toLiveKeyboardValue } from "./keyboardDelta";
import { parsePairingLink } from "./pairingLink";
import { getPcDisplayName } from "./pcDisplayName";
import type { AudioStateMessage, RemoteLaunchAction } from "./protocol";
import { buildMobileDiagnostics } from "./mobileDiagnostics";
import type { RemoteSettings } from "./remoteSettings";
import { useVolturaAirConnection } from "./useVolturaAirConnection";
import { usePointerInput } from "./input/usePointerInput";
import { triggerHapticFeedback } from "./hapticFeedback";
import { useKeyboardInput } from "./input/useKeyboardInput";
import { usePcSettings } from "./settings/usePcSettings";
import { useSpeechDictation } from "./input/useSpeechDictation";
import { usePwaLifecycle } from "./pwa/usePwaLifecycle";
import { usePairingController } from "./pairing/usePairingController";
import { supportsSplitModeLayout } from "./splitModeLayout";
import { getModeDefinition, getModeTabs, type AppTab as Tab, type MainAppTab as MainTab, type ToolAppTab } from "./appModeTabs";

export function App() {
  const initialPairing = useMemo(() => parsePairingLink(window.location.href, window.location.origin), []);
  const {
    state,
    message,
    send,
    requestAudioState,
    requestPowerAction,
    requestAwakeChange,
    requestAppLaunch,
    requestTextTransfer,
    pendingTextTransfer,
    textTransferResult,
    pendingAppLaunchId,
    appLaunchResult,
    pendingPowerAction,
    powerActionResult,
    pendingAwakeChange,
    awakeResult,
    clientId,
    deviceName,
    activePc,
    pairedPcs,
    audioState,
    awakeCapability,
    powerCapabilities,
    supportsGestureDebug,
    supportsSleep,
    supportsVolumeControl,
    supportsRemoteLaunch,
    supportsTextTransfer,
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
  } = useVolturaAirConnection();
  const [tab, setTab] = useState<Tab>("trackpad");
  const [isSettingsOpen, setIsSettingsOpen] = useState(false);
  const [displayedAudioState, setDisplayedAudioState] = useState<AudioStateMessage | null>(audioState);
  const [themeMode, setThemeMode] = useState<ThemeMode>(() => loadThemeMode());
  const [systemPrefersDark, setSystemPrefersDark] = useState(() => window.matchMedia("(prefers-color-scheme: dark)").matches);
  const [canUseSplitMode, setCanUseSplitMode] = useState(() => supportsSplitModeLayout(window.innerWidth, window.innerHeight));
  const [isTrackpadExpanded, setIsTrackpadExpanded] = useState(false);
  const [areModeTabsCollapsed, setAreModeTabsCollapsed] = useState(false);
  const [isModeSelectorOpen, setIsModeSelectorOpen] = useState(false);
  const [textTransferDraft, setTextTransferDraft] = useState("");
  const [isInputRecoveryDialogDismissed, setIsInputRecoveryDialogDismissed] = useState(false);
  const inputBlockedByElevation = hostStatus?.inputBlockedByElevation === true;

  useEffect(() => {
    if (!inputBlockedByElevation) {
      setIsInputRecoveryDialogDismissed(false);
    }
  }, [inputBlockedByElevation]);
  const hostPointerSpeed = hostStatus?.pointerSpeed;
  const hostDefaultRemoteMode = hostStatus?.defaultRemoteMode;
  const {
    appSettings,
    effectiveTrackpadSettings,
    keyboardSettings,
    remoteSettings,
    setAppSettingsState,
    setKeyboardSettings,
    setRemoteSettingsState,
    setTrackpadSettingsState,
    trackpadSettings
  } = usePcSettings(clientId, activePc?.id ?? null, hostDefaultRemoteMode, hostPointerSpeed);
  const modeTabs = useMemo(() => getModeTabs(appSettings.fourthMode), [appSettings.fourthMode]);
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
    pendingPairing,
    scanPairingQr,
    setPairingDeviceName
  } = usePairingController({ beginNewPairing, connectManualPc, deviceName, initialPairing, message, pairWithToken, setIsSettingsOpen, state });
  const { emit, onTouchCancel, onTouchEnd, onTouchMove, onTouchStart, sendSpecial, sendText, sleepPc } = usePointerInput({
    send,
    state,
    trackpadSettings: effectiveTrackpadSettings
  });
  const {
    committedKeyboardTextRef,
    isComposingRef,
    keyboardText,
    keyboardTextareaRef,
    liveKeyboard,
    onKeyboardTextChange,
    placeLiveKeyboardCaret,
    sendEmptyDelete,
    setKeyboardText,
    setLiveTyping
  } = useKeyboardInput(emit);
  const { canUseSpeech, dictationText, isListening, setDictationText, startSpeech, stopSpeech } = useSpeechDictation(sendText);

  useEffect(() => {
    setDisplayedAudioState(audioState);
  }, [audioState]);

  useEffect(() => {
    const trackpadVolumeVisible = tab === "trackpad" && trackpadSettings.showVolumeControl && !isTrackpadExpanded;
    if (state === "paired" && supportsVolumeControl && (trackpadVolumeVisible || tab === "remote")) {
      requestAudioState();
    }
  }, [isTrackpadExpanded, requestAudioState, state, supportsVolumeControl, tab, trackpadSettings.showVolumeControl]);


  useEffect(() => {
    const mediaQuery = window.matchMedia("(prefers-color-scheme: dark)");
    const onChange = () => setSystemPrefersDark(mediaQuery.matches);
    mediaQuery.addEventListener("change", onChange);
    return () => mediaQuery.removeEventListener("change", onChange);
  }, []);

  useEffect(() => {
    const onResize = () => setCanUseSplitMode(supportsSplitModeLayout(window.innerWidth, window.innerHeight));
    onResize();
    window.addEventListener("resize", onResize);
    return () => window.removeEventListener("resize", onResize);
  }, []);

  useEffect(() => {
    saveThemeMode(themeMode);
    if (themeMode === "system") {
      document.documentElement.removeAttribute("data-theme");
    } else {
      document.documentElement.dataset.theme = themeMode;
    }

    document.querySelector('meta[name="theme-color"]')?.setAttribute("content", resolveTheme(themeMode, systemPrefersDark) === "dark" ? "#101418" : "#f6f8fa");
  }, [systemPrefersDark, themeMode]);

  useEffect(() => {
    if (!supportsGestureDebug && tab === "debug") {
      setTab("trackpad");
    }
  }, [supportsGestureDebug, tab]);

  useEffect(() => {
    if (tab === "debug") {
      setAreModeTabsCollapsed(false);
      setIsModeSelectorOpen(false);
    }
  }, [tab]);

  const updateTrackpadSetting = <Key extends keyof TrackpadSettings>(key: Key, value: TrackpadSettings[Key]) => {
    setTrackpadSettingsState((current) => ({
      ...current,
      settings: { ...current.settings, [key]: value }
    }));

    if (key === "pointerSpeed" && typeof value === "number") {
      setHostPointerSpeed(value);
    }

    if (key === "hapticFeedback" && value === true) {
      triggerHapticFeedback({ ...effectiveTrackpadSettings, hapticFeedback: true });
    }
  };

  const updateKeyboardSetting = <Key extends keyof KeyboardSettings>(key: Key, value: KeyboardSettings[Key]) => {
    setKeyboardSettings((current) => ({ ...current, [key]: value }));
  };

  const launchRemoteAction = (action: RemoteLaunchAction) => {
    if (supportsRemoteLaunch) {
      emit({ type: "remote.launch", action });
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

  const updateRemoteSetting = <Key extends keyof RemoteSettings>(key: Key, value: RemoteSettings[Key]) => {
    const nextSettings = { ...remoteSettings, [key]: value } as RemoteSettings;
    if (key === "mode") {
      if (value === "youtube" || value === "kodi") {
        selectModeTab("remote");
        setIsSettingsOpen(false);
      }

      if (value !== remoteSettings.mode) {
        maybeLaunchRemoteMode(value, nextSettings);
      }
    }

    setRemoteSettingsState((current) => ({
      ...current,
      isStored: true,
      settings: { ...current.settings, [key]: value }
    }));
  };

  const updateAppSetting = <Key extends keyof AppSettings>(key: Key, value: AppSettings[Key]) => {
    setAppSettingsState((current) => ({
      ...current,
      settings: { ...current.settings, [key]: value }
    }));
  };

  const toggleMute = () => {
    if (!supportsVolumeControl) {
      return;
    }

    emit({ type: "audio.mute.toggle" });
  };

  const setVolume = (volume: number) => {
    if (!supportsVolumeControl) {
      return;
    }

    const nextVolume = Math.max(0, Math.min(100, Math.round(volume)));
    setDisplayedAudioState({
      type: "audio.state",
      volume: nextVolume,
      muted: false
    });
    emit({ type: "audio.volume.set", volume: nextVolume });
  };

  const forgetPcAndSettings = (pcId: string) => {
    clearTrackpadSettings(clientId, pcId);
    clearRemoteSettings(clientId, pcId);
    clearAppSettings(clientId, pcId);
    forgetPc(pcId);
  };

  const renderTrackpadMode = () => (
    <TrackpadMode
      audioState={displayedAudioState}
      isExpanded={isTrackpadExpanded}
      onMouseButtonDown={(button) => {
        triggerHapticFeedback(effectiveTrackpadSettings);
        emit({ type: "pointer.button", button, action: "down" });
      }}
      onMouseButtonUp={(button) => {
        emit({ type: "pointer.button", button, action: "up" });
      }}
      onSetVolume={setVolume}
      onToggleExpanded={() => setIsTrackpadExpanded((current) => !current)}
      onToggleMute={toggleMute}
      onTouchCancel={onTouchCancel}
      onTouchEnd={onTouchEnd}
      onTouchMove={onTouchMove}
      onTouchStart={onTouchStart}
      supportsVolumeControl={supportsVolumeControl}
      trackpadSettings={effectiveTrackpadSettings}
    />
  );

  const renderKeyboardMode = () => (
    <KeyboardMode
      committedKeyboardTextRef={committedKeyboardTextRef}
      isComposingRef={isComposingRef}
      keyboardText={keyboardText}
      keyboardTextareaRef={keyboardTextareaRef}
      liveKeyboard={liveKeyboard}
      onKeyboardTextChange={onKeyboardTextChange}
      onSleep={sleepPc}
      onPasteToPc={(text) => requestTextTransfer(text)}
      pasteToPcPending={pendingTextTransfer}
      placeLiveKeyboardCaret={placeLiveKeyboardCaret}
      sendEmptyDelete={sendEmptyDelete}
      sendSpecial={sendSpecial}
      sendText={sendText}
      setKeyboardText={setKeyboardText}
      setLiveTyping={setLiveTyping}
      showArrowKeys={keyboardSettings.showArrowKeys}
      showControlKeys={keyboardSettings.showControlKeys}
      showFunctionKeys={keyboardSettings.showFunctionKeys}
      showPasteToPcButton={keyboardSettings.showPasteToPcButton && supportsTextTransfer}
      showSleepButton={keyboardSettings.showSleepButton && supportsSleep}
      toLiveKeyboardValue={toLiveKeyboardValue}
    />
  );

  const renderRemoteMode = () => (
    <RemoteMode
      appLaunchActions={hostStatus?.appLaunchActions ?? []}
      audioState={displayedAudioState}
      awakeControl={{ awake: awakeCapability, awakeResult, onAwakeChange: requestAwakeChange, pendingAwakeChange }}
      onPointerButtonClick={(button) => emit({ type: "pointer.button", button, action: "click" })}
      onPointerMove={(dx, dy) => emit({ type: "pointer.move", dx, dy })}
      onPowerAction={requestPowerAction}
      onAppLaunch={requestAppLaunch}
      pendingAppLaunchId={pendingAppLaunchId}
      pendingPowerAction={pendingPowerAction}
      powerActionResult={powerActionResult}
      powerCapabilities={powerCapabilities}
      remoteSettings={remoteSettings}
      sendSpecial={sendSpecial}
    />
  );

  const renderSplitMode = () => (
    <div className={`split-mode-shell split-trackpad-${trackpadSettings.splitTrackpadPlacement}`}>
      <div className="split-keyboard-pane" aria-label="Split keyboard panel">
        {renderKeyboardMode()}
      </div>
      <div className="split-trackpad-pane" aria-label="Split trackpad panel">
        {renderTrackpadMode()}
      </div>
    </div>
  );

  const mobileDiagnostics = useMemo(() => buildMobileDiagnostics({
    activePc,
    connectionState: state,
    lastErrorCode: lastConnectionError?.code ?? null,
    lastErrorMessage: lastConnectionError?.message ?? null,
    message,
    pairedPcCount: pairedPcs.length,
    hostStatus
  }), [activePc, hostStatus, lastConnectionError?.code, lastConnectionError?.message, message, pairedPcs.length, state]);

  const shouldShowSplitMode = canUseSplitMode && trackpadSettings.enableSplitMode && (tab === "trackpad" || tab === "keyboard");
  const activeModeTab = tab === "debug" ? undefined : getModeDefinition(tab);
  const ActiveModeIcon = activeModeTab?.Icon;
  const canShowModeNavigation = state === "paired";
  const connectionPcName = state === "paired" && activePc ? getPcDisplayName(activePc) : message;

  const selectModeTab = (nextTab: MainTab, source: "tabs" | "selector" = "tabs") => {
    if (nextTab === "remote") {
      maybeLaunchRemoteMode(remoteSettings.mode, remoteSettings);
    }

    if (tab === nextTab) {
      setAreModeTabsCollapsed(source === "selector" ? false : true);
      setIsModeSelectorOpen(false);
      return;
    }

    setTab(nextTab);
    setAreModeTabsCollapsed(false);
    setIsModeSelectorOpen(false);
  };

  const openToolFromMenu = (tool: ToolAppTab) => {
    setTab(tool);
    setAreModeTabsCollapsed(false);
    setIsModeSelectorOpen(false);
    setIsSettingsOpen(false);
  };

  return (
    <main className={`app-shell ${canShowModeNavigation ? "has-mode-navigation" : ""} ${tab === "trackpad" ? "trackpad-active" : ""} ${tab === "remote" ? "remote-active" : ""} ${tab === "text-transfer" ? "text-transfer-active" : ""} ${shouldShowSplitMode ? "split-mode-active" : ""} ${shouldShowSplitMode && trackpadSettings.splitShowModeButtons ? "split-show-mode-buttons" : ""} ${shouldShowSplitMode && trackpadSettings.splitShowStatusRow ? "split-show-status-row" : ""} ${areModeTabsCollapsed ? "mode-tabs-collapsed" : ""} ${isModeSelectorOpen ? "mode-selector-open" : ""}`}>
      <header className="top-bar">
        <div className="brand-group">
          <button className="icon-button" type="button" aria-label="Open menu" onClick={() => setIsSettingsOpen(true)}>
            <Menu aria-hidden="true" />
          </button>
          <div className="brand">
            <MousePointer2 aria-hidden="true" />
            <span>Voltura Air</span>
          </div>
          {canShowModeNavigation && ActiveModeIcon && activeModeTab && (
            <button
              className="compact-mode-button"
              type="button"
              aria-expanded={isModeSelectorOpen}
              aria-haspopup="menu"
              aria-label="Change mode"
              title={`Change mode (${activeModeTab.label})`}
              onClick={() => setIsModeSelectorOpen((current) => !current)}
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
        <>
          <button className="mode-selector-scrim" type="button" aria-label="Close mode selector" onClick={() => setIsModeSelectorOpen(false)} />
          <div className="mode-selector-popover" role="menu" aria-label="Change mode">
            {modeTabs.map(({ id, label, ariaLabel, Icon }) => (
              <button key={id} role="menuitemradio" aria-checked={tab === id} aria-label={ariaLabel} className={tab === id ? "active" : ""} onClick={() => selectModeTab(id, "selector")}>
                <Icon aria-hidden="true" />
                <span>{label}</span>
              </button>
            ))}
          </div>
        </>
      )}

      {isSettingsOpen && <button className="drawer-scrim" type="button" aria-label="Close menu" onClick={() => setIsSettingsOpen(false)} />}

      {state === "needs-pairing" && !isSettingsOpen && (
        <PairingStatus
          diagnostics={mobileDiagnostics}
          deviceName={pendingPairing ? pairingDeviceName : undefined}
          message={pendingPairing ? "Confirm the device name shown on the PC, or change it before pairing." : pairingScanMessage}
          onDeviceNameChange={pendingPairing ? setPairingDeviceName : undefined}
          onPrimaryAction={pendingPairing ? confirmPendingPairing : scanPairingQr}
          onManualHostSubmit={connectManualHost}
          primaryLabel={pendingPairing ? "Pair" : undefined}
        />
      )}

      {state === "rejected" && !isSettingsOpen && <PairingStatus diagnostics={mobileDiagnostics} message={message} onPrimaryAction={scanPairingQr} onManualHostSubmit={connectManualHost} primaryLabel="Take photo of new QR code" />}

      {state === "unavailable" && activePc && !isSettingsOpen && (
        <PairingStatus
          activePcUnavailable
          diagnostics={mobileDiagnostics}
          message={message}
          onPrimaryAction={() => selectPc(activePc.id)}
          onSecondaryAction={scanPairingQr}
          onManualHostSubmit={connectManualHost}
        />
      )}

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
        onClose={() => setIsSettingsOpen(false)}
        onOpenGestureDebug={supportsGestureDebug ? () => {
          setTab("debug");
          setAreModeTabsCollapsed(false);
          setIsSettingsOpen(false);
        } : undefined}
        onOpenTool={openToolFromMenu}
        onPairingQrSelected={onPairingQrSelected}
        onManualHostSubmit={connectManualHost}
        pairedPcs={pairedPcs}
        pairingQrInputRef={pairingQrInputRef}
        pairingScanMessage={pairingScanMessage}
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
        trackpadSettings={effectiveTrackpadSettings}
        updateKeyboardSetting={updateKeyboardSetting}
        updateRemoteSetting={updateRemoteSetting}
        updateAppSetting={updateAppSetting}
        updateTrackpadSetting={updateTrackpadSetting}
      />

      {canShowModeNavigation && <ModeTabs className="tabs top-mode-tabs" modeTabs={modeTabs} tab={tab} selectModeTab={selectModeTab} />}

      {(tab === "trackpad" || tab === "keyboard") &&
        (shouldShowSplitMode ? renderSplitMode() : tab === "trackpad" ? renderTrackpadMode() : renderKeyboardMode())}

      {tab === "remote" && renderRemoteMode()}

      {tab === "dictation" && (
        <DictationMode
          canUseSpeech={canUseSpeech}
          dictationText={dictationText}
          isListening={isListening}
          sendText={sendText}
          setDictationText={setDictationText}
          startSpeech={startSpeech}
          stopSpeech={stopSpeech}
        />
      )}

      {tab === "text-transfer" && (
        <TextTransferMode
          clearAfterSending={appSettings.clearTextAfterSending}
          clientId={clientId}
          draft={textTransferDraft}
          leftHandedButtons={effectiveTrackpadSettings.leftHandedButtons}
          onClearAfterSendingChange={(value) => updateAppSetting("clearTextAfterSending", value)}
          onDraftChange={setTextTransferDraft}
          onPointerButtonClick={(button) => emit({ type: "pointer.button", button, action: "click" })}
          onTouchCancel={onTouchCancel}
          onTouchEnd={onTouchEnd}
          onTouchMove={onTouchMove}
          onTouchStart={onTouchStart}
          pending={pendingTextTransfer}
          requestTextTransfer={requestTextTransfer}
          result={textTransferResult}
          supported={supportsTextTransfer}
          target={hostStatus?.textTransferTarget}
        />
      )}

      {supportsGestureDebug && tab === "debug" && <GestureDebugMode trackpadSettings={effectiveTrackpadSettings} />}

      {canShowModeNavigation && !areModeTabsCollapsed && <ModeTabs className="tabs bottom-mode-tabs" modeTabs={modeTabs} tab={tab} selectModeTab={selectModeTab} />}

      {inputBlockedByElevation && (
        isInputRecoveryDialogDismissed ? (
          <button
            type="button"
            className="input-recovery-toast"
            onClick={() => setIsInputRecoveryDialogDismissed(false)}
            aria-label="PC input paused. Open recovery options."
          >
            <strong>PC input paused</strong>
            <span>Show options</span>
          </button>
        ) : (
          <section className="input-recovery-dialog" role="dialog" aria-labelledby="input-recovery-dialog-title">
            <h2 id="input-recovery-dialog-title">Administrator app active</h2>
            <p>Pointer control is unavailable. Other controls remain available.</p>
            <div className="input-recovery-dialog-actions">
              <button type="button" className="primary" onClick={() => send({ type: "keyboard.special", key: "D", modifiers: ["Win"] })}>Show desktop</button>
              <button type="button" onClick={() => setIsInputRecoveryDialogDismissed(true)}>Continue</button>
            </div>
          </section>
        )
      )}

      {pendingAppLaunchId !== null && (
        <div className="app-toast pending" role="status">Waiting for the PC to respond…</div>
      )}
      {pendingAppLaunchId === null && appLaunchResult && (
        <div className={`app-toast ${appLaunchResult.succeeded ? "success" : "error"}`} role={appLaunchResult.succeeded ? "status" : "alert"}>
          {appLaunchResult.message}
        </div>
      )}
      {tab !== "text-transfer" && pendingTextTransfer && (
        <div className="app-toast pending" role="status">Waiting for the PC to send text…</div>
      )}
      {tab !== "text-transfer" && !pendingTextTransfer && textTransferResult && (
        <div className={`app-toast ${textTransferResult.succeeded ? "success" : "error"}`} role={textTransferResult.succeeded ? "status" : "alert"}>
          {textTransferResult.message}
        </div>
      )}
    </main>
  );
}

function ModeTabs({ className, modeTabs, tab, selectModeTab }: { className: string; modeTabs: ReturnType<typeof getModeTabs>; tab: Tab; selectModeTab: (nextTab: MainTab) => void }) {
  return (
    <nav className={className} aria-label="Mode">
      {modeTabs.map(({ id, label, ariaLabel, Icon }) => (
        <button key={id} aria-label={ariaLabel} aria-selected={tab === id} className={tab === id ? "active" : ""} onClick={() => selectModeTab(id)}>
          <Icon aria-hidden="true" />
          <span>{label}</span>
        </button>
      ))}
    </nav>
  );
}
