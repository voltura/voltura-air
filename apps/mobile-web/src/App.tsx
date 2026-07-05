import { useEffect, useMemo, useRef, useState } from "react";
import { Circle, Keyboard, Menu, Mic, MousePointer2, Tv } from "lucide-react";
import { DictationMode } from "./components/DictationMode";
import { GestureDebugMode } from "./components/GestureDebugMode";
import { KeyboardMode } from "./components/KeyboardMode";
import { PairingStatus } from "./components/PairingStatus";
import { RemoteMode } from "./components/RemoteMode";
import { SettingsDrawer } from "./components/SettingsDrawer";
import { TrackpadMode } from "./components/TrackpadMode";
import { defaultAppSettings, normalizeAppSettings, type AppSettings } from "./appSettings";
import { defaultTrackpadSettings, GestureRecognizer, normalizeTrackpadSettings, touchesFromList, type TrackpadSettings } from "./gestures";
import { defaultKeyboardSettings, normalizeKeyboardSettings, type KeyboardSettings } from "./keyboardSettings";
import {
  didDeleteLiveKeyboardSentinel,
  fromLiveKeyboardValue,
  getEmptyDeleteMessage,
  getKeyboardDeltaMessages,
  liveKeyboardSentinel,
  toLiveKeyboardValue
} from "./keyboardDelta";
import { parsePairingLink, type PairingLink } from "./pairingLink";
import type { AudioStateMessage, ClientMessage, KeyboardSpecialMessage } from "./protocol";
import { buildMobileDiagnostics } from "./mobileDiagnostics";
import { decodeQrImage } from "./qrCode";
import { defaultRemoteSettings, resolveRemoteSettings, type RemoteModeId, type RemoteSettings } from "./remoteSettings";
import { useVolturaAirConnection } from "./useVolturaAirConnection";

type Tab = "trackpad" | "keyboard" | "remote" | "dictation" | "debug";
type ThemeMode = "system" | "light" | "dark";
const liveKeyboardKey = "voltura-air.liveKeyboard";
const liveKeyboardDefaultMigrationKey = "voltura-air.liveKeyboardDefaultOn";
const splitModeMediaQuery = "(orientation: landscape) and (min-width: 640px)";
const autoRefreshSessionPrefix = "voltura-air.autoRefresh";

type SpeechRecognitionConstructor = new () => SpeechRecognition;

type SpeechRecognition = {
  continuous: boolean;
  interimResults: boolean;
  lang: string;
  start: () => void;
  stop: () => void;
  onresult: ((event: SpeechRecognitionEvent) => void) | null;
  onend: (() => void) | null;
  onerror: (() => void) | null;
};

type SpeechRecognitionEvent = {
  results: ArrayLike<ArrayLike<{ transcript: string } & { isFinal?: boolean }> & { isFinal?: boolean }>;
};

type BeforeInstallPromptEvent = Event & {
  prompt: () => Promise<void>;
  userChoice: Promise<{ outcome: "accepted" | "dismissed"; platform: string }>;
};

declare global {
  interface Window {
    SpeechRecognition?: SpeechRecognitionConstructor;
    webkitSpeechRecognition?: SpeechRecognitionConstructor;
  }

  interface Navigator {
    standalone?: boolean;
  }
}

