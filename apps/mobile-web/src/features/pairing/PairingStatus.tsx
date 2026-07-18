import { useEffect, useId, useMemo, useRef, useState } from "react";
import { Camera, CheckCircle2, LoaderCircle, Power, RefreshCw } from "lucide-react";
import { copyTextToClipboard } from "../../foundation/diagnostics/mobileDiagnostics";
import { buildPairingDiagnostics, getPairingFeedback } from "../../foundation/pairing/pairingFeedback";
import {
  validateManualConnectionInput,
  type ManualConnectionTarget
} from "../../foundation/pairing/pairingLink";
import { ModalDialog } from "../../ui/overlays/ModalDialog";
import { SavedPcReconnectChoice, type SavedPcReconnectOption } from "./SavedPcReconnectChoice";

interface PairingStatusProps {
  activePcUnavailable?: boolean;
  blocksAppInteraction?: boolean;
  connectionProgress?: "reconnecting" | "connected";
  deviceName?: string | undefined;
  diagnostics?: string;
  heading?: string | undefined;
  message: string;
  onDeviceNameChange?: ((deviceName: string) => void) | undefined;
  onManualHostSubmit?: (target: ManualConnectionTarget) => void;
  onPrimaryAction: () => void;
  onSecondaryAction?: (() => void) | undefined;
  primaryLabel?: string | undefined;
  pcName?: string;
  savedPcOptions?: SavedPcReconnectOption[] | undefined;
  secondaryLabel?: string | undefined;
  selectedSavedPcId?: string | undefined;
  onSavedPcChange?: ((pcId: string) => void) | undefined;
}

