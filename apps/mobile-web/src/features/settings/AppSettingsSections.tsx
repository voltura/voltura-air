import { Download, RefreshCw } from "lucide-react";
import { lazy, Suspense, useId } from "react";
import { getEffectiveFourthMode } from "../../foundation/settings/appSettings";
import type { StorageProtectionState } from "../../foundation/pwa/usePersistentStorage";
import { InfoButton } from "../../ui/overlays/InfoButton";
import type { SettingsDrawerProps } from "./SettingsDrawerTypes";

const AccentColorSetting = lazy(() =>
  import("./AccentColorSetting").then((module) => ({
    default: module.AccentColorSetting,
  })),
);

export function AppearanceSettingsSection({
  accentColor,
  accentColorOverridden = false,
  accentColorSupported = false,
  controlDepth = true,
  setHostAccentColor,
  setHostControlDepth,
  setHostShowModeButtons,
  setThemeMode,
  showModeButtons = true,
  themeMode,
}: Pick<
  SettingsDrawerProps,
  | "controlDepth"
  | "accentColor"
  | "accentColorOverridden"
  | "accentColorSupported"
  | "setHostAccentColor"
  | "setHostControlDepth"
  | "setHostShowModeButtons"
  | "setThemeMode"
  | "showModeButtons"
  | "themeMode"
>) {
  return (
    <div className="setting-group">
      <span>Theme</span>
      <div className="segmented-control three">
        <button
          type="button"
          className={themeMode === "system" ? "active" : ""}
          onClick={() => {
            setThemeMode("system");
          }}
        >
          System
        </button>
        <button
          type="button"
          className={themeMode === "light" ? "active" : ""}
          onClick={() => {
            setThemeMode("light");
          }}
        >
          Light
        </button>
        <button
          type="button"
          className={themeMode === "dark" ? "active" : ""}
          onClick={() => {
            setThemeMode("dark");
          }}
        >
          Dark
        </button>
      </div>
      {accentColorSupported && (
        <Suspense fallback={null}>
          <AccentColorSetting
            accentColor={accentColor}
            accentColorOverridden={accentColorOverridden}
            setHostAccentColor={setHostAccentColor}
          />
        </Suspense>
      )}
      <label className="toggle-row">
        <span>Show mode buttons</span>
        <input
          type="checkbox"
          checked={showModeButtons}
          onChange={(event) => {
            setHostShowModeButtons?.(event.target.checked);
          }}
        />
      </label>
      <label className="toggle-row">
        <span>3D effect on controls</span>
        <input
          type="checkbox"
          checked={controlDepth}
          onChange={(event) => {
            setHostControlDepth?.(event.target.checked);
          }}
        />
      </label>
    </div>
  );
}

