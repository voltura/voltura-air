import { useId, useState, type FormEvent, type MouseEvent, type ReactNode } from "react";
import { Camera, Clipboard, Download, MousePointer2, Power, RefreshCw, X } from "lucide-react";
import { copyTextToClipboard } from "../mobileDiagnostics";
import { getPcDisplayName } from "../pcDisplayName";
import { normalizeManualHostInput } from "../pairingFeedback";
import type { SettingsDrawerProps, SettingsSection } from "./SettingsDrawerTypes";
import { InfoButton } from "./InfoButton";
import { supportsHapticFeedback } from "../hapticFeedback";
import { getEffectiveFourthMode } from "../appModeTabs";

type SettingsSectionDetailsProps = {
  children: ReactNode;
  isOpen: boolean;
  label: string;
  onToggle: (event: MouseEvent<HTMLElement>, section: SettingsSection) => void;
  section: SettingsSection;
};

export function SettingsSectionDetails({ children, isOpen, label, onToggle, section }: SettingsSectionDetailsProps) {
  return (
    <details className="settings-section" data-settings-section={section} open={isOpen}>
      <summary onClick={(event) => onToggle(event, section)}>
        <span>{label}</span>
      </summary>
      <div className="settings-section-body">{children}</div>
    </details>
  );
}

