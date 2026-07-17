import { useEffect, useId, useMemo, useRef, useState } from "react";
import { Camera, CheckCircle2, LoaderCircle, Power, RefreshCw } from "lucide-react";
import { copyTextToClipboard } from "../../mobileDiagnostics";
import { buildPairingDiagnostics, getPairingFeedback, normalizeManualHostInput } from "../../pairingFeedback";

interface PairingStatusProps {
  activePcUnavailable?: boolean;
  connectionProgress?: "reconnecting" | "connected";
  deviceName?: string | undefined;
  diagnostics?: string;
  message: string;
  onDeviceNameChange?: ((deviceName: string) => void) | undefined;
  onManualHostSubmit?: (target: string) => void;
  onPrimaryAction: () => void;
  onSecondaryAction?: () => void;
  primaryLabel?: string | undefined;
  pcName?: string;
}

export function PairingStatus({
  activePcUnavailable = false,
  connectionProgress,
  deviceName,
  diagnostics,
  message,
  onDeviceNameChange,
  onManualHostSubmit,
  onPrimaryAction,
  onSecondaryAction,
  primaryLabel,
  pcName
}: PairingStatusProps) {
  const feedback = useMemo(() => getPairingFeedback(message, activePcUnavailable), [activePcUnavailable, message]);
  const headingId = useId();
  const descriptionId = useId();
  const headingRef = useRef<HTMLHeadingElement | null>(null);
  const sectionRef = useRef<HTMLElement | null>(null);
  const isBlocking = activePcUnavailable || connectionProgress !== undefined;
  const [showHelp, setShowHelp] = useState(false);
  const [showManualHost, setShowManualHost] = useState(false);
  const [manualHost, setManualHost] = useState("");
  const [manualHostError, setManualHostError] = useState("");
  const [copyStatus, setCopyStatus] = useState("");
  const [copyToast, setCopyToast] = useState("");
  const [manualDiagnostics, setManualDiagnostics] = useState("");

  useEffect(() => {
    if (!isBlocking) {
      return;
    }

    headingRef.current?.focus();
  }, [connectionProgress, isBlocking]);

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

  const submitManualHost = (event: React.SubmitEvent<HTMLFormElement>) => {
    event.preventDefault();
    const target = normalizeManualHostInput(manualHost, window.location.href);
    if (!target) {
      setManualHostError("Enter a host URL, IP:port, pairing link, or port number.");
      return;
    }

    if (onManualHostSubmit) {
      onManualHostSubmit(target);
      setManualHost("");
      setManualHostError("");
      setShowManualHost(false);
      return;
    }

    window.location.assign(target);
  };

  const keepModalFocusInside = (event: React.KeyboardEvent<HTMLElement>) => {
    if (!isBlocking || event.key !== "Tab") {
      return;
    }

    const focusable = [...(sectionRef.current?.querySelectorAll<HTMLElement>(
      'button:not([disabled]), input:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])'
    ) ?? [])];
    if (focusable.length === 0) {
      event.preventDefault();
      headingRef.current?.focus();
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

  return (
    <>
      {isBlocking && <div className="pairing-backdrop" aria-hidden="true" />}
      <section
        ref={sectionRef}
        className={`pairing-required ${isBlocking ? "connection-unavailable" : ""} ${connectionProgress ? `connection-${connectionProgress}` : ""} ${!connectionProgress && feedback.severity === "error" ? "pairing-feedback-error" : ""}`}
        role={isBlocking ? "dialog" : "status"}
        aria-modal={isBlocking || undefined}
        aria-labelledby={headingId}
        aria-describedby={descriptionId}
        aria-busy={connectionProgress === "reconnecting" || undefined}
        aria-live="polite"
        onKeyDown={keepModalFocusInside}
      >
        {connectionProgress === "reconnecting"
          ? <LoaderCircle className="pairing-progress-icon" aria-hidden="true" />
          : connectionProgress === "connected"
            ? <CheckCircle2 aria-hidden="true" />
            : activePcUnavailable
              ? <Power aria-hidden="true" />
              : <Camera aria-hidden="true" />}
        <p className="pairing-status-label">{connectionProgress ? "Connection status" : feedback.severity === "info" ? "Pairing" : "Pairing feedback"}</p>
        <h1 ref={headingRef} id={headingId} tabIndex={isBlocking ? -1 : undefined}>{connectionProgress ? progressTitle : feedback.title}</h1>
        <p id={descriptionId}>{connectionProgress ? progressBody : feedback.body}</p>
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

        {!connectionProgress && showHelp && feedback.hints.length > 0 && (
          <ul className="pairing-help-list">
            {feedback.hints.map((hint) => (
              <li key={hint}>{hint}</li>
            ))}
          </ul>
        )}

        <div className="pairing-actions">
          {connectionProgress === "reconnecting" ? (
            <button className="pairing-action-primary" type="button" disabled>
              <LoaderCircle className="pairing-progress-icon" aria-hidden="true" />
              <span>Reconnecting…</span>
            </button>
          ) : connectionProgress !== "connected" && (
            <button className="pairing-action-primary" type="button" onClick={onPrimaryAction}>
              <RefreshCw aria-hidden="true" />
              <span>{primaryLabel ?? feedback.primaryLabel}</span>
            </button>
          )}
          {!connectionProgress && onSecondaryAction && (
            <button className="pairing-action-secondary" type="button" onClick={onSecondaryAction}>
              <Camera aria-hidden="true" />
              <span>Scan new QR code</span>
            </button>
          )}
          {!connectionProgress && feedback.showRecoveryActions && (
            <>
              <button type="button" onClick={() => { setShowManualHost((current) => !current); }}>
                <span>{showManualHost ? "Hide manual host" : "Enter host manually"}</span>
              </button>
              <button type="button" onClick={() => { setShowHelp((current) => !current); }}>
                <span>{showHelp ? "Hide troubleshooting" : "Open troubleshooting help"}</span>
              </button>
              <button type="button" onClick={() => { void copyDiagnostics(); }}>
                <span>Copy diagnostics</span>
              </button>
            </>
          )}
        </div>

        {!connectionProgress && showManualHost && (
          <form className="pairing-manual-host" onSubmit={submitManualHost}>
            <label>
              <span>Host or pairing link</span>
              <input
                className="text-input"
                inputMode="url"
                placeholder="http://192.168.1.50:51395"
                value={manualHost}
                onChange={(event) => {
                  setManualHost(event.target.value);
                  setManualHostError("");
                }}
              />
            </label>
            <div className="pairing-manual-host-row">
              <button type="submit">Connect</button>
            </div>
            {manualHostError && <p className="pairing-inline-error">{manualHostError}</p>}
          </form>
        )}

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
      </section>
      {copyToast && (
        <div className="app-toast success" role="status">
          {copyToast}
        </div>
      )}
    </>
  );
}
