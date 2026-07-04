import { useEffect, useMemo, useState } from "react";
import { Camera, Power, RefreshCw } from "lucide-react";
import { buildPairingDiagnostics, getPairingFeedback, normalizeManualHostInput } from "../pairingFeedback";

type PairingStatusProps = {
  activePcUnavailable?: boolean;
  deviceName?: string;
  message: string;
  onDeviceNameChange?: (deviceName: string) => void;
  onManualHostSubmit?: (target: string) => void;
  onPrimaryAction: () => void;
  onSecondaryAction?: () => void;
  primaryLabel?: string;
};

export function PairingStatus({
  activePcUnavailable = false,
  deviceName,
  message,
  onDeviceNameChange,
  onManualHostSubmit,
  onPrimaryAction,
  onSecondaryAction,
  primaryLabel
}: PairingStatusProps) {
  const feedback = useMemo(() => getPairingFeedback(message, activePcUnavailable), [activePcUnavailable, message]);
  const [showHelp, setShowHelp] = useState(false);
  const [showManualHost, setShowManualHost] = useState(false);
  const [manualHost, setManualHost] = useState("");
  const [manualHostError, setManualHostError] = useState("");
  const [copyStatus, setCopyStatus] = useState("");

  useEffect(() => {
    if (!activePcUnavailable) {
      return;
    }

    if (document.activeElement instanceof HTMLElement) {
      document.activeElement.blur();
    }
  }, [activePcUnavailable]);

  const copyDiagnostics = async () => {
    setCopyStatus("");
    const diagnostics = buildPairingDiagnostics(message, activePcUnavailable, feedback.diagnosticCode);
    try {
      await navigator.clipboard.writeText(diagnostics);
      setCopyStatus("Diagnostics copied.");
    } catch {
      setCopyStatus("Could not copy diagnostics in this browser.");
    }
  };

  const submitManualHost = (event: React.FormEvent<HTMLFormElement>) => {
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

  return (
    <>
      {activePcUnavailable && <div className="pairing-backdrop" aria-hidden="true" />}
      <section
        className={`pairing-required ${activePcUnavailable ? "connection-unavailable" : ""} ${feedback.severity === "error" ? "pairing-feedback-error" : ""}`}
        role="status"
        aria-live="polite"
      >
        {activePcUnavailable ? <Power aria-hidden="true" /> : <Camera aria-hidden="true" />}
        <p className="pairing-status-label">{feedback.severity === "info" ? "Pairing" : "Pairing feedback"}</p>
        <h1>{feedback.title}</h1>
        <p>{feedback.body}</p>
        {feedback.diagnosticCode && <p className="pairing-diagnostic-code">{feedback.diagnosticCode}</p>}

        {deviceName !== undefined && onDeviceNameChange && (
          <label className="pairing-device-name">
            <span>Device name</span>
            <input
              className="text-input"
              maxLength={80}
              value={deviceName}
              onChange={(event) => onDeviceNameChange(event.target.value)}
            />
          </label>
        )}

        {showHelp && feedback.hints.length > 0 && (
          <ul className="pairing-help-list">
            {feedback.hints.map((hint) => (
              <li key={hint}>{hint}</li>
            ))}
          </ul>
        )}

        <div className="pairing-actions">
          <button className="pairing-action-primary" type="button" onClick={onPrimaryAction}>
            <RefreshCw aria-hidden="true" />
            <span>{primaryLabel ?? feedback.primaryLabel}</span>
          </button>
          {onSecondaryAction && (
            <button className="pairing-action-secondary" type="button" onClick={onSecondaryAction}>
              <Camera aria-hidden="true" />
              <span>Scan new QR code</span>
            </button>
          )}
          {feedback.showRecoveryActions && (
            <>
              <button type="button" onClick={() => setShowManualHost((current) => !current)}>
                <span>{showManualHost ? "Hide manual host" : "Enter host manually"}</span>
              </button>
              <button type="button" onClick={() => setShowHelp((current) => !current)}>
                <span>{showHelp ? "Hide troubleshooting" : "Open troubleshooting help"}</span>
              </button>
              <button type="button" onClick={copyDiagnostics}>
                <span>Copy diagnostics</span>
              </button>
            </>
          )}
        </div>

        {showManualHost && (
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
      </section>
    </>
  );
}
