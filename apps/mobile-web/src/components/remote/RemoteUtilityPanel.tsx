import { useEffect, useId, useRef, useState, type PointerEvent } from "react";
import { ArrowLeft, ArrowRight, CornerDownLeft, Monitor, Plus, RefreshCw, RotateCcw, Search, SquareX } from "lucide-react";
import type { RemoteSettings } from "../../remoteSettings";
import { RemoteButton, type RepeatablePressProps } from "./RemoteButton";

const switcherLongPressMs = 400;
const switcherSlideStepPx = 44;

type SwitcherPointer = {
  id: number;
  currentX: number;
  stepX: number;
  active: boolean;
};

type RemoteUtilityPanelProps = {
  id: string;
  isOpen: boolean;
  onClose: () => void;
  remoteSettings: RemoteSettings;
  sendSpecial: (key: string, modifiers?: string[]) => void;
};

export function RemoteUtilityPanel({ id, isOpen, onClose, remoteSettings, sendSpecial }: RemoteUtilityPanelProps) {
  const [isSwitcherActive, setIsSwitcherActive] = useState(false);
  const switcherHintId = useId();
  const switcherPointerRef = useRef<SwitcherPointer | null>(null);
  const switcherTimerRef = useRef<number | null>(null);
  const ignoreSwitcherClickRef = useRef(false);
  const sendSpecialRef = useRef(sendSpecial);
  sendSpecialRef.current = sendSpecial;

  const clearSwitcherTimer = () => {
    if (switcherTimerRef.current !== null) {
      window.clearTimeout(switcherTimerRef.current);
      switcherTimerRef.current = null;
    }
  };

  const finishSwitcher = (commit: boolean) => {
    clearSwitcherTimer();
    const pointer = switcherPointerRef.current;
    switcherPointerRef.current = null;
    if (!pointer?.active) {
      setIsSwitcherActive(false);
      return;
    }

    sendSpecialRef.current(commit ? "Enter" : "Escape");
    setIsSwitcherActive(false);
  };

  useEffect(() => {
    const cancelHiddenSwitcher = () => {
      if (document.visibilityState === "hidden") {
        finishSwitcher(false);
      }
    };

    document.addEventListener("visibilitychange", cancelHiddenSwitcher);
    return () => {
      document.removeEventListener("visibilitychange", cancelHiddenSwitcher);
      clearSwitcherTimer();
      if (switcherPointerRef.current?.active) {
        sendSpecialRef.current("Escape");
      }
      switcherPointerRef.current = null;
    };
  }, []);

  const pressSwitcher = (event: PointerEvent<HTMLButtonElement>) => {
    if (event.button !== 0) {
      return;
    }

    event.preventDefault();
    ignoreSwitcherClickRef.current = true;
    event.currentTarget.setPointerCapture?.(event.pointerId);
    clearSwitcherTimer();
    switcherPointerRef.current = {
      id: event.pointerId,
      currentX: event.clientX,
      stepX: event.clientX,
      active: false
    };
    switcherTimerRef.current = window.setTimeout(() => {
      const pointer = switcherPointerRef.current;
      if (!pointer) {
        return;
      }

      pointer.active = true;
      pointer.stepX = pointer.currentX;
      switcherTimerRef.current = null;
      sendSpecialRef.current("Tab", ["Control", "Alt"]);
      setIsSwitcherActive(true);
    }, switcherLongPressMs);
  };

  const moveSwitcher = (event: PointerEvent<HTMLButtonElement>) => {
    const pointer = switcherPointerRef.current;
    if (!pointer || pointer.id !== event.pointerId) {
      return;
    }

    pointer.currentX = event.clientX;
    if (!pointer.active) {
      return;
    }

    event.preventDefault();
    const steps = Math.trunc((event.clientX - pointer.stepX) / switcherSlideStepPx);
    if (steps === 0) {
      return;
    }

    pointer.stepX += steps * switcherSlideStepPx;
    const direction = steps > 0 ? 1 : -1;
    for (let index = 0; index < Math.abs(steps); index += 1) {
      if (direction > 0) {
        sendSpecialRef.current("Tab");
      } else {
        sendSpecialRef.current("Tab", ["Shift"]);
      }
    }
  };

  const releaseSwitcher = (event: PointerEvent<HTMLButtonElement>) => {
    const pointer = switcherPointerRef.current;
    if (!pointer || pointer.id !== event.pointerId) {
      return;
    }

    event.preventDefault();
    if (pointer.active) {
      finishSwitcher(true);
    } else {
      clearSwitcherTimer();
      switcherPointerRef.current = null;
      sendSpecialRef.current("Tab", ["Alt"]);
    }
  };

  const cancelSwitcher = (event: PointerEvent<HTMLButtonElement>) => {
    if (switcherPointerRef.current?.id !== event.pointerId) {
      return;
    }

    finishSwitcher(false);
  };

  const switcherPressProps: RepeatablePressProps = {
    onPointerDown: pressSwitcher,
    onPointerMove: moveSwitcher,
    onPointerUp: releaseSwitcher,
    onPointerCancel: cancelSwitcher,
    onClick: () => {
      if (ignoreSwitcherClickRef.current) {
        ignoreSwitcherClickRef.current = false;
        return;
      }

      sendSpecial("Tab", ["Alt"]);
    }
  };

  return (
    <div id={id} className="remote-section remote-utility-section">
      <div className="remote-section-title">
        <span>Windows</span>
        <small>Fast helper keys for couch use.</small>
      </div>
      <div className="remote-utility-grid" aria-label="Windows helper controls">
        <RemoteButton label="Start or search" onClick={() => sendSpecial("Win")}>
          <Search aria-hidden="true" />
          <span>Start</span>
        </RemoteButton>
        <RemoteButton
          label="Switch app"
          className={isSwitcherActive ? "remote-switch-app active" : "remote-switch-app"}
          title="Tap for the previous app. Hold and slide to choose an app."
          pressProps={switcherPressProps}
        >
          <span>Switch app</span>
        </RemoteButton>
        <RemoteButton label="Task view" title="Open Windows Task View" onClick={() => sendSpecial("Tab", ["Win"])}>
          <span>Task view</span>
        </RemoteButton>
        {remoteSettings.showWindowHelpers && (
          <>
            <RemoteButton label="Show desktop" onClick={() => sendSpecial("D", ["Win"])}>
              <Monitor aria-hidden="true" />
              <span>Desktop</span>
            </RemoteButton>
            <RemoteButton label="Close focused window" onClick={() => sendSpecial("F4", ["Alt"])}>
              <SquareX aria-hidden="true" />
              <span>Close</span>
            </RemoteButton>
            <RemoteButton label="Minimize focused window" onClick={() => sendSpecial("ArrowDown", ["Win"])}>
              <span>Minimize</span>
            </RemoteButton>
          </>
        )}
      </div>
      {isSwitcherActive && (
        <div id={switcherHintId} className="remote-switcher-hint" role="status" aria-live="polite">
          <strong>Slide left or right to choose</strong>
          <span>Release to open</span>
        </div>
      )}
      <div className="remote-section-title remote-helper-section-title">
        <span>Browser</span>
        <small>Tabs and page controls.</small>
      </div>
      <div className="remote-utility-grid" aria-label="Browser helper controls">
        <RemoteButton label="Browser back" onClick={() => sendSpecial("BrowserBack")}>
          <CornerDownLeft aria-hidden="true" />
          <span>Back</span>
        </RemoteButton>
        {remoteSettings.showBrowserHelpers && (
          <>
            <RemoteButton label="New tab" onClick={() => sendSpecial("T", ["Control"])}>
              <Plus aria-hidden="true" />
              <span>New tab</span>
            </RemoteButton>
            <RemoteButton label="Close tab" onClick={() => sendSpecial("W", ["Control"])}>
              <SquareX aria-hidden="true" />
              <span>Close</span>
            </RemoteButton>
            <RemoteButton label="Reopen closed tab" onClick={() => sendSpecial("T", ["Control", "Shift"])}>
              <RotateCcw aria-hidden="true" />
              <span>Reopen</span>
            </RemoteButton>
            <RemoteButton label="Next tab" onClick={() => sendSpecial("Tab", ["Control"])}>
              <ArrowRight aria-hidden="true" />
              <span>Next</span>
            </RemoteButton>
            <RemoteButton label="Previous tab" onClick={() => sendSpecial("Tab", ["Control", "Shift"])}>
              <ArrowLeft aria-hidden="true" />
              <span>Prev tab</span>
            </RemoteButton>
            <RemoteButton label="Reload page" onClick={() => sendSpecial("R", ["Control"])}>
              <RefreshCw aria-hidden="true" />
              <span>Reload</span>
            </RemoteButton>
          </>
        )}
      </div>
      <button type="button" className="remote-fn-button remote-floating-fn" aria-controls={id} aria-expanded={isOpen} onClick={onClose}>
        Main
      </button>
    </div>
  );
}