export function AppSettingsSection({
  appSettings,
  keepDeviceScreenOn,
  setKeepDeviceScreenOn,
  screenWakeLockSupported,
  protectSavedData,
  storageProtection,
  installApp,
  installPrompt,
  isInstalled,
  presentationAvailable,
  filesAvailable,
  terminalAvailable,
  refreshInstalledApp,
  refreshMessage,
  updateAppSetting,
}: Pick<
  SettingsDrawerProps,
  | "appSettings"
  | "keepDeviceScreenOn"
  | "setKeepDeviceScreenOn"
  | "screenWakeLockSupported"
  | "protectSavedData"
  | "storageProtection"
  | "filesAvailable"
  | "terminalAvailable"
  | "installApp"
  | "installPrompt"
  | "isInstalled"
  | "presentationAvailable"
  | "refreshInstalledApp"
  | "refreshMessage"
  | "updateAppSetting"
>) {
  const keepScreenOnId = useId();

  return (
    <div className="install-card">
      <label className="setting-group">
        <span>Fourth mode button</span>
        <select
          className="text-input fourth-mode-select"
          value={getEffectiveFourthMode(
            appSettings.fourthMode,
            presentationAvailable,
            filesAvailable,
            terminalAvailable,
          )}
          onChange={(event) => {
            updateAppSetting(
              "fourthMode",
              event.target.value === "presentation" ||
                event.target.value === "text-transfer" ||
                event.target.value === "clipboard-read" ||
                event.target.value === "files" ||
                event.target.value === "terminal"
                ? event.target.value
                : "dictation",
            );
          }}
        >
          {presentationAvailable && <option value="presentation">Presentation</option>}
          <option value="dictation">Dictation</option>
          <option value="text-transfer">Send text to PC</option>
          <option value="clipboard-read">Get text from PC</option>
          {filesAvailable && <option value="files">Files</option>}
          {terminalAvailable && <option value="terminal">Terminal</option>}
        </select>
      </label>
      {!isInstalled && (
        <>
          <div className="install-title">
            <Download aria-hidden="true" />
            <span className="setting-label-with-info">
              <span>Home screen app</span>
              <InfoButton
                title="Home screen app"
                size="detailed"
                description="On iPhone or iPad, tap Share (or open the page menu, then Share), choose Add to Home Screen, turn on Open as Web App if shown, and tap Add. In other browsers, open the browser menu and choose Add to Home screen or Install app if available. Use Refresh app below if Voltura Air looks stale."
              />
            </span>
          </div>
          {installPrompt ? (
            <>
              <button
                type="button"
                onClick={() => {
                  void installApp();
                }}
              >
                <Download aria-hidden="true" />
                <span>Install app</span>
              </button>
            </>
          ) : null}
        </>
      )}
      {refreshMessage !== "Reload from the PC if the home screen app looks stale." && (
        <p role="status">{refreshMessage}</p>
      )}
      <div className="toggle-row">
        <span className="setting-label-with-info">
          <label htmlFor={keepScreenOnId}>Keep this device’s screen on</label>
          <InfoButton
            title="Keep this device’s screen on"
            description="When enabled, Voltura Air asks your browser to keep this device’s screen on while connected and visible. The device may still dim or lock the screen. This setting applies across your saved PCs."
          />
        </span>
        <input
          id={keepScreenOnId}
          type="checkbox"
          checked={keepDeviceScreenOn}
          disabled={!screenWakeLockSupported}
          onChange={(event) => setKeepDeviceScreenOn(event.target.checked)}
        />
      </div>
      <div className="setting-group">
        <span className="setting-label-with-info">
          <span>Keep pairing and settings</span>
          <InfoButton
            title="Keep pairing and settings"
            size="detailed"
            description="Ask this browser to protect saved PCs, pairing, preferences, and text snippets from automatic cleanup. Your browser decides whether to allow it. This is not a backup; clearing website data still removes them."
          />
        </span>
        {storageProtection !== "enabled" && (
          <button
            type="button"
            disabled={
              storageProtection === "checking" ||
              storageProtection === "requesting" ||
              storageProtection === "unavailable"
            }
            aria-describedby="saved-data-status"
            onClick={() => void protectSavedData()}
          >
            {storageProtection === "requesting" ? "Requesting…" : "Protect saved data"}
          </button>
        )}
        <p id="saved-data-status" role="status">
          {storageProtectionMessages[storageProtection]}
        </p>
      </div>
      <label className="toggle-row">
        <span>Auto refresh</span>
        <input
          type="checkbox"
          checked={appSettings.autoRefresh}
          onChange={(event) => {
            updateAppSetting("autoRefresh", event.target.checked);
          }}
        />
      </label>
      <button
        type="button"
        onClick={() => {
          void refreshInstalledApp();
        }}
      >
        <RefreshCw aria-hidden="true" />
        <span>Refresh app</span>
      </button>
    </div>
  );
}

const storageProtectionMessages: Record<StorageProtectionState, string> = {
  checking: "Checking…",
  available: "",
  requesting: "Waiting for the browser…",
  enabled: "Browser protection enabled.",
  declined: "Browser declined. You can try again.",
  unavailable: "Unavailable in this browser or connection.",
  failed: "Could not enable protection. You can try again.",
};
