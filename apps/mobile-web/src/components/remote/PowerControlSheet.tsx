import { useEffect, useRef, useState, type CSSProperties, type KeyboardEvent, type PointerEvent } from "react";
import { LockKeyhole, LogOut, Monitor, MonitorOff, Power, RotateCcw, ShieldAlert, Sparkles, X } from "lucide-react";
import type { PowerCapabilities, SystemPowerAction, SystemPowerResultMessage } from "../../protocol";

const holdDurationMs = 1600;
const holdTickMs = 40;

type PowerControlSheetProps = {
  capabilities: PowerCapabilities;
  onAction: (action: SystemPowerAction) => void;
  onClose: () => void;
  pendingAction: SystemPowerAction | null;
  result: SystemPowerResultMessage | null;
};

type PowerActionDefinition = {
  action: SystemPowerAction;
  description: string;
  destructive: boolean;
  label: string;
  requiresConfirmation?: boolean;
  warning?: string;
  Icon: typeof Power;
};

const standardActions: PowerActionDefinition[] = [
  { action: "lock", label: "Lock PC", description: "Lock Windows and keep your apps running.", destructive: false, Icon: LockKeyhole },
  { action: "blackoutDisplay", label: "Blackout display", description: "Cover every screen with black while the PC and Voltura Air stay active. Any mouse or keyboard input restores it.", destructive: false, Icon: Monitor },
  {
    action: "displayOff",
    label: "Turn off display",
    description: "Ask Windows to turn off display output. Some PCs also enter sleep and disconnect Voltura Air.",
    destructive: false,
    requiresConfirmation: true,
    warning: "Your PC may enter sleep or Modern Standby. Voltura Air cannot wake it remotely; use a physical keyboard or mouse. Windows may require sign-in after wake.",
    Icon: MonitorOff
  },
  { action: "screenSaver", label: "Turn on screen saver", description: "Start the screen saver configured in Windows.", destructive: false, Icon: Sparkles }
];

const destructiveActions: PowerActionDefinition[] = [
  { action: "signOut", label: "Sign out", description: "End this Windows session.", warning: "Open apps may prevent sign out or show a save prompt.", destructive: true, Icon: LogOut },
  { action: "restart", label: "Restart PC", description: "Restart Windows now.", warning: "Unsaved work may be lost.", destructive: true, Icon: RotateCcw },
  { action: "shutdown", label: "Shut down PC", description: "Turn this PC off now.", warning: "Unsaved work may be lost.", destructive: true, Icon: Power }
];