export function ConnectionSettingsSection({
  activePc,
  deviceName,
  diagnostics,
  disconnectActivePc,
  forgetPc,
  onManualHostSubmit,
  onPairingQrSelected,
  pairedPcs,
  pairingQrInputRef,
  pairingScanMessage,
  renameDevice,
  renamePc,
  scanPairingQr,
  selectPc
}: Pick<
  SettingsDrawerProps,
  | "activePc"
  | "deviceName"
  | "diagnostics"
  | "disconnectActivePc"
  | "forgetPc"
  | "onManualHostSubmit"
  | "onPairingQrSelected"
  | "pairedPcs"
  | "pairingQrInputRef"
  | "pairingScanMessage"
  | "renameDevice"
  | "renamePc"
  | "scanPairingQr"
  | "selectPc"
>) {
  const [manualHost, setManualHost] = useState("");
  const [manualHostError, setManualHostError] = useState("");
  const [copyDiagnosticsStatus, setCopyDiagnosticsStatus] = useState("");
  const [manualDiagnostics, setManualDiagnostics] = useState("");

  const copyDiagnostics = async () => {
    setCopyDiagnosticsStatus("");
    setManualDiagnostics("");
    const result = await copyTextToClipboard(diagnostics);
    if (result === "copied") {
      setCopyDiagnosticsStatus("Diagnostics copied.");
      return;
    }

    setManualDiagnostics(diagnostics);
    setCopyDiagnosticsStatus("Could not copy automatically. Select the diagnostics below and copy manually.");
  };

  const submitManualHost = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const target = normalizeManualHostInput(manualHost, window.location.href);
    if (!target) {
      setManualHostError("Enter a host URL, IP:port, pairing link, or port number.");
      return;
    }

    onManualHostSubmit(target);
    setManualHost("");
    setManualHostError("");
  };

  return (
    <>
      <label className="setting-group">
        <span>This device name</span>
        <input className="text-input" type="text" value={deviceName} onChange={(event) => renameDevice(event.target.value)} placeholder="Joakim's iPhone" />
      </label>

      <div className="install-card">
        <div className="install-title">
          <Power aria-hidden="true" />
          <span>PC connection</span>
        </div>
        <p>{activePc ? `Active PC: ${getPcDisplayName(activePc)}` : "No active PC. Choose a saved PC or scan a pairing QR."}</p>
        {activePc && (
          <button type="button" className="danger-button" onClick={disconnectActivePc}>
            <Power aria-hidden="true" />
            <span>Disconnect this PC</span>
          </button>
        )}
        {pairedPcs.length > 0 && (
          <div className="pc-list">
            {pairedPcs.map((pc) => (
              <div className={`pc-row ${pc.id === activePc?.id ? "active" : ""}`} key={pc.id}>
                <div className="pc-meta">
                  <input aria-label="PC name" className="pc-name-input" type="text" value={pc.name} onChange={(event) => renamePc(pc.id, event.target.value)} />
                  <small>{pc.id === activePc?.id ? "Active" : "Saved"} &middot; {pc.url}</small>
                </div>
                <div className="pc-actions">
                  {pc.id !== activePc?.id && (
                    <button type="button" onClick={() => selectPc(pc.id)}>
                      <RefreshCw aria-hidden="true" />
                      <span>Connect</span>
                    </button>
                  )}
                  <button type="button" className="danger-button" onClick={() => forgetPc(pc.id)}>
                    <X aria-hidden="true" />
                    <span>Forget</span>
                  </button>
                </div>
              </div>
            ))}
          </div>
        )}
      </div>

      <div className="install-card">
        <div className="install-title">
          <Camera aria-hidden="true" />
          <span>Pair from QR code</span>
        </div>
        <p>{pairingScanMessage}</p>
        <input ref={pairingQrInputRef} className="visually-hidden" type="file" accept="image/*" capture="environment" onChange={onPairingQrSelected} />
        <button type="button" onClick={scanPairingQr}>
          <Camera aria-hidden="true" />
          <span>Take photo of QR code</span>
        </button>
      </div>

      <div className="install-card">
        <div className="install-title">
          <Clipboard aria-hidden="true" />
          <span className="setting-label-with-info">
            <span>Diagnostics</span>
            <InfoButton title="Connection diagnostics" size="detailed" description="Copies redacted connection details for troubleshooting. Pairing secrets, device tokens, and hashes are not included." />
          </span>
        </div>
        <p>Copy redacted troubleshooting details.</p>
        <button type="button" onClick={copyDiagnostics}>
          <Clipboard aria-hidden="true" />
          <span>Copy diagnostics</span>
        </button>
        {copyDiagnosticsStatus && <p className="pairing-inline-status">{copyDiagnosticsStatus}</p>}
        {manualDiagnostics && (
          <textarea
            aria-label="Diagnostics text"
            className="text-input diagnostics-textarea"
            onFocus={(event) => event.currentTarget.select()}
            readOnly
            rows={8}
            value={manualDiagnostics}
          />
        )}
      </div>

      <div className="install-card">
        <div className="install-title">
          <Power aria-hidden="true" />
          <span className="setting-label-with-info">
            <span>Add PC manually</span>
            <InfoButton title="Connect manually" size="detailed" description="Use this when the PC IP or port changed, or when a QR page was opened before the host changed network." />
          </span>
        </div>
        <p>Connect using a host address or pairing link.</p>
        <form className="manual-pc-form" onSubmit={submitManualHost}>
          <label className="setting-group">
            <span>Host or pairing link</span>
            <input
              className="text-input"
              inputMode="url"
              placeholder="192.168.1.50:51395"
              value={manualHost}
              onChange={(event) => {
                setManualHost(event.target.value);
                setManualHostError("");
              }}
            />
          </label>
          <button type="submit">Connect to PC</button>
          {manualHostError && <p className="pairing-inline-error">{manualHostError}</p>}
        </form>
      </div>
    </>
  );
}

