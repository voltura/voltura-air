import type { HTMLAttributes } from "react";
import {
  ArrowDown,
  ArrowLeft,
  ArrowRight,
  ArrowUp,
  Captions,
  CornerDownLeft,
  Info,
  Maximize2,
  Menu,
  Volume2,
} from "lucide-react";
import type {
  PowerCapabilities,
  SystemPowerAction,
  SystemPowerResultMessage,
} from "../../../foundation/protocol/messages";
import { PowerControlEntry, type AwakeControlProps } from "./PowerControlEntry";
import { RemoteButton, type RepeatablePressProps } from "./RemoteButton";

interface RemoteNavigationSectionProps {
  awakeControl?: AwakeControlProps | undefined;
  getRepeatablePressProps: (action: () => void) => RepeatablePressProps;
  isKodiMode: boolean;
  miniTrackpadProps: HTMLAttributes<HTMLDivElement>;
  navigationPanelProps: HTMLAttributes<HTMLDivElement>;
  navigationRing: boolean;
  onAudioTrack: () => void;
  onBrowserFullscreen: () => void;
  onHideUtilityPanel: () => void;
  onInfo: () => void;
  onMenu: () => void;
  onPowerAction: (action: SystemPowerAction) => void;
  onSubtitles: () => void;
  onToggleUtilityPanel: () => void;
  onUpOneLevel: () => void;
  pendingPowerAction: SystemPowerAction | null;
  powerActionResult: SystemPowerResultMessage | null;
  powerCapabilities: PowerCapabilities | null;
  sendSpecial: (key: string, modifiers?: string[]) => void;
  showUtilityPanel: boolean;
  utilityPanelId: string;
}

export function RemoteNavigationSection({
  awakeControl,
  getRepeatablePressProps,
  isKodiMode,
  miniTrackpadProps,
  navigationPanelProps,
  navigationRing,
  onAudioTrack,
  onBrowserFullscreen,
  onHideUtilityPanel,
  onInfo,
  onMenu,
  onPowerAction,
  onSubtitles,
  onToggleUtilityPanel,
  onUpOneLevel,
  pendingPowerAction,
  powerActionResult,
  powerCapabilities,
  sendSpecial,
  showUtilityPanel,
  utilityPanelId,
}: RemoteNavigationSectionProps) {
  const navigationControl = navigationRing ? (
    <div className="remote-navigation-ring" aria-label="Navigation ring">
      <button
        type="button"
        className="remote-ring-zone remote-ring-up"
        aria-label="D-pad up"
        {...getRepeatablePressProps(() => {
          sendSpecial("ArrowUp");
        })}
      >
        <ArrowUp aria-hidden="true" />
      </button>
      <button
        type="button"
        className="remote-ring-zone remote-ring-left"
        aria-label="D-pad left"
        {...getRepeatablePressProps(() => {
          sendSpecial("ArrowLeft");
        })}
      >
        <ArrowLeft aria-hidden="true" />
      </button>
      <button
        type="button"
        className="remote-ring-zone remote-ring-right"
        aria-label="D-pad right"
        {...getRepeatablePressProps(() => {
          sendSpecial("ArrowRight");
        })}
      >
        <ArrowRight aria-hidden="true" />
      </button>
      <button
        type="button"
        className="remote-ring-zone remote-ring-down"
        aria-label="D-pad down"
        {...getRepeatablePressProps(() => {
          sendSpecial("ArrowDown");
        })}
      >
        <ArrowDown aria-hidden="true" />
      </button>
      <div
        role="button"
        tabIndex={0}
        className="remote-mini-trackpad"
        aria-label="Mini trackpad"
        {...miniTrackpadProps}
      >
        <span aria-hidden="true" />
      </div>
    </div>
  ) : (
    <div className="remote-dpad" aria-label="Directional pad">
      <button
        type="button"
        className="remote-dpad-up"
        aria-label="D-pad up"
        {...getRepeatablePressProps(() => {
          sendSpecial("ArrowUp");
        })}
      >
        <ArrowUp aria-hidden="true" />
      </button>
      <button
        type="button"
        className="remote-dpad-left"
        aria-label="D-pad left"
        {...getRepeatablePressProps(() => {
          sendSpecial("ArrowLeft");
        })}
      >
        <ArrowLeft aria-hidden="true" />
      </button>
      <button
        type="button"
        className="remote-dpad-ok"
        onClick={() => {
          sendSpecial("Enter");
        }}
      >
        OK
      </button>
      <button
        type="button"
        className="remote-dpad-right"
        aria-label="D-pad right"
        {...getRepeatablePressProps(() => {
          sendSpecial("ArrowRight");
        })}
      >
        <ArrowRight aria-hidden="true" />
      </button>
      <button
        type="button"
        className="remote-dpad-down"
        aria-label="D-pad down"
        {...getRepeatablePressProps(() => {
          sendSpecial("ArrowDown");
        })}
      >
        <ArrowDown aria-hidden="true" />
      </button>
    </div>
  );

  return (
    <div
      className={`remote-section remote-navigation-section${isKodiMode ? " remote-navigation-section-kodi" : ""}`}
      {...navigationPanelProps}
    >
      <div className="remote-section-title">
        <span>Navigation</span>
        <small>{navigationRing ? "Ring and mini-trackpad." : "D-pad menu navigation."}</small>
      </div>
      {isKodiMode && (
        <>
          <div className="remote-kodi-action-rail remote-kodi-action-rail-left">
            <RemoteButton label="Up one level" className="remote-nav-action" onClick={onUpOneLevel}>
              <CornerDownLeft aria-hidden="true" />
            </RemoteButton>
            <RemoteButton
              label="Menu or player controls"
              className="remote-nav-action"
              onClick={onMenu}
            >
              <Menu aria-hidden="true" />
            </RemoteButton>
            <RemoteButton
              label="Info"
              className="remote-nav-action remote-nav-action-info"
              onClick={onInfo}
            >
              <Info aria-hidden="true" />
            </RemoteButton>
          </div>
          <div className="remote-kodi-action-rail remote-kodi-action-rail-right">
            <RemoteButton
              label="Toggle subtitles"
              className="remote-nav-action remote-nav-action-subtitles"
              onClick={onSubtitles}
            >
              <Captions aria-hidden="true" />
            </RemoteButton>
            <RemoteButton label="Audio track" className="remote-nav-action" onClick={onAudioTrack}>
              <Volume2 aria-hidden="true" />
            </RemoteButton>
            <RemoteButton
              label="Toggle fullscreen or windowed"
              className="remote-nav-action remote-nav-action-fullscreen"
              onClick={onBrowserFullscreen}
            >
              <Maximize2 aria-hidden="true" />
            </RemoteButton>
          </div>
        </>
      )}
      {navigationControl}
      <PowerControlEntry
        {...awakeControl}
        capabilities={powerCapabilities}
        onAction={onPowerAction}
        onOpen={onHideUtilityPanel}
        pendingAction={pendingPowerAction}
        result={powerActionResult}
      />
      <button
        type="button"
        className="remote-corner-action remote-floating-fn remote-navigation-main"
        aria-label={showUtilityPanel ? "Main" : "Fn"}
        aria-controls={utilityPanelId}
        aria-expanded={showUtilityPanel}
        onClick={onToggleUtilityPanel}
      >
        <span className="remote-functions-icon" aria-hidden="true" />
        <span className="remote-corner-action-label">{showUtilityPanel ? "Main" : "Fn"}</span>
      </button>
    </div>
  );
}
