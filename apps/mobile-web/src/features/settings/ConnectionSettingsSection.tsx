import { useId, useState, type SubmitEvent } from "react";
import { Camera, Clipboard, Power, RefreshCw, X } from "lucide-react";
import { copyTextToClipboard } from "../../foundation/diagnostics/mobileDiagnostics";
import { validateManualConnectionInput } from "../../foundation/pairing/pairingLink";
import { getPcDisplayName } from "../../foundation/pairing/pcDisplayName";
import { InfoButton } from "../../ui/overlays/InfoButton";
import { ConfirmationDialog } from "../../ui/overlays/ConfirmationDialog";
import type { SettingsDrawerProps } from "./SettingsDrawerTypes";

type ConnectionSettingsProps = Pick<
  SettingsDrawerProps,
  | "activePc"
  | "deviceName"
  | "diagnostics"
  | "disconnectActivePc"
  | "forgetPc"
  | "isPairingQrReading"
  | "onManualHostSubmit"
  | "onPairingQrSelected"
  | "pairedPcs"
  | "pairingQrInputRef"
  | "pairingScanMessage"
  | "renameDevice"
  | "renamePc"
  | "scanPairingQr"
  | "selectPc"
  | "usesLivePairingQr"
>;

export function ConnectionSettingsSection({
  activePc,
  deviceName,
  diagnostics,
  disconnectActivePc,
  forgetPc,
  isPairingQrReading = false,
  onManualHostSubmit,
  onPairingQrSelected,
  pairedPcs,
  pairingQrInputRef,
  pairingScanMessage,
  renameDevice,
  renamePc,
  scanPairingQr,
  selectPc,
  usesLivePairingQr
}: ConnectionSettingsProps) {
  const pairingQrActionLabel = usesLivePairingQr ? "Scan QR code" : "Take photo of QR code";
  const [manualHost, setManualHost] = useState("");
  const [manualHostError, setManualHostError] = useState("");
  const manualHostErrorId = useId();
  const [copyDiagnosticsStatus, setCopyDiagnosticsStatus] = useState("");
  const [manualDiagnostics, setManualDiagnostics] = useState("");
  const [forgetTarget, setForgetTarget] = useState<{ id: string; name: string } | null>(null);

  const copyDiagnostics = async () => {
    setCopyDiagnosticsStatus("");
    setManualDiagnostics("");
    const result = await copyTextToClipboard(diagnostics);
    if (result === "copied") {
      setCopyDiagnosticsStatus("Diagnostics copied.");
      return;
    }

    setManualDiagnostics(diagnostics);
    setCopyDiagnosticsStatus("Could not copy automatically. Select the diagnostics below and copy manually.");
  };

  const submitManualHost = (event: SubmitEvent<HTMLFormElement>) => {
    event.preventDefault();
    const validation = validateManualConnectionInput(manualHost, window.location.href);
    if (!validation.valid) {
      setManualHostError(validation.message);
      return;
    }

    onManualHostSubmit(validation.target);
    setManualHost("");
    setManualHostError("");
  };

  return (
    <>
      <label className="setting-group">
        <span>This device name</span>
        <input className="text-input" type="text" maxLength={80} value={deviceName} onChange={(event) => { renameDevice(event.target.value); }} placeholder="Joakim's iPhone" />
      </label>

      <div className="install-card">
        <div className="install-title"><Power aria-hidden="true" /><span>PC connection</span></div>
        <p>{activePc ? `Active PC: ${getPcDisplayName(activePc)}` : "No active PC. Choose a saved PC or scan a pairing QR."}</p>
        {activePc && <button type="button" className="danger-button" onClick={disconnectActivePc}><Power aria-hidden="true" /><span>Disconnect this PC</span></button>}
        {pairedPcs.length > 0 && (
          <div className="pc-list">
            {pairedPcs.map((pc) => (
              <div className={`pc-row ${pc.id === activePc?.id ? "active" : ""}`} key={pc.id}>
                <div className="pc-meta">
                  <input aria-label="PC name" className="pc-name-input" type="text" maxLength={120} value={pc.name} onChange={(event) => { renamePc(pc.id, event.target.value); }} />
                  <small>{pc.id === activePc?.id ? "Active" : "Saved"} &middot; {pc.url}</small>
                </div>
                <div className="pc-actions">
                  {pc.id !== activePc?.id && <button type="button" onClick={() => { selectPc(pc.id); }}><RefreshCw aria-hidden="true" /><span>Connect</span></button>}
                  <button type="button" className="danger-button" onClick={() => { setForgetTarget({ id: pc.id, name: getPcDisplayName(pc) }); }}><X aria-hidden="true" /><span>Forget</span></button>
                </div>
              </div>
            ))}
          </div>
        )}
      </div>
      <ConfirmationDialog
        confirmLabel="Forget PC"
        description={`Forget ${forgetTarget?.name ?? "this PC"}? You will need to scan its pairing QR code again, and this device's saved settings for it will be removed.`}
        isOpen={forgetTarget !== null}
        onCancel={() => { setForgetTarget(null); }}
        onConfirm={() => { const target = forgetTarget; setForgetTarget(null); if (target) {forgetPc(target.id);} }}
        title="Forget this PC?"
      />

      <div className="install-card">
        <div className="install-title"><Camera aria-hidden="true" /><span>Pair from QR code</span></div>
        <p>{pairingScanMessage}</p>
        <input ref={pairingQrInputRef} className="visually-hidden" type="file" accept="image/*" capture="environment" disabled={isPairingQrReading} onChange={(event) => { void onPairingQrSelected(event); }} />
        <button type="button" disabled={isPairingQrReading} aria-busy={isPairingQrReading || undefined} onClick={scanPairingQr}><Camera aria-hidden="true" /><span>{isPairingQrReading ? "Reading QR code…" : pairingQrActionLabel}</span></button>
      </div>

      <div className="install-card">
        <div className="install-title">
          <Clipboard aria-hidden="true" />
          <span className="setting-label-with-info"><span>Diagnostics</span><InfoButton title="Connection diagnostics" size="detailed" description="Copies redacted connection details for troubleshooting. Pairing tokens, private reconnect keys, challenges, and proofs are not included." /></span>
        </div>
        <p>Copy redacted troubleshooting details.</p>
        <button type="button" onClick={() => { void copyDiagnostics(); }}><Clipboard aria-hidden="true" /><span>Copy diagnostics</span></button>
        {copyDiagnosticsStatus && <p className="pairing-inline-status">{copyDiagnosticsStatus}</p>}
        {manualDiagnostics && <textarea aria-label="Diagnostics text" className="text-input diagnostics-textarea" onFocus={(event) => { event.currentTarget.select(); }} readOnly rows={8} value={manualDiagnostics} />}
      </div>

      <div className="install-card">
        <div className="install-title">
          <Power aria-hidden="true" />
          <span className="setting-label-with-info"><span>Add PC manually</span><InfoButton title="Connect manually" size="detailed" description="Use this when the PC IP or port changed, or when a QR page was opened before the host changed network." /></span>
        </div>
        <p>Connect using a host address or pairing link.</p>
        <form className="manual-pc-form" onSubmit={submitManualHost}>
          <label className="setting-group">
            <span>Host or pairing link</span>
            <input
              className="text-input"
              inputMode="url"
              placeholder="192.168.1.50:51395"
              aria-describedby={manualHostError ? manualHostErrorId : undefined}
              aria-invalid={manualHostError ? true : undefined}
              value={manualHost}
              onChange={(event) => { setManualHost(event.target.value); setManualHostError(""); }}
            />
          </label>
          <button type="submit">Connect to PC</button>
          {manualHostError && <p id={manualHostErrorId} className="pairing-inline-error" role="alert">{manualHostError}</p>}
        </form>
      </div>
    </>
  );
}
