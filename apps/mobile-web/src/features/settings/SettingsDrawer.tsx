import { useEffect, useId, useLayoutEffect, useRef, useState, type MouseEvent } from "react";
import { X } from "lucide-react";
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
  const [openSection, setOpenSection] = useState<SettingsSection | null>(null);
  const dialogRef = useRef<HTMLDialogElement>(null);
  const scrollRegionRef = useRef<HTMLDivElement>(null);
  const returnFocusRef = useRef<HTMLElement | null>(null);
  const notifyOnCloseRef = useRef(false);
  const titleId = useId();

  useEffect(() => {
    const dialog = dialogRef.current;
    if (!dialog) {
      return;
    }

    if (props.isOpen && !dialog.open) {
      returnFocusRef.current = document.activeElement instanceof HTMLElement ? document.activeElement : null;
      if (typeof dialog.showModal === "function") {
        dialog.showModal();
      } else {
        dialog.setAttribute("open", "");
      }
      dialog.focus({ preventScroll: true });
    } else if (!props.isOpen && dialog.open) {
      notifyOnCloseRef.current = false;
      if (typeof dialog.close === "function") {
        dialog.close();
      } else {
        dialog.removeAttribute("open");
        dialog.dispatchEvent(new Event("close"));
      }
    }
  }, [props.isOpen]);

  useLayoutEffect(() => {
    if (!props.isOpen || !openSection) {
      return;
    }

    const scrollRegion = scrollRegionRef.current;
    const section = scrollRegion?.querySelector<HTMLDetailsElement>(`[data-settings-section="${openSection}"]`);
    if (scrollRegion && section) {
      revealOpenedSection(scrollRegion, section);
    }
  }, [openSection, props.isOpen]);

  const toggleSection = (event: MouseEvent<HTMLElement>, section: SettingsSection) => {
    event.preventDefault();
    setOpenSection((current) => current === section ? null : section);
  };

  const closeDialog = () => {
    const dialog = dialogRef.current;
    notifyOnCloseRef.current = true;
    if (dialog && typeof dialog.close === "function") {
      dialog.close();
      return;
    }

    dialog?.removeAttribute("open");
    setOpenSection(null);
    notifyOnCloseRef.current = false;
    props.onClose();
    returnFocusRef.current?.focus();
  };

  return (
    <dialog
      ref={dialogRef}
      className="settings-drawer"
      aria-labelledby={titleId}
      aria-modal="true"
      tabIndex={-1}
      onCancel={(event) => {
        event.preventDefault();
        closeDialog();
      }}
      onClose={(event) => {
        if (event.target !== event.currentTarget) {
          return;
        }

        setOpenSection(null);
        if (notifyOnCloseRef.current) {
          notifyOnCloseRef.current = false;
          props.onClose();
        }
        returnFocusRef.current?.focus();
      }}
    >
      <button
        className="settings-drawer-light-dismiss"
        type="button"
        aria-hidden="true"
        tabIndex={-1}
        onClick={closeDialog}
      />
      <div className="settings-drawer-panel">
      <header className="drawer-header">
        <div className="drawer-title">
          <h2 id={titleId}>Menu</h2>
          <span>v{__APP_VERSION__}</span>
        </div>
        <button className="icon-button" type="button" aria-label="Close menu" onClick={closeDialog}>
          <X aria-hidden="true" />
        </button>
      </header>

      <div ref={scrollRegionRef} className="settings-drawer-scroll-region">
        <section className="drawer-group" aria-labelledby="drawer-tools-title">
          <h3 id="drawer-tools-title">Tools</h3>
          <div className="drawer-tool-list">
            {props.toolOptions.filter(({ id }) => id !== "presentation" || props.presentationAvailable).map(({ id, Icon, label }) => {
              return (
                <button key={id} type="button" onClick={() => props.onOpenTool?.(id)}>
                  <Icon aria-hidden="true" />
                  <span>{label}</span>
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
      </div>
      </div>
    </dialog>
  );
}

const assistedScrollPadding = 16;

function revealOpenedSection(scrollRegion: HTMLElement, section: HTMLDetailsElement): void {
  const summary = section.querySelector<HTMLElement>("summary");
  const body = section.querySelector<HTMLElement>(".settings-section-body");
  const firstControl = body?.querySelector<HTMLElement>("button, input, select, textarea, a[href], [tabindex]");
  const revealTarget = firstControl ?? body;
  if (!summary || !revealTarget) {
    return;
  }

  const drawerRect = scrollRegion.getBoundingClientRect();
  const summaryRect = summary.getBoundingClientRect();
  const targetRect = revealTarget.getBoundingClientRect();
  if (drawerRect.height <= 0 || summaryRect.height <= 0) {
    return;
  }

  const visibleTop = drawerRect.top + assistedScrollPadding;
  const visibleBottom = drawerRect.bottom - assistedScrollPadding;
  if (summaryRect.top < visibleTop) {
    scrollAssisted(scrollRegion, summaryRect.top - visibleTop);
    return;
  }

  const hiddenBy = targetRect.bottom - visibleBottom;
  const availableScroll = summaryRect.top - visibleTop;
  const scrollDistance = Math.min(hiddenBy, availableScroll);

  if (scrollDistance <= 0) {
    return;
  }

  scrollAssisted(scrollRegion, scrollDistance);
}

function scrollAssisted(scrollRegion: HTMLElement, scrollDistance: number): void {
  const reduceMotion = window.matchMedia?.("(prefers-reduced-motion: reduce)").matches ?? false;
  scrollRegion.scrollBy({
    top: scrollDistance,
    behavior: reduceMotion ? "auto" : "smooth"
  });
}
