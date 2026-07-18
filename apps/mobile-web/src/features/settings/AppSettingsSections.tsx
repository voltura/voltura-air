import { Download, RefreshCw } from "lucide-react";
import { getEffectiveFourthMode } from "../../foundation/settings/appSettings";
import type { SettingsDrawerProps } from "./SettingsDrawerTypes";

export function AppearanceSettingsSection({ setThemeMode, themeMode }: Pick<SettingsDrawerProps, "setThemeMode" | "themeMode">) {
  return (
    <div className="setting-group">
      <span>Theme</span>
      <div className="segmented-control three">
        <button type="button" className={themeMode === "system" ? "active" : ""} onClick={() => { setThemeMode("system"); }}>System</button>
        <button type="button" className={themeMode === "light" ? "active" : ""} onClick={() => { setThemeMode("light"); }}>Light</button>
        <button type="button" className={themeMode === "dark" ? "active" : ""} onClick={() => { setThemeMode("dark"); }}>Dark</button>
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
        <select className="text-input fourth-mode-select" value={getEffectiveFourthMode(appSettings.fourthMode, presentationAvailable)} onChange={(event) => { updateAppSetting("fourthMode", event.target.value === "presentation" || event.target.value === "text-transfer" || event.target.value === "clipboard-read" ? event.target.value : "dictation"); }}>
          {presentationAvailable && <option value="presentation">Presentation</option>}
          <option value="dictation">Dictation</option>
          <option value="text-transfer">Send text to PC</option>
          <option value="clipboard-read">Get text from PC</option>
        </select>
      </label>
      {!isInstalled && (
        <>
          <div className="install-title"><Download aria-hidden="true" /><span>Home screen app</span></div>
          {installPrompt ? (
            <>
              <p>Add Voltura Air to this device for a normal app icon and faster launching.</p>
              <button type="button" onClick={() => { void installApp(); }}><Download aria-hidden="true" /><span>Install app</span></button>
            </>
          ) : isAppleTouchDevice() ? (
            <ol className="install-steps"><li>Tap Share.</li><li>Tap Add to Home Screen.</li><li>Tap Add.</li></ol>
          ) : (
            <ol className="install-steps"><li>Open the browser menu.</li><li>Choose Add to Home screen or Install app.</li><li>Confirm the shortcut.</li></ol>
          )}
        </>
      )}
      {!isInstalled && <p>{refreshMessage}</p>}
      <label className="toggle-row"><span>Auto refresh</span><input type="checkbox" checked={appSettings.autoRefresh} onChange={(event) => { updateAppSetting("autoRefresh", event.target.checked); }} /></label>
      <button type="button" onClick={() => { void refreshInstalledApp(); }}><RefreshCw aria-hidden="true" /><span>Refresh app</span></button>
    </div>
  );
}

function isAppleTouchDevice(): boolean {
  return /iPad|iPhone|iPod/.test(navigator.userAgent) || (navigator.platform === "MacIntel" && navigator.maxTouchPoints > 1);
}
