import { useEffect, useId, useRef, useState, type PointerEvent } from "react";
import { AppWindow, ArrowLeft, ArrowRight, CornerDownLeft, ExternalLink, Monitor, Plus, RefreshCw, RotateCcw, Search, SquareX } from "lucide-react";
import type { AppLaunchActionSummary, UrlOpenCapability, UrlOpenResultMessage } from "../../protocol";
import type { RemoteSettings } from "../../remoteSettings";
import { validateUrlDraft } from "../../urlOpenValidation";
import { InfoButton } from "../InfoButton";
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
  appLaunchActions: AppLaunchActionSummary[];
  id: string;
  isConnected: boolean;
  onAppLaunch: (actionId: string) => void;
  onUrlOpen: (url: string) => string | null;
  pendingAppLaunchId: string | null;
  pendingUrlOpen: boolean;
  remoteSettings: RemoteSettings;
  sendSpecial: (key: string, modifiers?: string[]) => void;
  urlOpenCapability?: UrlOpenCapability;
  urlOpenResult: UrlOpenResultMessage | null;
};

function getAppLaunchLabelClass(label: string): string {
  const length = [...label].length;
  if (length >= 9) {
    return "remote-app-launch-label remote-app-launch-label-long";
  }

  if (length >= 7) {
    return "remote-app-launch-label remote-app-launch-label-medium";
  }

  return "remote-app-launch-label";
}

