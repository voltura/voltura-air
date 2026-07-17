import { useId } from "react";
import { MousePointer2 } from "lucide-react";
import { supportsHapticFeedback } from "../../hapticFeedback";
import { InfoButton } from "../../ui/overlays/InfoButton";
import type { SettingsDrawerProps } from "./SettingsDrawerTypes";

export function TrackpadSettingsSection({
  onOpenGestureDebug,
  showGestureDebug,
  trackpadSettings,
  updateTrackpadSetting
}: Pick<SettingsDrawerProps, "onOpenGestureDebug" | "showGestureDebug" | "trackpadSettings" | "updateTrackpadSetting">) {
  const hapticFeedbackId = useId();
  const hapticFeedbackSupported = supportsHapticFeedback();

  return (
    <>
      {showGestureDebug && onOpenGestureDebug && <button type="button" onClick={onOpenGestureDebug}><MousePointer2 aria-hidden="true" /><span>Open gesture debug</span></button>}
      <label className="toggle-row"><span>Tap to click</span><input type="checkbox" checked={trackpadSettings.tapToClick} onChange={(event) => { updateTrackpadSetting("tapToClick", event.target.checked); }} /></label>
      <label className="toggle-row"><span>Pointer smoothing</span><input type="checkbox" checked={trackpadSettings.pointerSmoothing} onChange={(event) => { updateTrackpadSetting("pointerSmoothing", event.target.checked); }} /></label>
      <label className="toggle-row"><span>Pointer acceleration</span><input type="checkbox" checked={trackpadSettings.pointerAcceleration} onChange={(event) => { updateTrackpadSetting("pointerAcceleration", event.target.checked); }} /></label>
      <label className="toggle-row"><span>Scroll acceleration</span><input type="checkbox" checked={trackpadSettings.scrollAcceleration} onChange={(event) => { updateTrackpadSetting("scrollAcceleration", event.target.checked); }} /></label>
      <label className="toggle-row"><span>Vertical scrolling</span><input type="checkbox" checked={trackpadSettings.verticalScroll} onChange={(event) => { updateTrackpadSetting("verticalScroll", event.target.checked); }} /></label>
      <label className="toggle-row"><span>Horizontal scrolling</span><input type="checkbox" checked={trackpadSettings.horizontalScroll} onChange={(event) => { updateTrackpadSetting("horizontalScroll", event.target.checked); }} /></label>
      <label className="toggle-row"><span>Pinch zoom</span><input type="checkbox" checked={trackpadSettings.zoomGestures} onChange={(event) => { updateTrackpadSetting("zoomGestures", event.target.checked); }} /></label>
      <label className="toggle-row"><span>Show volume control</span><input type="checkbox" checked={trackpadSettings.showVolumeControl} onChange={(event) => { updateTrackpadSetting("showVolumeControl", event.target.checked); }} /></label>

      <div className="toggle-row">
        <span className="setting-label-with-info">
          <label htmlFor={hapticFeedbackId}>Haptic feedback{hapticFeedbackSupported ? "" : " (not supported)"}</label>
          <InfoButton title="Haptic feedback" description="Vibrates on trackpad taps and click-button presses. It is only available when this browser and device expose vibration to web apps." />
        </span>
        <input id={hapticFeedbackId} type="checkbox" checked={hapticFeedbackSupported && trackpadSettings.hapticFeedback} disabled={!hapticFeedbackSupported} onChange={(event) => { updateTrackpadSetting("hapticFeedback", event.target.checked); }} />
      </div>

      <label className="toggle-row"><span>Left-handed button layout</span><input type="checkbox" checked={trackpadSettings.leftHandedButtons} onChange={(event) => { updateTrackpadSetting("leftHandedButtons", event.target.checked); }} /></label>
      <label className="toggle-row"><span>Large click buttons</span><input type="checkbox" checked={trackpadSettings.largeClickButtons} onChange={(event) => { updateTrackpadSetting("largeClickButtons", event.target.checked); }} /></label>

      <div className="setting-group">
        <span>Scroll direction</span>
        <div className="segmented-control">
          <button type="button" className={trackpadSettings.scrollDirection === "normal" ? "active" : ""} onClick={() => { updateTrackpadSetting("scrollDirection", "normal"); }}>Natural scrolling</button>
          <button type="button" className={trackpadSettings.scrollDirection === "inverted" ? "active" : ""} onClick={() => { updateTrackpadSetting("scrollDirection", "inverted"); }}>Traditional scrolling</button>
        </div>
      </div>

      <label className="setting-group">
        <span>Pointer speed</span>
        <div className="range-row">
          <input type="range" min="10" max="100" step="5" value={trackpadSettings.pointerSpeed} onChange={(event) => { updateTrackpadSetting("pointerSpeed", Number(event.target.value)); }} />
          <output>{trackpadSettings.pointerSpeed}%</output>
        </div>
      </label>
    </>
  );
}