export function TrackpadSettingsSection({
  onOpenGestureDebug,
  showGestureDebug,
  trackpadSettings,
  updateTrackpadSetting
}: Pick<SettingsDrawerProps, "onOpenGestureDebug" | "showGestureDebug" | "trackpadSettings" | "updateTrackpadSetting">) {
  const hapticFeedbackId = useId();
  const hapticFeedbackSupported = supportsHapticFeedback();

  return (
    <>
      {showGestureDebug && onOpenGestureDebug && (
        <button type="button" onClick={onOpenGestureDebug}>
          <MousePointer2 aria-hidden="true" />
          <span>Open gesture debug</span>
        </button>
      )}

      <label className="toggle-row">
        <span>Tap to click</span>
        <input type="checkbox" checked={trackpadSettings.tapToClick} onChange={(event) => updateTrackpadSetting("tapToClick", event.target.checked)} />
      </label>

      <label className="toggle-row">
        <span>Pointer smoothing</span>
        <input type="checkbox" checked={trackpadSettings.pointerSmoothing} onChange={(event) => updateTrackpadSetting("pointerSmoothing", event.target.checked)} />
      </label>

      <label className="toggle-row">
        <span>Pointer acceleration</span>
        <input type="checkbox" checked={trackpadSettings.pointerAcceleration} onChange={(event) => updateTrackpadSetting("pointerAcceleration", event.target.checked)} />
      </label>

      <label className="toggle-row">
        <span>Scroll acceleration</span>
        <input type="checkbox" checked={trackpadSettings.scrollAcceleration} onChange={(event) => updateTrackpadSetting("scrollAcceleration", event.target.checked)} />
      </label>

      <label className="toggle-row">
        <span>Vertical scrolling</span>
        <input type="checkbox" checked={trackpadSettings.verticalScroll} onChange={(event) => updateTrackpadSetting("verticalScroll", event.target.checked)} />
      </label>

      <label className="toggle-row">
        <span>Horizontal scrolling</span>
        <input type="checkbox" checked={trackpadSettings.horizontalScroll} onChange={(event) => updateTrackpadSetting("horizontalScroll", event.target.checked)} />
      </label>

      <label className="toggle-row">
        <span>Pinch zoom</span>
        <input type="checkbox" checked={trackpadSettings.zoomGestures} onChange={(event) => updateTrackpadSetting("zoomGestures", event.target.checked)} />
      </label>

      <label className="toggle-row">
        <span>Show volume control</span>
        <input type="checkbox" checked={trackpadSettings.showVolumeControl} onChange={(event) => updateTrackpadSetting("showVolumeControl", event.target.checked)} />
      </label>

      <div className="toggle-row">
        <span className="setting-label-with-info">
          <label htmlFor={hapticFeedbackId}>Haptic feedback{hapticFeedbackSupported ? "" : " (not supported)"}</label>
          <InfoButton title="Haptic feedback" description="Vibrates on trackpad taps and click-button presses. It is only available when this browser and device expose vibration to web apps." />
        </span>
        <input
          id={hapticFeedbackId}
          type="checkbox"
          checked={hapticFeedbackSupported && trackpadSettings.hapticFeedback}
          disabled={!hapticFeedbackSupported}
          onChange={(event) => updateTrackpadSetting("hapticFeedback", event.target.checked)}
        />
      </div>

      <label className="toggle-row">
        <span>Left-handed button layout</span>
        <input type="checkbox" checked={trackpadSettings.leftHandedButtons} onChange={(event) => updateTrackpadSetting("leftHandedButtons", event.target.checked)} />
      </label>

      <label className="toggle-row">
        <span>Large click buttons</span>
        <input type="checkbox" checked={trackpadSettings.largeClickButtons} onChange={(event) => updateTrackpadSetting("largeClickButtons", event.target.checked)} />
      </label>

      <div className="setting-group">
        <span>Scroll direction</span>
        <div className="segmented-control">
          <button type="button" className={trackpadSettings.scrollDirection === "normal" ? "active" : ""} onClick={() => updateTrackpadSetting("scrollDirection", "normal")}>
            Natural scrolling
          </button>
          <button type="button" className={trackpadSettings.scrollDirection === "inverted" ? "active" : ""} onClick={() => updateTrackpadSetting("scrollDirection", "inverted")}>
            Traditional scrolling
          </button>
        </div>
      </div>

      <label className="setting-group">
        <span>Pointer speed</span>
        <div className="range-row">
          <input
            type="range"
            min="10"
            max="100"
            step="5"
            value={trackpadSettings.pointerSpeed}
            onChange={(event) => updateTrackpadSetting("pointerSpeed", Number(event.target.value))}
          />
          <output>{trackpadSettings.pointerSpeed}%</output>
        </div>
      </label>
    </>
  );
}

