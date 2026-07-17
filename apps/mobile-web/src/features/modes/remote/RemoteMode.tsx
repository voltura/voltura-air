import { useEffect, useState } from "react";
import type {
  AppLaunchActionSummary,
  AudioStateMessage,
  PowerCapabilities,
  SystemPowerAction,
  SystemPowerResultMessage,
  UrlOpenCapability,
  UrlOpenResultMessage
} from "../../../protocol";
import type { RemoteSettings } from "../../../remoteSettings";
import type { AwakeControlProps } from "./PowerControlEntry";
import { RemoteMediaSection } from "./RemoteMediaSection";
import { RemoteNavigationSection } from "./RemoteNavigationSection";
import { RemoteUtilityPanel } from "./RemoteUtilityPanel";
import { RemoteVolumeSection } from "./RemoteVolumeSection";
import { getRemoteModeCopy } from "./remoteModeCopy";
import { remoteShortcutMaps, type RemoteShortcut } from "./remoteShortcuts";
import { useRemoteInteractions } from "./useRemoteInteractions";

interface RemoteModeProps {
  appLaunchActions: AppLaunchActionSummary[];
  audioState: AudioStateMessage | null;
  awakeControl?: AwakeControlProps | undefined;
  isConnected?: boolean;
  remoteSettings: RemoteSettings;
  onPointerButtonClick: (button: "left" | "right") => void;
  onPointerMove: (dx: number, dy: number) => void;
  onPowerAction: (action: SystemPowerAction) => void;
  onAppLaunch: (actionId: string) => void;
  onUrlOpen: (url: string) => string | null;
  pendingUrlOpen: boolean;
  pendingAppLaunchId: string | null;
  pendingPowerAction: SystemPowerAction | null;
  powerActionResult: SystemPowerResultMessage | null;
  powerCapabilities: PowerCapabilities | null;
  urlOpenCapability?: UrlOpenCapability | undefined;
  urlOpenResult: UrlOpenResultMessage | null;
  sendSpecial: (key: string, modifiers?: string[]) => void;
  onUtilityPanelOpenChange?: (isOpen: boolean) => void;
}

export function RemoteMode({
  appLaunchActions,
  audioState,
  awakeControl,
  isConnected = true,
  remoteSettings,
  onPointerButtonClick,
  onPointerMove,
  onPowerAction,
  onAppLaunch,
  onUrlOpen,
  pendingUrlOpen,
  pendingAppLaunchId,
  pendingPowerAction,
  powerActionResult,
  powerCapabilities,
  urlOpenCapability,
  urlOpenResult,
  sendSpecial,
  onUtilityPanelOpenChange
}: RemoteModeProps) {
  const [showUtilityPanel, setShowUtilityPanel] = useState(false);
  const modeCopy = getRemoteModeCopy(remoteSettings.mode);
  const isKodiMode = remoteSettings.mode === "kodi";
  const shortcuts = remoteShortcutMaps[remoteSettings.mode];
  const { getRepeatablePressProps, miniTrackpadProps, navigationPanelProps } = useRemoteInteractions({
    isKodiMode,
    navigationRing: remoteSettings.navigationRing,
    onPointerButtonClick,
    onPointerMove,
    sendSpecial
  });

  useEffect(() => {
    onUtilityPanelOpenChange?.(showUtilityPanel);
  }, [onUtilityPanelOpenChange, showUtilityPanel]);

  const sendShortcut = (shortcut: RemoteShortcut) => {
    if (shortcut.modifiers) {
      sendSpecial(shortcut.key, shortcut.modifiers);
    } else {
      sendSpecial(shortcut.key);
    }
  };

  const sendPrevious = () => { sendShortcut(shortcuts.previous); };
  const sendPlayPause = () => { sendShortcut(shortcuts.playPause); };
  const sendNext = () => { sendShortcut(shortcuts.next); };
  const sendSeekBackward = () => { sendShortcut(shortcuts.seekBackward); };
  const sendSeekForward = () => { sendShortcut(shortcuts.seekForward); };
  const sendVolumeDown = () => { sendShortcut(shortcuts.volumeDown); };
  const sendMute = () => { sendShortcut(shortcuts.mute); };
  const sendVolumeUp = () => { sendShortcut(shortcuts.volumeUp); };
  const sendBack = () => { sendShortcut(shortcuts.back); };
  const sendAppFullscreen = () => { sendShortcut(shortcuts.appFullscreen); };
  const sendBrowserFullscreen = () => { sendShortcut(shortcuts.browserFullscreen); };
  const sendSpace = () => { sendShortcut(shortcuts.space); };
  const sendStopPlayback = () => shortcuts.stop && sendShortcut(shortcuts.stop);
  const sendInfo = () => shortcuts.info && sendShortcut(shortcuts.info);
  const sendSubtitles = () => shortcuts.subtitles && sendShortcut(shortcuts.subtitles);
  const sendPowerMenu = () => shortcuts.powerMenu && sendShortcut(shortcuts.powerMenu);
  const utilityPanelId = "remote-utility-panel";

  return (
    <section className={`remote-mode ${showUtilityPanel ? "remote-utility-open" : ""}`} aria-label="Couch remote">
      <RemoteMediaSection
        getRepeatablePressProps={getRepeatablePressProps}
        isKodiMode={isKodiMode}
        modeCopy={modeCopy}
        onAppFullscreen={sendAppFullscreen}
        onBack={sendBack}
        onBrowserFullscreen={sendBrowserFullscreen}
        onNext={sendNext}
        onPlayPause={sendPlayPause}
        onPowerMenu={sendPowerMenu}
        onPrevious={sendPrevious}
        onSeekBackward={sendSeekBackward}
        onSeekForward={sendSeekForward}
        onSpace={sendSpace}
        onStopPlayback={sendStopPlayback}
      />
      <RemoteVolumeSection
        audioState={audioState}
        getRepeatablePressProps={getRepeatablePressProps}
        mode={remoteSettings.mode}
        modeCopy={modeCopy}
        onMute={sendMute}
        onVolumeDown={sendVolumeDown}
        onVolumeUp={sendVolumeUp}
      />
      <RemoteNavigationSection
        awakeControl={awakeControl}
        getRepeatablePressProps={getRepeatablePressProps}
        isKodiMode={isKodiMode}
        miniTrackpadProps={miniTrackpadProps}
        navigationPanelProps={navigationPanelProps}
        navigationRing={remoteSettings.navigationRing}
        onBrowserFullscreen={sendBrowserFullscreen}
        onHideUtilityPanel={() => { setShowUtilityPanel(false); }}
        onInfo={sendInfo}
        onPowerAction={onPowerAction}
        onSubtitles={sendSubtitles}
        onToggleUtilityPanel={() => { setShowUtilityPanel((current) => !current); }}
        pendingPowerAction={pendingPowerAction}
        powerActionResult={powerActionResult}
        powerCapabilities={powerCapabilities}
        sendSpecial={sendSpecial}
        showUtilityPanel={showUtilityPanel}
        utilityPanelId={utilityPanelId}
      />
      <RemoteUtilityPanel
        appLaunchActions={appLaunchActions}
        id={utilityPanelId}
        isConnected={isConnected}
        onAppLaunch={onAppLaunch}
        onUrlOpen={onUrlOpen}
        pendingAppLaunchId={pendingAppLaunchId}
        pendingUrlOpen={pendingUrlOpen}
        remoteSettings={remoteSettings}
        sendSpecial={sendSpecial}
        urlOpenCapability={urlOpenCapability}
        urlOpenResult={urlOpenResult}
      />
    </section>
  );
}
