import { useEffect, useState, type MouseEvent } from "react";
import { X } from "lucide-react";
import { toolModeDefinitions, type ToolAppTab } from "../appModeTabs";
import {
  AppSettingsSection,
  AppearanceSettingsSection,
  ConnectionSettingsSection,
  CustomPointerSettingsSection,
  KeyboardSettingsSection,
  RemoteSettingsSection,
  SettingsSectionDetails,
  TrackpadSettingsSection
} from "./SettingsDrawerSections";
import type { SettingsDrawerProps, SettingsSection } from "./SettingsDrawerTypes";
import { SplitModeSettings } from "./SplitModeSettings";

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
          <h2>Menu</h2>
          <span>v{__APP_VERSION__}</span>
        </div>
        <button className="icon-button" type="button" aria-label="Close menu" onClick={props.onClose}>
          <X aria-hidden="true" />
        </button>
      </header>

      <section className="drawer-group" aria-labelledby="drawer-tools-title">
        <h3 id="drawer-tools-title">Tools</h3>
        <div className="drawer-tool-list">
          {(["dictation", "text-transfer"] satisfies ToolAppTab[]).map((toolId) => {
            const { Icon, ariaLabel } = toolModeDefinitions[toolId];
            return (
              <button key={toolId} type="button" onClick={() => props.onOpenTool?.(toolId)}>
                <Icon aria-hidden="true" />
                <span>{ariaLabel}</span>
              </button>
            );
          })}
        </div>
      </section>

      <h3 className="drawer-settings-title">Settings</h3>

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

      <SettingsSectionDetails section="custom-pointer" label="Custom pointer" isOpen={openSection === "custom-pointer"} onToggle={toggleSection}>
        <CustomPointerSettingsSection customPointerEnabled={props.customPointerEnabled} setHostCustomPointer={props.setHostCustomPointer} />
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

      <SettingsSectionDetails section="split" label="Split mode" isOpen={openSection === "split"} onToggle={toggleSection}>
        <SplitModeSettings settings={props.trackpadSettings} updateSetting={props.updateTrackpadSetting} />
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
