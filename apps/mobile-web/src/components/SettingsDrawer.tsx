import { useEffect, useState } from "react";
import { Camera, Clipboard, Download, MousePointer2, Power, RefreshCw, X } from "lucide-react";
import { copyTextToClipboard } from "../mobileDiagnostics";
import { getPcDisplayName } from "../pcDisplayName";
import { normalizeManualHostInput } from "../pairingFeedback";
import type { TrackpadSettings } from "../gestures";
import type { KeyboardSettings } from "../keyboardSettings";
import type { PcProfile } from "../pcProfiles";

type ThemeMode = "system" | "light" | "dark";
type SettingsSection = "connection" | "trackpad" | "keyboard" | "appearance" | "app";

type SettingsDrawerProps = {
  activePc: PcProfile | null;
  diagnostics: string;
  deviceName: string;
  disconnectActivePc: () => void;
  forgetPc: (pcId: string) => void;
  installApp: () => void;
  installPrompt: Event | null;
  isInstalled: boolean;
  isOpen: boolean;
  keyboardSettings: KeyboardSettings;
  onClose: () => void;
  onPairingQrSelected: (event: React.ChangeEvent<HTMLInputElement>) => void;
  onManualHostSubmit: (target: string) => void;
  onOpenGestureDebug?: () => void;
  pairedPcs: PcProfile[];
  pairingQrInputRef: React.RefObject<HTMLInputElement | null>;
  pairingScanMessage: string;
  refreshInstalledApp: () => void;
  refreshMessage: string;
  renameDevice: (name: string) => void;
  renamePc: (pcId: string, name: string) => void;
  scanPairingQr: () => void;
  selectPc: (pcId: string) => void;
  setThemeMode: React.Dispatch<React.SetStateAction<ThemeMode>>;
  showGestureDebug: boolean;
  themeMode: ThemeMode;
  trackpadSettings: TrackpadSettings;
  updateKeyboardSetting: <Key extends keyof KeyboardSettings>(key: Key, value: KeyboardSettings[Key]) => void;
  updateTrackpadSetting: <Key extends keyof TrackpadSettings>(key: Key, value: TrackpadSettings[Key]) => void;
};

export function SettingsDrawer({
  activePc,
  diagnostics,
  deviceName,
  disconnectActivePc,
  forgetPc,
  installApp,
  installPrompt,
  isInstalled,
  isOpen,
  keyboardSettings,
  onClose,
  onPairingQrSelected,
  onManualHostSubmit,
  onOpenGestureDebug,
  pairedPcs,
  pairingQrInputRef,
  pairingScanMessage,
  refreshInstalledApp,
  refreshMessage,
  renameDevice,
  renamePc,
  scanPairingQr,
  selectPc,
  setThemeMode,
  showGestureDebug,
  themeMode,
  trackpadSettings,
  updateKeyboardSetting,
  updateTrackpadSetting
}: SettingsDrawerProps) {
  const [manualHost, setManualHost] = useState("");
  const [manualHostError, setManualHostError] = useState("");
  const [copyDiagnosticsStatus, setCopyDiagnosticsStatus] = useState("");
  const [manualDiagnostics, setManualDiagnostics] = useState("");
  const [openSection, setOpenSection] = useState<SettingsSection | null>(null);

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

  const submitManualHost = (event: React.FormEvent<HTMLFormElement>) => {
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

  useEffect(() => {
    if (!isOpen) {
      setOpenSection(null);
    }
  }, [isOpen]);

  const toggleSection = (event: React.MouseEvent<HTMLElement>, section: SettingsSection) => {
    event.preventDefault();
    setOpenSection((current) => (current === section ? null : section));
  };

  return (
    <aside className={`settings-drawer ${isOpen ? "open" : ""}`} aria-hidden={!isOpen}>
      <header className="drawer-header">
        <div className="drawer-title">
          <h2>Settings</h2>
          <span>v{__APP_VERSION__}</span>
        </div>
        <button className="icon-button" type="button" aria-label="Close settings" onClick={onClose}>
          <X aria-hidden="true" />
        </button>
      </header>

      <details className="settings-section" key={`connection-${isOpen ? "open" : "closed"}`} open={openSection === "connection"}>
        <summary onClick={(event) => toggleSection(event, "connection")}>
          <span>Connection</span>
        </summary>
        <div className="settings-section-body">
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
                    <small>{pc.id === activePc?.id ? "Active" : "Saved"} · {pc.url}</small>
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
            <span>Diagnostics</span>
          </div>
          <p>Copy redacted connection details for troubleshooting. Pairing secrets, device tokens, and hashes are not included.</p>
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
            <span>Add PC manually</span>
          </div>
          <p>Use this when the PC IP or port changed, or when a QR page was opened before the host changed network.</p>
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
        </div>
      </details>

      <details className="settings-section" key={`trackpad-${isOpen ? "open" : "closed"}`} open={openSection === "trackpad"}>
        <summary onClick={(event) => toggleSection(event, "trackpad")}>
          <span>Trackpad</span>
        </summary>
        <div className="settings-section-body">
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

        <label className="toggle-row">
          <span>Enable split mode</span>
          <input type="checkbox" checked={trackpadSettings.enableSplitMode} onChange={(event) => updateTrackpadSetting("enableSplitMode", event.target.checked)} />
        </label>

        <label className="toggle-row">
          <span>Haptic feedback</span>
          <input type="checkbox" checked={trackpadSettings.hapticFeedback} onChange={(event) => updateTrackpadSetting("hapticFeedback", event.target.checked)} />
        </label>

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
        </div>
      </details>

      <details className="settings-section" key={`keyboard-${isOpen ? "open" : "closed"}`} open={openSection === "keyboard"}>
        <summary onClick={(event) => toggleSection(event, "keyboard")}>
          <span>Keyboard</span>
        </summary>
        <div className="settings-section-body">
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

          <label className="toggle-row">
          <span>Enable split mode</span>
          <input type="checkbox" checked={keyboardSettings.enableSplitMode} onChange={(event) => updateKeyboardSetting("enableSplitMode", event.target.checked)} />
          </label>
        </div>
      </details>

      <details className="settings-section" key={`appearance-${isOpen ? "open" : "closed"}`} open={openSection === "appearance"}>
        <summary onClick={(event) => toggleSection(event, "appearance")}>
          <span>Appearance</span>
        </summary>
        <div className="settings-section-body">
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
        </div>
      </details>

      <details className="settings-section" key={`app-${isOpen ? "open" : "closed"}`} open={openSection === "app"}>
        <summary onClick={(event) => toggleSection(event, "app")}>
          <span>App</span>
        </summary>
        <div className="settings-section-body">
          <div className="install-card">
          <div className="install-title">
            <Download aria-hidden="true" />
            <span>Home screen app</span>
          </div>
          {isInstalled ? (
            <p>Voltura Air is already running like an installed app.</p>
          ) : installPrompt ? (
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
          <p>{refreshMessage}</p>
          <button type="button" onClick={refreshInstalledApp}>
            <RefreshCw aria-hidden="true" />
            <span>Refresh app</span>
          </button>
          </div>
        </div>
      </details>
    </aside>
  );
}

function isAppleTouchDevice(): boolean {
  return /iPad|iPhone|iPod/.test(navigator.userAgent) || (navigator.platform === "MacIntel" && navigator.maxTouchPoints > 1);
}