export function KeyboardSettingsSection({ keyboardSettings, updateKeyboardSetting }: Pick<SettingsDrawerProps, "keyboardSettings" | "updateKeyboardSetting">) {
  return (
    <>
      <label className="toggle-row"><span>Show function keys</span><input type="checkbox" checked={keyboardSettings.showFunctionKeys} onChange={(event) => { updateKeyboardSetting("showFunctionKeys", event.target.checked); }} /></label>
      <label className="toggle-row"><span>Show control keys</span><input type="checkbox" checked={keyboardSettings.showControlKeys} onChange={(event) => { updateKeyboardSetting("showControlKeys", event.target.checked); }} /></label>
      <label className="toggle-row"><span>Show arrow keys</span><input type="checkbox" checked={keyboardSettings.showArrowKeys} onChange={(event) => { updateKeyboardSetting("showArrowKeys", event.target.checked); }} /></label>
      <label className="toggle-row"><span>Show sleep button</span><input type="checkbox" checked={keyboardSettings.showSleepButton} onChange={(event) => { updateKeyboardSetting("showSleepButton", event.target.checked); }} /></label>
    </>
  );
}

export function CustomPointerSettingsSection({ customPointerEnabled, setHostCustomPointer }: Pick<SettingsDrawerProps, "customPointerEnabled" | "setHostCustomPointer">) {
  const available = typeof customPointerEnabled === "boolean";
  return (
    <>
      <p className="settings-description">Turn Custom pointer on or off for this PC.</p>
      <label className="toggle-row"><span>Custom pointer</span><input type="checkbox" checked={customPointerEnabled === true} disabled={!available} onChange={(event) => setHostCustomPointer?.(event.target.checked)} /></label>
      {!available && <p className="pairing-inline-status">Connect to a host that supports Custom pointer.</p>}
    </>
  );
}

export function RemoteSettingsSection({ remoteSettings, supportsRemoteLaunch, updateRemoteSetting }: Pick<SettingsDrawerProps, "remoteSettings" | "supportsRemoteLaunch" | "updateRemoteSetting">) {
  return (
    <>
      <label className="toggle-row"><span>Navigation ring</span><input type="checkbox" checked={remoteSettings.navigationRing} onChange={(event) => { updateRemoteSetting("navigationRing", event.target.checked); }} /></label>
      <div className="setting-group">
        <span>Remote mode</span>
        <div className="segmented-control three" aria-label="Remote mode">
          <button type="button" className={remoteSettings.mode === "standard" ? "active" : ""} onClick={() => { updateRemoteSetting("mode", "standard"); }}>Standard</button>
          <button type="button" className={remoteSettings.mode === "youtube" ? "active" : ""} onClick={() => { updateRemoteSetting("mode", "youtube"); }}>YouTube</button>
          <button type="button" className={remoteSettings.mode === "kodi" ? "active" : ""} onClick={() => { updateRemoteSetting("mode", "kodi"); }}>Kodi</button>
        </div>
      </div>
      <div className="setting-group">
        <span>Extra helper buttons</span>
        <label className="toggle-row"><span>Window actions</span><input type="checkbox" checked={remoteSettings.showWindowHelpers} onChange={(event) => { updateRemoteSetting("showWindowHelpers", event.target.checked); }} /></label>
        <label className="toggle-row"><span>Browser tabs and reload</span><input type="checkbox" checked={remoteSettings.showBrowserHelpers} onChange={(event) => { updateRemoteSetting("showBrowserHelpers", event.target.checked); }} /></label>
      </div>
      {supportsRemoteLaunch && (
        <>
          <label className="toggle-row"><span>Open YouTube from Remote mode</span><input type="checkbox" checked={remoteSettings.openYoutube} onChange={(event) => { updateRemoteSetting("openYoutube", event.target.checked); }} /></label>
          <label className="toggle-row"><span>Start Kodi from Remote mode</span><input type="checkbox" checked={remoteSettings.startKodi} onChange={(event) => { updateRemoteSetting("startKodi", event.target.checked); }} /></label>
        </>
      )}
    </>
  );
}