export function RemoteUtilityPanel({ appLaunchActions, id, isConnected, onAppLaunch, onUrlOpen, pendingAppLaunchId, pendingUrlOpen, remoteSettings, sendSpecial, urlOpenCapability, urlOpenResult }: RemoteUtilityPanelProps) {
  const [isSwitcherActive, setIsSwitcherActive] = useState(false);
  const [isUrlDialogOpen, setIsUrlDialogOpen] = useState(false);
  const [urlDraft, setUrlDraft] = useState("");
  const [dismissedUrlResultOperationId, setDismissedUrlResultOperationId] = useState<string | null>(null);
  const switcherHintId = useId();
  const urlDialogTitleId = useId();
  const urlValidationId = useId();
  const urlDialogRef = useRef<HTMLDialogElement | null>(null);
  const urlInputRef = useRef<HTMLInputElement | null>(null);
  const switcherPointerRef = useRef<SwitcherPointer | null>(null);
  const switcherTimerRef = useRef<number | null>(null);
  const ignoreSwitcherClickRef = useRef(false);
  const sendSpecialRef = useRef(sendSpecial);
  const pendingUrlOperationRef = useRef<string | null>(null);
  const urlValidation = validateUrlDraft(urlDraft);
  const visibleUrlOpenResult = urlOpenResult?.operationId === dismissedUrlResultOperationId ? null : urlOpenResult;
  sendSpecialRef.current = sendSpecial;

  useEffect(() => {
    if (!urlOpenResult || urlOpenResult.operationId !== pendingUrlOperationRef.current) {
      return;
    }

    pendingUrlOperationRef.current = null;
  }, [urlOpenResult]);

  useEffect(() => {
    const dialog = urlDialogRef.current;
    if (!isUrlDialogOpen || !dialog || dialog.open) {
      return;
    }

    if (typeof dialog.showModal === "function") {
      dialog.showModal();
    } else {
      dialog.setAttribute("open", "");
    }

    window.requestAnimationFrame(() => urlInputRef.current?.focus());
  }, [isUrlDialogOpen]);

  useEffect(() => {
    if (!isConnected) {
      setIsUrlDialogOpen(false);
    }
  }, [isConnected]);

  const closeUrlDialog = () => {
    if (pendingUrlOpen) {
      return;
    }

    const dialog = urlDialogRef.current;
    if (dialog?.open && typeof dialog.close === "function") {
      dialog.close();
      return;
    }

    setIsUrlDialogOpen(false);
  };

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
        <RemoteButton label="Open URL" onClick={() => setIsUrlDialogOpen(true)}>
          <ExternalLink aria-hidden="true" />
          <span>Open URL</span>
        </RemoteButton>
      </div>
      {isUrlDialogOpen && (
        <dialog
          ref={urlDialogRef}
          className="remote-url-dialog"
          aria-labelledby={urlDialogTitleId}
          aria-modal="true"
          onCancel={(event) => {
            event.preventDefault();
            closeUrlDialog();
          }}
          onClick={(event) => {
            if (event.target === event.currentTarget) {
              closeUrlDialog();
            }
          }}
          onClose={(event) => {
            if (event.target === event.currentTarget) {
              setIsUrlDialogOpen(false);
            }
          }}
        >
          {/* Use the URL keyboard without rejecting the app's supported scheme-less addresses. */}
          <form
            noValidate
            onSubmit={(event) => {
              event.preventDefault();
              if (!pendingUrlOpen && urlOpenCapability?.canOpen && urlValidation.valid) {
                setDismissedUrlResultOperationId(null);
                pendingUrlOperationRef.current = onUrlOpen(urlDraft);
              }
            }}
          >
            <header>
              <span className="setting-label-with-info remote-url-open-label">
                <h2 id={urlDialogTitleId}>Open URL on PC</h2>
                <InfoButton
                  title="Opening URLs on PC"
                  size="detailed"
                  description="Enter a web address to open once in the PC's default browser. Addresses without a scheme use HTTPS. Only HTTP and HTTPS are supported. Voltura Air does not choose or fall back to a specific browser."
                />
              </span>
            </header>
            {!urlOpenCapability && <p className="remote-url-feedback error" role="alert">Update the Windows host to open web addresses.</p>}
            {urlOpenCapability && !urlOpenCapability.canOpen && <p className="remote-url-feedback error" role="alert">Allow URL opening in the PC permissions first.</p>}
            {urlOpenCapability?.canOpen && (
              <label>
                <span>Web address</span>
                <input
                  ref={urlInputRef}
                  id="remote-url-draft"
                  name="url"
                  type="url"
                  inputMode="url"
                  autoComplete="url"
                  autoCapitalize="none"
                  autoCorrect="off"
                  enterKeyHint="go"
                  spellCheck={false}
                  maxLength={2048}
                  placeholder="example.com"
                  value={urlDraft}
                  aria-describedby={!urlValidation.valid && urlDraft.trim() ? urlValidationId : undefined}
                  aria-invalid={!urlValidation.valid && urlDraft.trim() ? true : undefined}
                  onChange={(event) => setUrlDraft(event.target.value)}
                />
              </label>
            )}
            {!urlValidation.valid && urlDraft.trim() && <p id={urlValidationId} className="remote-url-feedback error" role="alert">{urlValidation.message}</p>}
            {pendingUrlOpen && <p className="remote-url-feedback pending" role="status">Waiting for the PC.</p>}
            {!pendingUrlOpen && visibleUrlOpenResult && <p className={`remote-url-feedback ${visibleUrlOpenResult.succeeded ? "success" : "error"}`} role={visibleUrlOpenResult.succeeded ? "status" : "alert"}>{visibleUrlOpenResult.message}</p>}
            <div className="remote-url-dialog-actions">
              {urlOpenCapability?.canOpen && (
                <button type="submit" className="remote-url-dialog-primary" disabled={pendingUrlOpen || !urlValidation.valid}>
                  <ExternalLink aria-hidden="true" />
                  <span>{pendingUrlOpen ? "Opening…" : visibleUrlOpenResult && !visibleUrlOpenResult.succeeded ? "Retry" : "Open"}</span>
                </button>
              )}
              <button
                type="button"
                disabled={pendingUrlOpen || urlDraft.length === 0}
                onClick={() => {
                  setUrlDraft("");
                  setDismissedUrlResultOperationId(urlOpenResult?.operationId ?? null);
                }}
              >
                Clear
              </button>
              <button type="button" onClick={closeUrlDialog} disabled={pendingUrlOpen}>Close</button>
            </div>
          </form>
        </dialog>
      )}
      {appLaunchActions.length > 0 && (
        <>
          <div className="remote-section-title remote-helper-section-title">
            <span>Applications</span>
            <small>Buttons approved on this PC.</small>
          </div>
          <div className="remote-utility-grid remote-app-launch-grid" aria-label="Application launch controls">
            {appLaunchActions.map((action) => (
              <RemoteButton
                key={action.id}
                label={`Start ${action.label}`}
                disabled={pendingAppLaunchId !== null}
                onClick={() => onAppLaunch(action.id)}
              >
                <AppWindow aria-hidden="true" />
                <span className={getAppLaunchLabelClass(action.label)}>{pendingAppLaunchId === action.id ? "Starting…" : action.label}</span>
              </RemoteButton>
            ))}
          </div>
        </>
      )}
    </div>
  );
}