export function PairingStatus({
  activePcUnavailable = false,
  blocksAppInteraction = false,
  connectionProgress,
  deviceName,
  diagnostics,
  heading,
  message,
  onDeviceNameChange,
  onManualHostSubmit,
  onPrimaryAction,
  onSecondaryAction,
  primaryLabel,
  pcName,
  savedPcOptions,
  secondaryLabel,
  selectedSavedPcId,
  onSavedPcChange
}: PairingStatusProps) {
  const feedback = useMemo(() => getPairingFeedback(message, activePcUnavailable), [activePcUnavailable, message]);
  const headingId = useId();
  const descriptionId = useId();
  const manualHostErrorId = useId();
  const primaryActionRef = useRef<HTMLButtonElement | null>(null);
  const manualHostInputRef = useRef<HTMLInputElement | null>(null);
  const sectionRef = useRef<HTMLElement | null>(null);
  const isBlocking = blocksAppInteraction || activePcUnavailable || connectionProgress !== undefined;
  const [isHelpDialogOpen, setIsHelpDialogOpen] = useState(false);
  const [isManualHostDialogOpen, setIsManualHostDialogOpen] = useState(false);
  const [manualHost, setManualHost] = useState("");
  const [manualHostError, setManualHostError] = useState("");
  const [copyStatus, setCopyStatus] = useState("");
  const [copyToast, setCopyToast] = useState("");
  const [manualDiagnostics, setManualDiagnostics] = useState("");

  useEffect(() => {
    if (!isBlocking) {
      return;
    }

    primaryActionRef.current?.focus();
  }, [isBlocking]);

  useEffect(() => {
    if (!copyToast) {
      return;
    }

    const timeout = window.setTimeout(() => { setCopyToast(""); }, 3000);
    return () => { window.clearTimeout(timeout); };
  }, [copyToast]);

  const copyDiagnostics = async () => {
    setCopyToast("");
    setCopyStatus("");
    setManualDiagnostics("");
    const diagnosticsText = diagnostics ?? buildPairingDiagnostics(message, activePcUnavailable, feedback.diagnosticCode);
    const result = await copyTextToClipboard(diagnosticsText);
    if (result === "copied") {
      setCopyToast("Diagnostics copied.");
      return;
    }

    setManualDiagnostics(diagnosticsText);
    setCopyStatus("Could not copy automatically. Select the diagnostics below and copy manually.");
  };

  const closeManualHostDialog = () => {
    setIsManualHostDialogOpen(false);
    setManualHost("");
    setManualHostError("");
  };

  const submitManualHost = (event: React.SubmitEvent<HTMLFormElement>) => {
    event.preventDefault();
    const validation = validateManualConnectionInput(manualHost, window.location.href);
    if (!validation.valid) {
      setManualHostError(validation.message);
      return false;
    }

    if (onManualHostSubmit) {
      onManualHostSubmit(validation.target);
      setManualHost("");
      setManualHostError("");
      return true;
    }

    window.location.assign(validation.target.kind === "pairing" ? manualHost.trim() : validation.target.pcUrl);
    return false;
  };

  const keepModalFocusInside = (event: React.KeyboardEvent<HTMLElement>) => {
    if (!isBlocking || event.key !== "Tab") {
      return;
    }

    const focusable = [...(sectionRef.current?.querySelectorAll<HTMLElement>(
      'button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'
    ) ?? [])];
    if (focusable.length === 0) {
      event.preventDefault();
      primaryActionRef.current?.focus();
      return;
    }

    const first = focusable[0]!;
    const last = focusable.at(-1)!;
    if (!focusable.includes(document.activeElement as HTMLElement)) {
      event.preventDefault();
      (event.shiftKey ? last : first).focus();
    } else if (event.shiftKey && document.activeElement === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault();
      first.focus();
    }
  };

  const progressTitle = connectionProgress === "connected"
    ? `Connected to ${pcName ?? "PC"}`
    : `Reconnecting to ${pcName ?? "PC"}…`;
  const progressBody = connectionProgress === "connected"
    ? "Connection restored. Returning to your previous screen."
    : "Checking whether Voltura Air is available.";
  const primaryActionDisabled = connectionProgress !== undefined;
  const primaryActionLabel = connectionProgress === "reconnecting"
    ? "Reconnecting…"
    : connectionProgress === "connected"
      ? "Connected"
      : primaryLabel ?? feedback.primaryLabel;
  const hasSavedPcChoice = !connectionProgress && savedPcOptions !== undefined && savedPcOptions.length > 0;
  const displayTitle = heading ?? (hasSavedPcChoice ? "Connect to a PC" : feedback.title);
  const displayBody = hasSavedPcChoice
    ? savedPcOptions.length === 1
      ? `Reconnect to ${savedPcOptions[0]!.label}, or pair another PC by taking a photo of its QR code.`
      : "Choose a saved PC to reconnect, or pair another PC by taking a photo of its QR code."
    : feedback.body;

  return (
    <>
      {isBlocking && <div className="pairing-backdrop" aria-hidden="true" />}
      <section
        ref={sectionRef}
        className={`pairing-required ${isBlocking ? "connection-blocking" : ""} ${connectionProgress ? `connection-${connectionProgress}` : ""} ${!connectionProgress && feedback.severity === "error" ? "pairing-feedback-error" : ""}`}
        role={isBlocking ? "dialog" : "status"}
        aria-modal={isBlocking || undefined}
        aria-labelledby={headingId}
        aria-describedby={descriptionId}
        aria-busy={connectionProgress === "reconnecting" || undefined}
        aria-live="polite"
        onKeyDown={keepModalFocusInside}
      >
        <div className="pairing-summary">
          {connectionProgress === "reconnecting"
            ? <LoaderCircle className="pairing-progress-icon" aria-hidden="true" />
            : connectionProgress === "connected"
              ? <CheckCircle2 aria-hidden="true" />
              : activePcUnavailable
                ? <Power aria-hidden="true" />
                : <Camera aria-hidden="true" />}
          <p className="pairing-status-label">{connectionProgress ? "Connection status" : hasSavedPcChoice ? "Connection" : feedback.severity === "info" ? "Pairing" : "Pairing feedback"}</p>
          <h1 id={headingId}>{connectionProgress ? progressTitle : displayTitle}</h1>
          <p id={descriptionId}>{connectionProgress ? progressBody : displayBody}</p>
          {!connectionProgress && feedback.diagnosticCode && <p className="pairing-diagnostic-code">{feedback.diagnosticCode}</p>}

          {deviceName !== undefined && onDeviceNameChange && (
            <label className="pairing-device-name">
              <span>Device name</span>
              <input
                className="text-input"
                maxLength={80}
                value={deviceName}
                onChange={(event) => { onDeviceNameChange(event.target.value); }}
              />
            </label>
          )}

          {hasSavedPcChoice && onSavedPcChange && (
            <SavedPcReconnectChoice onChange={onSavedPcChange} options={savedPcOptions} selectedPcId={selectedSavedPcId} />
          )}
        </div>

        <div className="pairing-recovery">
          <div className="pairing-actions">
            <button
              ref={primaryActionRef}
              className="pairing-action-primary"
              type="button"
              aria-disabled={primaryActionDisabled || undefined}
              onClick={() => {
                if (!primaryActionDisabled) {
                  onPrimaryAction();
                }
              }}
            >
              {connectionProgress === "reconnecting" ? (
                <>
                  <LoaderCircle className="pairing-progress-icon" aria-hidden="true" />
                  <span>{primaryActionLabel}</span>
                </>
              ) : connectionProgress === "connected" ? (
                <>
                  <CheckCircle2 aria-hidden="true" />
                  <span>{primaryActionLabel}</span>
                </>
              ) : (
                <>
                  <RefreshCw aria-hidden="true" />
                  <span>{primaryActionLabel}</span>
                </>
              )}
            </button>
            {!connectionProgress && (onSecondaryAction !== undefined || feedback.showRecoveryActions) && (
              <div className="pairing-secondary-actions">
                {onSecondaryAction && (
                  <button className="pairing-action-secondary" type="button" onClick={onSecondaryAction}>
                    <Camera aria-hidden="true" />
                    <span>{secondaryLabel ?? "Take photo of new QR code"}</span>
                  </button>
                )}
                {feedback.showRecoveryActions && (
                  <>
                    <button type="button" aria-haspopup="dialog" onClick={() => { setIsManualHostDialogOpen(true); }}>
                      <span>Enter host manually</span>
                    </button>
                    <button type="button" aria-haspopup="dialog" onClick={() => { setIsHelpDialogOpen(true); }}>
                      <span>Open troubleshooting help</span>
                    </button>
                    <button type="button" onClick={() => { void copyDiagnostics(); }}>
                      <span>Copy diagnostics</span>
                    </button>
                  </>
                )}
              </div>
            )}
          </div>

          {copyStatus && <p className="pairing-inline-status">{copyStatus}</p>}
          {manualDiagnostics && (
            <textarea
              aria-label="Diagnostics text"
              className="text-input diagnostics-textarea"
              onFocus={(event) => { event.currentTarget.select(); }}
              readOnly
              rows={8}
              value={manualDiagnostics}
            />
          )}
        </div>
      </section>
      <ModalDialog
        actionsClassName="pairing-manual-host-actions"
        className="pairing-manual-host-dialog"
        dismissLabel="Cancel"
        formClassName="pairing-manual-host"
        initialFocusRef={manualHostInputRef}
        isOpen={isManualHostDialogOpen}
        landscapeSize="wide"
        onClose={closeManualHostDialog}
        onSubmit={submitManualHost}
        submitClassName="pairing-manual-host-connect"
        submitLabel="Connect"
        title="Enter host manually"
      >
        <>
          <label>
            <span>Host or pairing link</span>
            <input
              ref={manualHostInputRef}
              className="text-input"
              inputMode="url"
              placeholder="http://192.168.1.50:51395"
              aria-describedby={manualHostError ? manualHostErrorId : undefined}
              aria-invalid={manualHostError ? true : undefined}
              value={manualHost}
              onChange={(event) => {
                setManualHost(event.target.value);
                setManualHostError("");
              }}
            />
          </label>
          {manualHostError && <p id={manualHostErrorId} className="pairing-inline-error" role="alert">{manualHostError}</p>}
        </>
      </ModalDialog>
      <ModalDialog
        className="pairing-help-dialog"
        dismissLabel="OK"
        focusDismissAction
        isOpen={isHelpDialogOpen}
        onClose={() => { setIsHelpDialogOpen(false); }}
        title="Troubleshooting help"
      >
        <ul className="pairing-help-list">
          {feedback.hints.map((hint) => (
            <li key={hint}>{hint}</li>
          ))}
        </ul>
      </ModalDialog>
      {copyToast && (
        <div className="app-toast success" role="status">
          {copyToast}
        </div>
      )}
    </>
  );
}
