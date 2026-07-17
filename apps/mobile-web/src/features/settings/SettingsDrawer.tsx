import { useLayoutEffect, useRef, useState, type MouseEvent } from "react";
import { X } from "lucide-react";
import { getAvailableToolModeIds, toolModeDefinitions, type ToolAppTab } from "../../appModeTabs";
import {
  CustomPointerSettingsSection,
  KeyboardSettingsSection,
  RemoteSettingsSection,
  TrackpadSettingsSection
} from "./ControlSettingsSections";
import { AppSettingsSection, AppearanceSettingsSection } from "./AppSettingsSections";
import { ConnectionSettingsSection } from "./ConnectionSettingsSection";
import { SettingsSectionDetails } from "./SettingsSectionDetails";
import type { SettingsDrawerProps, SettingsSection } from "./SettingsDrawerTypes";
import { SplitModeSettings } from "./SplitModeSettings";

export function SettingsDrawer(props: SettingsDrawerProps) {
  const [drawerState, setDrawerState] = useState<{ isOpen: boolean; section: SettingsSection | null }>({
    isOpen: props.isOpen,
    section: null
  });
  if (drawerState.isOpen !== props.isOpen) {
    setDrawerState({ isOpen: props.isOpen, section: null });
  }
  const openSection = drawerState.section;
  const drawerRef = useRef<HTMLElement>(null);

  useLayoutEffect(() => {
    if (!props.isOpen || !openSection) {
      return;
    }

    const drawer = drawerRef.current;
    const section = drawer?.querySelector<HTMLDetailsElement>(`[data-settings-section="${openSection}"]`);
    if (drawer && section) {
      revealOpenedSection(drawer, section);
    }
  }, [openSection, props.isOpen]);

  const toggleSection = (event: MouseEvent<HTMLElement>, section: SettingsSection) => {
    event.preventDefault();
    setDrawerState((current) => ({ ...current, section: current.section === section ? null : section }));
  };

  return (
    <aside ref={drawerRef} className={`settings-drawer ${props.isOpen ? "open" : ""}`} aria-hidden={!props.isOpen}>
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
          {getAvailableToolModeIds(props.presentationAvailable).map((toolId: ToolAppTab) => {
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

      <SettingsSectionDetails section="app" label="App" isOpen={openSection === "app"} onToggle={toggleSection}>
        <AppSettingsSection
          appSettings={props.appSettings}
          installApp={props.installApp}
          installPrompt={props.installPrompt}
          isInstalled={props.isInstalled}
          refreshInstalledApp={props.refreshInstalledApp}
          refreshMessage={props.refreshMessage}
          presentationAvailable={props.presentationAvailable}
          updateAppSetting={props.updateAppSetting}
        />
      </SettingsSectionDetails>

      <SettingsSectionDetails section="appearance" label="Appearance" isOpen={openSection === "appearance"} onToggle={toggleSection}>
        <AppearanceSettingsSection setThemeMode={props.setThemeMode} themeMode={props.themeMode} />
      </SettingsSectionDetails>

      <SettingsSectionDetails section="split" label="Split mode" isOpen={openSection === "split"} onToggle={toggleSection}>
        <SplitModeSettings settings={props.trackpadSettings} updateSetting={props.updateTrackpadSetting} />
      </SettingsSectionDetails>

      <SettingsSectionDetails section="custom-pointer" label="Custom pointer" isOpen={openSection === "custom-pointer"} onToggle={toggleSection}>
        <CustomPointerSettingsSection customPointerEnabled={props.customPointerEnabled} setHostCustomPointer={props.setHostCustomPointer} />
      </SettingsSectionDetails>
    </aside>
  );
}

const assistedScrollPadding = 16;

function revealOpenedSection(drawer: HTMLElement, section: HTMLDetailsElement): void {
  const summary = section.querySelector<HTMLElement>("summary");
  const body = section.querySelector<HTMLElement>(".settings-section-body");
  const firstControl = body?.querySelector<HTMLElement>("button, input, select, textarea, a[href], [tabindex]");
  const revealTarget = firstControl ?? body;
  if (!summary || !revealTarget) {
    return;
  }

  const drawerRect = drawer.getBoundingClientRect();
  const summaryRect = summary.getBoundingClientRect();
  const targetRect = revealTarget.getBoundingClientRect();
  const hiddenBy = targetRect.bottom - (drawerRect.bottom - assistedScrollPadding);
  const availableScroll = summaryRect.top - (drawerRect.top + assistedScrollPadding);
  const scrollDistance = Math.min(hiddenBy, availableScroll);

  if (scrollDistance <= 0) {
    return;
  }

  const reduceMotion = window.matchMedia?.("(prefers-reduced-motion: reduce)").matches ?? false;
  drawer.scrollBy({
    top: scrollDistance,
    behavior: reduceMotion ? "auto" : "smooth"
  });
}
