import { Camera, Download, Power, RefreshCw, X } from "lucide-react";
import { getPcDisplayName } from "../pcDisplayName";
import type { TrackpadSettings } from "../gestures";
import type { KeyboardSettings } from "../keyboardSettings";
import type { PcProfile } from "../useVolturaAirConnection";

type ThemeMode = "system" | "light" | "dark";

type SettingsDrawerProps = {
  activePc: PcProfile | null;
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
  themeMode: ThemeMode;
  trackpadSettings: TrackpadSettings;
  updateKeyboardSetting: <Key extends keyof KeyboardSettings>(key: Key, value: KeyboardSettings[Key]) => void;
  updateTrackpadSetting: <Key extends keyof TrackpadSettings>(key: Key, value: TrackpadSettings[Key]) => void;
};

export function SettingsDrawer({
  activePc,
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
  themeMode,
  trackpadSettings,
  updateKeyboardSetting,
  updateTrackpadSetting
}: SettingsDrawerProps) {
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

      <section className="settings-section">
        <h3>Connection</h3>
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
                    <small>{pc.id === activePc?.id ? "Active" : "Saved"}</small>
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
      </section>

      <section className="settings-section">
        <h3>Trackpad</h3>
        <label className="toggle-row">
          <span>Tap to click</span>
          <input type="checkbox" checked={trackpadSettings.tapToClick} onChange={(event) => updateTrackpadSetting("tapToClick", event.target.checked)} />
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

        <div className="setting-group">
          <span>Scroll direction</span>
          <div className="segmented-control">
            <button type="button" className={trackpadSettings.scrollDirection === "normal" ? "active" : ""} onClick={() => updateTrackpadSetting("scrollDirection", "normal")}>
              Normal
            </button>
            <button type="button" className={trackpadSettings.scrollDirection === "inverted" ? "active" : ""} onClick={() => updateTrackpadSetting("scrollDirection", "inverted")}>
              Inverted
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
      </section>

      <section className="settings-section">
        <h3>Keyboard</h3>
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
      </section>

      <section className="settings-section">
        <h3>Appearance</h3>
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
      </section>

      <section className="settings-section">
        <h3>App</h3>
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
      </section>
    </aside>
  );
}

function isAppleTouchDevice(): boolean {
  return /iPad|iPhone|iPod/.test(navigator.userAgent) || (navigator.platform === "MacIntel" && navigator.maxTouchPoints > 1);
}