export function KeyboardSettingsSection({
  keyboardSettings,
  updateKeyboardSetting
}: Pick<SettingsDrawerProps, "keyboardSettings" | "updateKeyboardSetting">) {
  return (
    <>
      <label className="toggle-row">
        <span>Show function keys</span>
        <input type="checkbox" checked={keyboardSettings.showFunctionKeys} onChange={(event) => updateKeyboardSetting("showFunctionKeys", event.target.checked)} />
      </label>

      <label className="toggle-row">
        <span>Show control keys</span>
        <input type="checkbox" checked={keyboardSettings.showControlKeys} onChange={(event) => updateKeyboardSetting("showControlKeys", event.target.checked)} />
      </label>

      <label className="toggle-row">
        <span>Show arrow keys</span>
        <input type="checkbox" checked={keyboardSettings.showArrowKeys} onChange={(event) => updateKeyboardSetting("showArrowKeys", event.target.checked)} />
      </label>

      <label className="toggle-row">
        <span>Show sleep button</span>
        <input type="checkbox" checked={keyboardSettings.showSleepButton} onChange={(event) => updateKeyboardSetting("showSleepButton", event.target.checked)} />
      </label>

    </>
  );
}

export function CustomPointerSettingsSection({ customPointerEnabled, setHostCustomPointer }: Pick<SettingsDrawerProps, "customPointerEnabled" | "setHostCustomPointer">) {
  const available = typeof customPointerEnabled === "boolean";
  return (
    <>
      <p className="settings-description">Turn Custom pointer on or off for this PC.</p>
      <label className="toggle-row">
        <span>Custom pointer</span>
        <input type="checkbox" checked={customPointerEnabled === true} disabled={!available} onChange={(event) => setHostCustomPointer?.(event.target.checked)} />
      </label>
      {!available && <p className="pairing-inline-status">Connect to a host that supports Custom pointer.</p>}
    </>
  );
}

export function RemoteSettingsSection({
  remoteSettings,
  supportsRemoteLaunch,
  updateRemoteSetting
}: Pick<SettingsDrawerProps, "remoteSettings" | "supportsRemoteLaunch" | "updateRemoteSetting">) {
  return (
    <>
      <label className="toggle-row">
        <span>Navigation ring</span>
        <input type="checkbox" checked={remoteSettings.navigationRing} onChange={(event) => updateRemoteSetting("navigationRing", event.target.checked)} />
      </label>

      <div className="setting-group">
        <span>Remote mode</span>
        <div className="segmented-control three" aria-label="Remote mode">
          <button type="button" className={remoteSettings.mode === "standard" ? "active" : ""} onClick={() => updateRemoteSetting("mode", "standard")}>
            Standard
          </button>
          <button type="button" className={remoteSettings.mode === "youtube" ? "active" : ""} onClick={() => updateRemoteSetting("mode", "youtube")}>
            YouTube
          </button>
          <button type="button" className={remoteSettings.mode === "kodi" ? "active" : ""} onClick={() => updateRemoteSetting("mode", "kodi")}>
            Kodi
          </button>
        </div>
      </div>

      <div className="setting-group">
        <span>Extra helper buttons</span>
        <label className="toggle-row">
          <span>Window actions</span>
          <input type="checkbox" checked={remoteSettings.showWindowHelpers} onChange={(event) => updateRemoteSetting("showWindowHelpers", event.target.checked)} />
        </label>
        <label className="toggle-row">
          <span>Browser tabs and reload</span>
          <input type="checkbox" checked={remoteSettings.showBrowserHelpers} onChange={(event) => updateRemoteSetting("showBrowserHelpers", event.target.checked)} />
        </label>
      </div>

      {supportsRemoteLaunch && (
        <>
          <label className="toggle-row">
            <span>Open YouTube from Remote mode</span>
            <input type="checkbox" checked={remoteSettings.openYoutube} onChange={(event) => updateRemoteSetting("openYoutube", event.target.checked)} />
          </label>

          <label className="toggle-row">
            <span>Start Kodi from Remote mode</span>
            <input type="checkbox" checked={remoteSettings.startKodi} onChange={(event) => updateRemoteSetting("startKodi", event.target.checked)} />
          </label>
        </>
      )}
    </>
  );
}