export function PowerControlSheet({ capabilities, onAction, onClose, pendingAction: pendingRequest, result }: PowerControlSheetProps) {
  const [pendingAction, setPendingAction] = useState<PowerActionDefinition | null>(null);

  useEffect(() => {
    const onKeyDown = (event: globalThis.KeyboardEvent) => {
      if (event.key === "Escape") {
        if (pendingAction) {
          setPendingAction(null);
        } else {
          onClose();
        }
      }
    };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [onClose, pendingAction]);

  const chooseAction = (definition: PowerActionDefinition) => {
    if (!capabilities[definition.action]) {
      return;
    }

    if (definition.destructive || definition.requiresConfirmation) {
      setPendingAction(definition);
      return;
    }

    onAction(definition.action);
    if (definition.action === "blackoutDisplay") {
      onClose();
    }
  };

  return (
    <div className="power-sheet-layer">
      <button className="power-sheet-scrim" type="button" aria-label="Close power and session controls" onClick={onClose} />
      <section className="power-sheet" role="dialog" aria-modal="true" aria-labelledby="power-sheet-title">
        <header className="power-sheet-header">
          <div>
            <span className="power-sheet-eyebrow">Windows controls</span>
            <h2 id="power-sheet-title">{pendingAction ? pendingAction.label : "Power & session"}</h2>
          </div>
          <button className="power-sheet-close" type="button" aria-label="Close power and session controls" autoFocus onClick={onClose}>
            <X aria-hidden="true" />
          </button>
        </header>

        {pendingAction ? (
          <PowerConfirmation
            definition={pendingAction}
            onCancel={() => setPendingAction(null)}
            onConfirm={() => {
              onAction(pendingAction.action);
              onClose();
            }}
          />
        ) : (
          <div className="power-sheet-content">
            <div className="power-action-list" aria-label="Power and session actions">
              {standardActions.filter((definition) => definition.action !== "screenSaver" || capabilities.screenSaverAvailable).map((definition) => (
                <PowerActionRow key={definition.action} definition={definition} disabledReason={getDisabledReason(definition, capabilities)} pending={pendingRequest === definition.action} onChoose={chooseAction} />
              ))}
            </div>

            <div className="power-destructive-group">
              <div className="power-group-heading">
                <ShieldAlert aria-hidden="true" />
                <span>Session-ending actions</span>
              </div>
              <div className="power-action-list">
                {destructiveActions.map((definition) => (
                  <PowerActionRow key={definition.action} definition={definition} enabled={capabilities[definition.action]} onChoose={chooseAction} />
                ))}
              </div>
            </div>
            {pendingRequest && <div className="power-action-feedback pending" role="status">Waiting for the PC to respond…</div>}
            {result && (
              <div className={`power-action-feedback ${result.succeeded ? "success" : "error"}`} role={result.succeeded ? "status" : "alert"}>
                {result.message}
              </div>
            )}
          </div>
        )}
      </section>
    </div>
  );
}

function getDisabledReason(definition: PowerActionDefinition, capabilities: PowerCapabilities): string | null {
  if (!capabilities[definition.action]) {
    return "Disabled by the host.";
  }

  if (definition.action === "lock" && capabilities.lockAvailability === "disabledByPolicy") {
    return "Disabled in Windows. Open Voltura Air on the PC to enable it.";
  }

  if (definition.action === "lock" && capabilities.lockAvailability === "unavailable") {
    return "The PC could not check whether Windows locking is available.";
  }

  return null;
}

function PowerActionRow({ definition, enabled, disabledReason, pending = false, onChoose }: { definition: PowerActionDefinition; enabled?: boolean; disabledReason?: string | null; pending?: boolean; onChoose: (definition: PowerActionDefinition) => void }) {
  const { Icon } = definition;
  const isEnabled = !pending && (enabled ?? disabledReason === null);
  return (
    <button
      className={`power-action-row ${definition.destructive ? "destructive" : ""}`}
      type="button"
      disabled={!isEnabled}
      onClick={() => onChoose(definition)}
    >
      <span className="power-action-icon"><Icon aria-hidden="true" /></span>
      <span className="power-action-copy">
        <strong>{definition.label}</strong>
        <small>{pending ? "Waiting for the PC…" : isEnabled ? definition.description : disabledReason ?? "Disabled by the host."}</small>
      </span>
      {definition.destructive && isEnabled && <span className="power-action-safety">Hold</span>}
    </button>
  );
}

function PowerConfirmation({ definition, onCancel, onConfirm }: { definition: PowerActionDefinition; onCancel: () => void; onConfirm: () => void }) {
  return (
    <div className="power-confirmation">
      <div className="power-confirmation-icon"><definition.Icon aria-hidden="true" /></div>
      <p>{definition.warning}</p>
      <HoldToConfirmButton label={definition.label} onConfirm={onConfirm} />
      <button className="power-confirm-cancel" type="button" onClick={onCancel}>Cancel</button>
    </div>
  );
}

function HoldToConfirmButton({ label, onConfirm }: { label: string; onConfirm: () => void }) {
  const [progress, setProgress] = useState(0);
  const intervalRef = useRef<number | null>(null);
  const timeoutRef = useRef<number | null>(null);
  const startedAtRef = useRef(0);
  const completedRef = useRef(false);

  const clearHold = (reset = true) => {
    if (intervalRef.current !== null) {
      window.clearInterval(intervalRef.current);
      intervalRef.current = null;
    }
    if (timeoutRef.current !== null) {
      window.clearTimeout(timeoutRef.current);
      timeoutRef.current = null;
    }
    if (reset && !completedRef.current) {
      setProgress(0);
    }
  };

  useEffect(() => () => clearHold(false), []);

  const beginHold = () => {
    if (timeoutRef.current !== null || completedRef.current) {
      return;
    }
    startedAtRef.current = performance.now();
    setProgress(0.01);
    intervalRef.current = window.setInterval(() => {
      setProgress(Math.min(1, (performance.now() - startedAtRef.current) / holdDurationMs));
    }, holdTickMs);
    timeoutRef.current = window.setTimeout(() => {
      completedRef.current = true;
      setProgress(1);
      clearHold(false);
      onConfirm();
    }, holdDurationMs);
  };

  const onPointerDown = (event: PointerEvent<HTMLButtonElement>) => {
    if (event.button !== 0) {
      return;
    }
    event.preventDefault();
    event.currentTarget.setPointerCapture?.(event.pointerId);
    beginHold();
  };

  const onKeyDown = (event: KeyboardEvent<HTMLButtonElement>) => {
    if ((event.key === " " || event.key === "Enter") && !event.repeat) {
      event.preventDefault();
      beginHold();
    }
  };

  const cancelHold = () => clearHold();
  const style = { "--hold-progress": `${Math.round(progress * 100)}%` } as CSSProperties;

  return (
    <button
      className="hold-confirm-button"
      type="button"
      style={style}
      aria-label={`Hold to ${label.toLocaleLowerCase()}`}
      onClick={(event) => event.preventDefault()}
      onKeyDown={onKeyDown}
      onKeyUp={cancelHold}
      onPointerCancel={cancelHold}
      onPointerDown={onPointerDown}
      onPointerLeave={cancelHold}
      onPointerUp={cancelHold}
    >
      <span>{progress > 0 ? "Keep holding…" : `Hold to ${label.toLocaleLowerCase()}`}</span>
    </button>
  );
}
