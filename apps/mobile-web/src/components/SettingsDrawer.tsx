import { useEffect, useState, type MouseEvent } from "react";
import { X } from "lucide-react";
import {
  AppSettingsSection,
  AppearanceSettingsSection,
  ConnectionSettingsSection,
  KeyboardSettingsSection,
  RemoteSettingsSection,
  SettingsSectionDetails,
  TrackpadSettingsSection
} from "./SettingsDrawerSections";
import type { SettingsDrawerProps, SettingsSection } from "./SettingsDrawerTypes";

export function SettingsDrawer(props: SettingsDrawerProps) {
  const [openSection, setOpenSection] = useState<SettingsSection | null>(null);

  useEffect(() => {
    if (!props.isOpen) {
      setOpenSection(null);
    }
  }, [props.isOpen]);

  const toggleSection = (event: MouseEvent<HTMLElement>, section: SettingsSection) => {
    event.preventDefault();
    setOpenSection((current) => (current === section ? null : section));
  };

  return (
    <aside className={`settings-drawer ${props.isOpen ? "open" : ""}`} aria-hidden={!props.isOpen}>
      <header className="drawer-header">
        <div className="drawer-title">
          <h2>Settings</h2>
          <span>v{__APP_VERSION__}</span>
        </div>
        <button className="icon-button" type="button" aria-label="Close settings" onClick={props.onClose}>
          <X aria-hidden="true" />
        </button>
      </header>

      <SettingsSectionDetails section="connection" label="Connection" isOpen={openSection === "connection"} onToggle={toggleSection}>
        <ConnectionSettingsSection
          activePc={props.activePc}
          deviceName={props.deviceName}
          diagnostics={props.diagnostics}
          disconnectActivePc={props.disconnectActivePc}
          forgetPc={props.forgetPc}
          onManualHostSubmit={props.onManualHostSubmit}
          onPairingQrSelected={props.onPairingQrSelected}
          pairedPcs={props.pairedPcs}
          pairingQrInputRef={props.pairingQrInputRef}
          pairingScanMessage={props.pairingScanMessage}
          renameDevice={props.renameDevice}
          renamePc={props.renamePc}
          scanPairingQr={props.scanPairingQr}
          selectPc={props.selectPc}
        />
      </SettingsSectionDetails>

      <SettingsSectionDetails section="trackpad" label="Trackpad" isOpen={openSection === "trackpad"} onToggle={toggleSection}>
        <TrackpadSettingsSection
          onOpenGestureDebug={props.onOpenGestureDebug}
          showGestureDebug={props.showGestureDebug}
          trackpadSettings={props.trackpadSettings}
          updateTrackpadSetting={props.updateTrackpadSetting}
        />
      </SettingsSectionDetails>

      <SettingsSectionDetails section="keyboard" label="Keyboard" isOpen={openSection === "keyboard"} onToggle={toggleSection}>
        <KeyboardSettingsSection keyboardSettings={props.keyboardSettings} updateKeyboardSetting={props.updateKeyboardSetting} />
      </SettingsSectionDetails>

      <SettingsSectionDetails section="remote" label="Remote" isOpen={openSection === "remote"} onToggle={toggleSection}>
        <RemoteSettingsSection
          remoteSettings={props.remoteSettings}
          supportsRemoteLaunch={props.supportsRemoteLaunch}
          updateRemoteSetting={props.updateRemoteSetting}
        />
      </SettingsSectionDetails>

      <SettingsSectionDetails section="appearance" label="Appearance" isOpen={openSection === "appearance"} onToggle={toggleSection}>
        <AppearanceSettingsSection setThemeMode={props.setThemeMode} themeMode={props.themeMode} />
      </SettingsSectionDetails>

      <SettingsSectionDetails section="app" label="App" isOpen={openSection === "app"} onToggle={toggleSection}>
        <AppSettingsSection
          appSettings={props.appSettings}
          installApp={props.installApp}
          installPrompt={props.installPrompt}
          isInstalled={props.isInstalled}
          refreshInstalledApp={props.refreshInstalledApp}
          refreshMessage={props.refreshMessage}
          updateAppSetting={props.updateAppSetting}
        />
      </SettingsSectionDetails>
    </aside>
  );
}