export function AppearanceSettingsSection({
  setThemeMode,
  themeMode
}: Pick<SettingsDrawerProps, "setThemeMode" | "themeMode">) {
  return (
    <div className="setting-group">
      <span>Theme</span>
      <div className="segmented-control three">
        <button type="button" className={themeMode === "system" ? "active" : ""} onClick={() => setThemeMode("system")}>
          System
        </button>
        <button type="button" className={themeMode === "light" ? "active" : ""} onClick={() => setThemeMode("light")}>
          Light
        </button>
        <button type="button" className={themeMode === "dark" ? "active" : ""} onClick={() => setThemeMode("dark")}>
          Dark
        </button>
      </div>
    </div>
  );
}

export function AppSettingsSection({
  appSettings,
  installApp,
  installPrompt,
  isInstalled,
  presentationAvailable,
  refreshInstalledApp,
  refreshMessage,
  updateAppSetting
}: Pick<SettingsDrawerProps, "appSettings" | "installApp" | "installPrompt" | "isInstalled" | "presentationAvailable" | "refreshInstalledApp" | "refreshMessage" | "updateAppSetting">) {
  return (
    <div className="install-card">
      <label className="setting-group">
        <span>Fourth mode button</span>
        <select className="text-input fourth-mode-select" value={getEffectiveFourthMode(appSettings.fourthMode, presentationAvailable)} onChange={(event) => updateAppSetting("fourthMode", event.target.value === "presentation" || event.target.value === "text-transfer" || event.target.value === "clipboard-read" ? event.target.value : "dictation")}>
          {presentationAvailable && <option value="presentation">Presentation</option>}
          <option value="dictation">Dictation</option>
          <option value="text-transfer">Send text to PC</option>
          <option value="clipboard-read">Get text from PC</option>
        </select>
      </label>
      {!isInstalled && (
        <>
          <div className="install-title">
            <Download aria-hidden="true" />
            <span>Home screen app</span>
          </div>
          {installPrompt ? (
        <>
          <p>Add Voltura Air to this device for a normal app icon and faster launching.</p>
          <button type="button" onClick={installApp}>
            <Download aria-hidden="true" />
            <span>Install app</span>
          </button>
        </>
      ) : isAppleTouchDevice() ? (
        <ol className="install-steps">
          <li>Tap Share.</li>
          <li>Tap Add to Home Screen.</li>
          <li>Tap Add.</li>
        </ol>
      ) : (
        <ol className="install-steps">
          <li>Open the browser menu.</li>
          <li>Choose Add to Home screen or Install app.</li>
          <li>Confirm the shortcut.</li>
        </ol>
          )}
        </>
      )}
      {!isInstalled && <p>{refreshMessage}</p>}
      <label className="toggle-row">
        <span>Auto refresh</span>
        <input type="checkbox" checked={appSettings.autoRefresh} onChange={(event) => updateAppSetting("autoRefresh", event.target.checked)} />
      </label>
      <button type="button" onClick={refreshInstalledApp}>
        <RefreshCw aria-hidden="true" />
        <span>Refresh app</span>
      </button>
    </div>
  );
}

function isAppleTouchDevice(): boolean {
  return /iPad|iPhone|iPod/.test(navigator.userAgent) || (navigator.platform === "MacIntel" && navigator.maxTouchPoints > 1);
}