export function App() {
  const initialPairing = useMemo(() => parsePairingLink(window.location.href, window.location.origin), []);
  const {
    state,
    message,
    send,
    clientId,
    deviceName,
    activePc,
    pairedPcs,
    audioState,
    supportsGestureDebug,
    supportsSleep,
    supportsVolumeControl,
    lastConnectionError,
    hostStatus,
    pairWithToken,
    selectPc,
    connectManualPc,
    disconnectActivePc,
    forgetPc,
    renamePc,
    renameDevice
  } = useVolturaAirConnection();
  const [tab, setTab] = useState<Tab>("trackpad");
  const [isSettingsOpen, setIsSettingsOpen] = useState(false);
  const [installPrompt, setInstallPrompt] = useState<BeforeInstallPromptEvent | null>(null);
  const [isInstalled, setIsInstalled] = useState(() => isRunningStandalone());
  const [keyboardSettings, setKeyboardSettings] = useState(() => loadKeyboardSettings(clientId));
  const [displayedAudioState, setDisplayedAudioState] = useState<AudioStateMessage | null>(audioState);
  const [themeMode, setThemeMode] = useState<ThemeMode>(() => loadThemeMode());
  const [systemPrefersDark, setSystemPrefersDark] = useState(() => window.matchMedia("(prefers-color-scheme: dark)").matches);
  const [canUseSplitMode, setCanUseSplitMode] = useState(() => window.matchMedia(splitModeMediaQuery).matches);
  const [pairingScanMessage, setPairingScanMessage] = useState("Scan the QR code shown on your PC.");
  const [pendingPairing, setPendingPairing] = useState<PairingLink | null>(initialPairing);
  const [pairingDeviceName, setPairingDeviceName] = useState(deviceName);
  const [refreshMessage, setRefreshMessage] = useState("Reload from the PC if the home screen app looks stale.");
  const [keyboardText, setKeyboardText] = useState("");
  const [liveKeyboard, setLiveKeyboard] = useState(() => loadLiveKeyboardDefault());
  const [dictationText, setDictationText] = useState("");
  const [isListening, setIsListening] = useState(false);
  const [isTrackpadExpanded, setIsTrackpadExpanded] = useState(false);
  const committedKeyboardTextRef = useRef("");
  const isComposingRef = useRef(false);
  const lastEmptyDeleteRef = useRef<{ key: string; timeStamp: number } | null>(null);
  const keyboardTextareaRef = useRef<HTMLTextAreaElement | null>(null);
  const pairingQrInputRef = useRef<HTMLInputElement | null>(null);
  const recognizerRef = useRef(new GestureRecognizer());
  const speechRef = useRef<SpeechRecognition | null>(null);

  const canUseSpeech = useMemo(() => Boolean(window.SpeechRecognition || window.webkitSpeechRecognition), []);
  const trackpadSettingsStorageKey = useMemo(() => trackpadSettingsKey(clientId, activePc?.id ?? null), [activePc?.id, clientId]);
  const [trackpadSettingsState, setTrackpadSettingsState] = useState(() => ({
    settings: loadTrackpadSettings(clientId, activePc?.id ?? null),
    storageKey: trackpadSettingsStorageKey
  }));
  const trackpadSettings = trackpadSettingsState.settings;
  const hostDefaultRemoteMode = hostStatus?.defaultRemoteMode;
  const remoteSettingsStorageKey = useMemo(() => remoteSettingsKey(clientId, activePc?.id ?? null), [activePc?.id, clientId]);
  const [remoteSettingsState, setRemoteSettingsState] = useState(() => ({
    ...loadRemoteSettings(clientId, activePc?.id ?? null, hostDefaultRemoteMode),
    storageKey: remoteSettingsStorageKey
  }));
  const remoteSettings = remoteSettingsState.settings;
  const appSettingsStorageKey = useMemo(() => appSettingsKey(clientId, activePc?.id ?? null), [activePc?.id, clientId]);
  const [appSettingsState, setAppSettingsState] = useState(() => ({
    settings: loadAppSettings(clientId, activePc?.id ?? null),
    storageKey: appSettingsStorageKey
  }));
  const appSettings = appSettingsState.settings;

  useEffect(() => {
    setDisplayedAudioState(audioState);
  }, [audioState]);

  useEffect(() => {
    setPairingDeviceName((current) => current || deviceName);
  }, [deviceName]);

  useEffect(() => {
    setTrackpadSettingsState((current) =>
      current.storageKey === trackpadSettingsStorageKey
        ? current
        : {
            settings: loadTrackpadSettings(clientId, activePc?.id ?? null),
            storageKey: trackpadSettingsStorageKey
          }
    );
  }, [activePc?.id, clientId, trackpadSettingsStorageKey]);

  useEffect(() => {
    if (trackpadSettingsState.storageKey === trackpadSettingsStorageKey) {
      localStorage.setItem(trackpadSettingsStorageKey, JSON.stringify(trackpadSettingsState.settings));
    }
  }, [trackpadSettingsState, trackpadSettingsStorageKey]);

  useEffect(() => {
    setRemoteSettingsState((current) =>
      current.storageKey === remoteSettingsStorageKey && (current.isStored || current.settings.mode === (hostDefaultRemoteMode ?? defaultRemoteSettings.mode))
        ? current
        : {
            ...loadRemoteSettings(clientId, activePc?.id ?? null, hostDefaultRemoteMode),
            storageKey: remoteSettingsStorageKey
          }
    );
  }, [activePc?.id, clientId, hostDefaultRemoteMode, remoteSettingsStorageKey]);

  useEffect(() => {
    if (remoteSettingsState.storageKey === remoteSettingsStorageKey && remoteSettingsState.isStored) {
      localStorage.setItem(remoteSettingsStorageKey, JSON.stringify(remoteSettingsState.settings));
    }
  }, [remoteSettingsState, remoteSettingsStorageKey]);

  useEffect(() => {
    setAppSettingsState((current) =>
      current.storageKey === appSettingsStorageKey
        ? current
        : {
            settings: loadAppSettings(clientId, activePc?.id ?? null),
            storageKey: appSettingsStorageKey
          }
    );
  }, [activePc?.id, appSettingsStorageKey, clientId]);

  useEffect(() => {
    if (appSettingsState.storageKey === appSettingsStorageKey) {
      localStorage.setItem(appSettingsStorageKey, JSON.stringify(appSettingsState.settings));
    }
  }, [appSettingsState, appSettingsStorageKey]);

  useEffect(() => {
    localStorage.setItem(keyboardSettingsKey(clientId), JSON.stringify(keyboardSettings));
  }, [clientId, keyboardSettings]);

  useEffect(() => {
    const mediaQuery = window.matchMedia("(prefers-color-scheme: dark)");
    const onChange = () => setSystemPrefersDark(mediaQuery.matches);
    mediaQuery.addEventListener("change", onChange);
    return () => mediaQuery.removeEventListener("change", onChange);
  }, []);

  useEffect(() => {
    const mediaQuery = window.matchMedia(splitModeMediaQuery);
    const onChange = () => setCanUseSplitMode(mediaQuery.matches);
    onChange();
    mediaQuery.addEventListener("change", onChange);
    return () => mediaQuery.removeEventListener("change", onChange);
  }, []);

  useEffect(() => {
    localStorage.setItem(themeModeKey, themeMode);
    if (themeMode === "system") {
      document.documentElement.removeAttribute("data-theme");
    } else {
      document.documentElement.dataset.theme = themeMode;
    }

    document.querySelector('meta[name="theme-color"]')?.setAttribute("content", resolveTheme(themeMode, systemPrefersDark) === "dark" ? "#101418" : "#f6f8fa");
  }, [systemPrefersDark, themeMode]);

  useEffect(() => {
    if (state === "needs-pairing" && !pendingPairing) {
      setPairingScanMessage(message.trim() || "Scan the QR code shown on your PC to pair this home screen app.");
    }
  }, [message, pendingPairing, state]);

  useEffect(() => {
    if (!supportsGestureDebug && tab === "debug") {
      setTab("trackpad");
    }
  }, [supportsGestureDebug, tab]);

  useEffect(() => {
    const onBeforeInstallPrompt = (event: Event) => {
      event.preventDefault();
      setInstallPrompt(event as BeforeInstallPromptEvent);
    };
    const onAppInstalled = () => {
      setIsInstalled(true);
      setInstallPrompt(null);
    };

    window.addEventListener("beforeinstallprompt", onBeforeInstallPrompt);
    window.addEventListener("appinstalled", onAppInstalled);

    return () => {
      window.removeEventListener("beforeinstallprompt", onBeforeInstallPrompt);
      window.removeEventListener("appinstalled", onAppInstalled);
    };
  }, []);

  const emit = (payload: ClientMessage) => {
    if (state === "paired" || payload.type === "pair.hello") {
      send(payload);
    }
  };

  const confirmPendingPairing = () => {
    if (!pendingPairing) {
      return;
    }

    const name = pairingDeviceName.trim() || deviceName;
    setPairingScanMessage("Connecting...");
    setPendingPairing(null);
    setIsSettingsOpen(false);
    pairWithToken(pendingPairing.pairToken, pendingPairing.pcUrl, name);
  };

  const connectManualHost = (target: string) => {
    connectManualPc(target);
    setPendingPairing(null);
    setIsSettingsOpen(false);
    setPairingScanMessage("Connecting to manually entered PC...");
  };

  const onTouchStart = (event: React.TouchEvent<HTMLDivElement>) => {
    event.preventDefault();
    recognizerRef.current.start(touchesFromList(event.targetTouches), event.timeStamp);
  };

  const onTouchMove = (event: React.TouchEvent<HTMLDivElement>) => {
    event.preventDefault();
    recognizerRef.current.move(touchesFromList(event.targetTouches), event.timeStamp, trackpadSettings).forEach(emit);
  };

  const onTouchEnd = (event: React.TouchEvent<HTMLDivElement>) => {
    event.preventDefault();
    const outputs = recognizerRef.current.end(event.timeStamp, trackpadSettings);
    if (outputs.some((output) => output.type === "pointer.button" && output.action === "click")) {
      triggerHapticFeedback(trackpadSettings);
    }
    outputs.forEach(emit);
  };

  const onTouchCancel = (event: React.TouchEvent<HTMLDivElement>) => {
    event.preventDefault();
    recognizerRef.current.cancel();
  };

  const sendSpecial = (key: string, modifiers?: string[]) => {
    emit({ type: "keyboard.special", key, modifiers } satisfies KeyboardSpecialMessage);
  };

  const sendText = (text: string) => {
    if (text.length > 0) {
      emit({ type: "keyboard.text", text });
    }
  };

  const sleepPc = () => {
    emit({ type: "system.sleep" });
  };

  useEffect(() => {
    if (liveKeyboard) {
      placeLiveKeyboardCaret();
    }
  }, [keyboardText, liveKeyboard]);

  const setLiveTyping = (enabled: boolean) => {
    setLiveKeyboard(enabled);
    localStorage.setItem(liveKeyboardKey, String(enabled));
    committedKeyboardTextRef.current = keyboardText;
  };

  const onKeyboardTextChange = (next: string) => {
    if (liveKeyboard && didDeleteLiveKeyboardSentinel(keyboardText, next)) {
      sendSpecial("Backspace");
      setKeyboardText("");
      committedKeyboardTextRef.current = "";
      placeLiveKeyboardCaret();
      return;
    }

    const normalizedNext = liveKeyboard ? fromLiveKeyboardValue(next) : next;
    setKeyboardText(normalizedNext);

    if (!liveKeyboard) {
      committedKeyboardTextRef.current = normalizedNext;
      return;
    }

    if (isComposingRef.current) {
      return;
    }

    getKeyboardDeltaMessages(committedKeyboardTextRef.current, normalizedNext).forEach(emit);
    committedKeyboardTextRef.current = normalizedNext;
  };

  const sendEmptyDelete = (inputTypeOrKey: string, timeStamp: number) => {
    if (!liveKeyboard || isComposingRef.current) {
      return false;
    }

    const message = getEmptyDeleteMessage(inputTypeOrKey, keyboardText);
    if (!message) {
      return false;
    }

    const previous = lastEmptyDeleteRef.current;
    if (previous?.key === message.key && Math.abs(timeStamp - previous.timeStamp) < 40) {
      return true;
    }

    lastEmptyDeleteRef.current = { key: message.key, timeStamp };
    emit(message);
    return true;
  };

  const startSpeech = () => {
    const SpeechRecognitionApi = window.SpeechRecognition ?? window.webkitSpeechRecognition;
    if (!SpeechRecognitionApi) {
      return;
    }

    const recognition = new SpeechRecognitionApi();
    recognition.continuous = true;
    recognition.interimResults = true;
    recognition.lang = navigator.language || "en-US";
    recognition.onresult = (event) => {
      let finalText = "";
      for (let index = 0; index < event.results.length; index += 1) {
        const result = event.results[index];
        if (result.isFinal) {
          finalText += result[0].transcript;
        }
      }
      if (finalText.trim().length > 0) {
        const text = `${finalText.trim()} `;
        setDictationText((current) => `${current}${text}`);
        sendText(text);
      }
    };
    recognition.onend = () => setIsListening(false);
    recognition.onerror = () => setIsListening(false);
    recognition.start();
    speechRef.current = recognition;
    setIsListening(true);
  };

  const stopSpeech = () => {
    speechRef.current?.stop();
    setIsListening(false);
  };

  const placeLiveKeyboardCaret = () => {
    window.requestAnimationFrame(() => {
      const textarea = keyboardTextareaRef.current;
      if (!textarea || !liveKeyboard || document.activeElement !== textarea) {
        return;
      }

      const caretPosition = liveKeyboardSentinel.length + keyboardText.length;
      textarea.setSelectionRange(caretPosition, caretPosition);
    });
  };

  const updateTrackpadSetting = <Key extends keyof TrackpadSettings>(key: Key, value: TrackpadSettings[Key]) => {
    setTrackpadSettingsState((current) => ({
      ...current,
      settings: { ...current.settings, [key]: value }
    }));
  };

  const updateKeyboardSetting = <Key extends keyof KeyboardSettings>(key: Key, value: KeyboardSettings[Key]) => {
    setKeyboardSettings((current) => ({ ...current, [key]: value }));
  };

  const updateRemoteSetting = <Key extends keyof RemoteSettings>(key: Key, value: RemoteSettings[Key]) => {
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

  const installApp = async () => {
    if (!installPrompt) {
      return;
    }

    await installPrompt.prompt();
    const choice = await installPrompt.userChoice;
    if (choice.outcome === "accepted") {
      setIsInstalled(true);
    }
    setInstallPrompt(null);
  };

  const refreshInstalledApp = async () => {
    setRefreshMessage("Refreshing app...");

    if ("serviceWorker" in navigator) {
      const registrations = await navigator.serviceWorker.getRegistrations();
      await Promise.all(registrations.map((registration) => registration.unregister()));
    }

    if ("caches" in window) {
      const cacheNames = await caches.keys();
      await Promise.all(cacheNames.map((cacheName) => caches.delete(cacheName)));
    }

    const freshUrl = new URL(window.location.href);
    freshUrl.searchParams.delete("t");
    freshUrl.searchParams.set("refresh", Date.now().toString());
    window.location.replace(freshUrl);
  };

  useEffect(() => {
    if (!appSettings.autoRefresh || state !== "paired" || !activePc || !hostStatus) {
      return;
    }

    if (hostStatus.developerMode && !hostStatus.developerSessionId) {
      return;
    }

    const refreshKey = getAutoRefreshSessionKey(clientId, activePc.id, hostStatus?.hostVersion, hostStatus?.developerMode, hostStatus?.developerSessionId);
    if (sessionStorage.getItem(refreshKey) === "true") {
      return;
    }

    sessionStorage.setItem(refreshKey, "true");
    void refreshInstalledApp();
  }, [activePc, appSettings.autoRefresh, clientId, hostStatus, hostStatus?.developerMode, hostStatus?.developerSessionId, hostStatus?.hostVersion, state]);

  const scanPairingQr = () => {
    pairingQrInputRef.current?.click();
  };

  const forgetPcAndSettings = (pcId: string) => {
    clearTrackpadSettings(clientId, pcId);
    clearRemoteSettings(clientId, pcId);
    clearAppSettings(clientId, pcId);
    forgetPc(pcId);
  };

  const onPairingQrSelected = async (event: React.ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    event.target.value = "";
    if (!file) {
      return;
    }

    try {
      const startMessage = "Reading QR code...";
      setPairingScanMessage(startMessage);

      let scannedText: string;
      try {
        scannedText = await decodeQrImage(file);
      } catch (decodeError) {
        console.error("QR decode error", decodeError, { name: file?.name, type: file?.type });
        const failMessage = "Could not read the QR code. Try zooming in, retaking the picture, or scanning a new code.";
        setPairingScanMessage(failMessage);
        return;
      }

      if (!scannedText) {
        const failMessage = "Could not read the QR code. Try zooming in, retaking the picture, or scanning a new code.";
        setPairingScanMessage(failMessage);
        return;
      }

      const pairingInfo = parsePairingLink(scannedText, window.location.origin);
      if (!pairingInfo) {
        const noLinkMessage = "No Voltura Air pairing link found in that QR code.";
        setPairingScanMessage(noLinkMessage);
        return;
      }

      setPendingPairing(pairingInfo);
      setPairingDeviceName(deviceName);
      setPairingScanMessage("Confirm the device name shown on the PC, or change it before pairing.");
      setIsSettingsOpen(false);
    } catch (error) {
      console.error("Pairing QR scan failed", error, { name: file?.name, type: file?.type });
      const failMessage = "Could not read the QR code. Try zooming in, retaking the picture, or scanning a new code.";
      setPairingScanMessage(failMessage);
    }
  };

  const renderTrackpadMode = () => (
    <TrackpadMode
      audioState={displayedAudioState}
      isExpanded={isTrackpadExpanded}
      onMouseButtonDown={(button) => {
        triggerHapticFeedback(trackpadSettings);
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
      trackpadSettings={trackpadSettings}
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
      placeLiveKeyboardCaret={placeLiveKeyboardCaret}
      sendEmptyDelete={sendEmptyDelete}
      sendSpecial={sendSpecial}
      sendText={sendText}
      setKeyboardText={setKeyboardText}
      setLiveTyping={setLiveTyping}
      showArrowKeys={keyboardSettings.showArrowKeys}
      showControlKeys={keyboardSettings.showControlKeys}
      showFunctionKeys={keyboardSettings.showFunctionKeys}
      showSleepButton={keyboardSettings.showSleepButton && supportsSleep}
      toLiveKeyboardValue={toLiveKeyboardValue}
    />
  );

  const renderRemoteMode = () => (
    <RemoteMode
      audioState={displayedAudioState}
      onPointerButtonClick={(button) => emit({ type: "pointer.button", button, action: "click" })}
      onPointerMove={(dx, dy) => emit({ type: "pointer.move", dx, dy })}
      remoteSettings={remoteSettings}
      sendSpecial={sendSpecial}
    />
  );

  const renderSplitMode = () => (
    <div className="split-mode-shell">
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

  const shouldShowSplitMode =
    canUseSplitMode && ((tab === "trackpad" && trackpadSettings.enableSplitMode) || (tab === "keyboard" && keyboardSettings.enableSplitMode));

  return (
    <main className={`app-shell ${tab === "trackpad" ? "trackpad-active" : ""} ${tab === "remote" ? "remote-active" : ""} ${shouldShowSplitMode ? "split-mode-active" : ""}`}>
      <header className="top-bar">
        <div className="brand-group">
          <button className="icon-button" type="button" aria-label="Open settings" onClick={() => setIsSettingsOpen(true)}>
            <Menu aria-hidden="true" />
          </button>
          <div className="brand">
            <MousePointer2 aria-hidden="true" />
            <span>Voltura Air</span>
          </div>
        </div>
        <div className={`status ${state}`}>
          <Circle aria-hidden="true" />
          <span>{message}</span>
        </div>
      </header>

      {isSettingsOpen && <button className="drawer-scrim" type="button" aria-label="Close settings" onClick={() => setIsSettingsOpen(false)} />}

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
          setIsSettingsOpen(false);
        } : undefined}
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
        setThemeMode={setThemeMode}
        showGestureDebug={supportsGestureDebug}
        themeMode={themeMode}
        trackpadSettings={trackpadSettings}
        updateKeyboardSetting={updateKeyboardSetting}
        updateRemoteSetting={updateRemoteSetting}
        updateAppSetting={updateAppSetting}
        updateTrackpadSetting={updateTrackpadSetting}
      />

      <nav className="tabs" aria-label="Mode">
        <button aria-label="Trackpad mode" className={tab === "trackpad" ? "active" : ""} onClick={() => setTab("trackpad")}>
          <MousePointer2 aria-hidden="true" />
          <span>Trackpad</span>
        </button>
        <button aria-label="Keyboard mode" className={tab === "keyboard" ? "active" : ""} onClick={() => setTab("keyboard")}>
          <Keyboard aria-hidden="true" />
          <span>Keyboard</span>
        </button>
        <button aria-label="Remote mode" className={tab === "remote" ? "active" : ""} onClick={() => setTab("remote")}>
          <Tv aria-hidden="true" />
          <span>Remote</span>
        </button>
        <button aria-label="Dictation mode" className={tab === "dictation" ? "active" : ""} onClick={() => setTab("dictation")}>
          <Mic aria-hidden="true" />
          <span>Dictate</span>
        </button>
      </nav>

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

      {supportsGestureDebug && tab === "debug" && <GestureDebugMode trackpadSettings={trackpadSettings} />}
    </main>
  );
}

function trackpadSettingsKey(clientId: string, pcId: string | null): string {
  return pcId ? `voltura-air.trackpadSettings.${clientId}.${pcId}` : baseTrackpadSettingsKey(clientId);
}

function remoteSettingsKey(clientId: string, pcId: string | null): string {
  return pcId ? `voltura-air.remoteSettings.${clientId}.${pcId}` : baseRemoteSettingsKey(clientId);
}

function appSettingsKey(clientId: string, pcId: string | null): string {
  return pcId ? `voltura-air.appSettings.${clientId}.${pcId}` : baseAppSettingsKey(clientId);
}

function keyboardSettingsKey(clientId: string): string {
  return `voltura-air.keyboardSettings.${clientId}`;
}

function loadLiveKeyboardDefault(): boolean {
  if (localStorage.getItem(liveKeyboardDefaultMigrationKey) !== "true") {
    localStorage.setItem(liveKeyboardDefaultMigrationKey, "true");
    localStorage.setItem(liveKeyboardKey, "true");
    return true;
  }

  return localStorage.getItem(liveKeyboardKey) !== "false";
}

const themeModeKey = "voltura-air.themeMode";

function loadThemeMode(): ThemeMode {
  const stored = localStorage.getItem(themeModeKey);
  return stored === "light" || stored === "dark" ? stored : "system";
}

function resolveTheme(themeMode: ThemeMode, systemPrefersDark: boolean): "light" | "dark" {
  return themeMode === "system" ? (systemPrefersDark ? "dark" : "light") : themeMode;
}

function loadTrackpadSettings(clientId: string, pcId: string | null): TrackpadSettings {
  const stored = localStorage.getItem(trackpadSettingsKey(clientId, pcId));
  if (!stored) {
    return defaultTrackpadSettings;
  }

  try {
    return normalizeTrackpadSettings(JSON.parse(stored));
  } catch {
    return defaultTrackpadSettings;
  }
}

function baseTrackpadSettingsKey(clientId: string): string {
  return `voltura-air.trackpadSettings.${clientId}`;
}

function clearTrackpadSettings(clientId: string, pcId: string): void {
  localStorage.removeItem(trackpadSettingsKey(clientId, pcId));
}

function loadRemoteSettings(clientId: string, pcId: string | null, hostDefaultMode?: RemoteModeId): { settings: RemoteSettings; isStored: boolean } {
  return resolveRemoteSettings(localStorage.getItem(remoteSettingsKey(clientId, pcId)), hostDefaultMode);
}

function baseRemoteSettingsKey(clientId: string): string {
  return `voltura-air.remoteSettings.${clientId}`;
}

function clearRemoteSettings(clientId: string, pcId: string): void {
  localStorage.removeItem(remoteSettingsKey(clientId, pcId));
}

function loadAppSettings(clientId: string, pcId: string | null): AppSettings {
  const stored = localStorage.getItem(appSettingsKey(clientId, pcId));
  if (!stored) {
    return defaultAppSettings;
  }

  try {
    return normalizeAppSettings(JSON.parse(stored));
  } catch {
    return defaultAppSettings;
  }
}

function baseAppSettingsKey(clientId: string): string {
  return `voltura-air.appSettings.${clientId}`;
}

function clearAppSettings(clientId: string, pcId: string): void {
  localStorage.removeItem(appSettingsKey(clientId, pcId));
}

function getAutoRefreshSessionKey(clientId: string, pcId: string, hostVersion?: string, developerMode?: boolean, developerSessionId?: string): string {
  const refreshScope = developerMode ? `dev.${developerSessionId || "enabled"}` : `version.${hostVersion || "unknown"}`;
  return `${autoRefreshSessionPrefix}.${clientId}.${pcId}.${refreshScope}`;
}

function loadKeyboardSettings(clientId: string): KeyboardSettings {
  const stored = localStorage.getItem(keyboardSettingsKey(clientId));
  if (!stored) {
    return defaultKeyboardSettings;
  }

  try {
    return normalizeKeyboardSettings(JSON.parse(stored));
  } catch {
    return defaultKeyboardSettings;
  }
}

function isRunningStandalone(): boolean {
  return window.matchMedia("(display-mode: standalone)").matches || window.navigator.standalone === true;
}

function triggerHapticFeedback(settings: TrackpadSettings): void {
  if (settings.hapticFeedback && typeof navigator.vibrate === "function") {
    navigator.vibrate(12);
  }
}
