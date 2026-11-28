import { useState } from "react";
import type { ConnectionState } from "../../foundation/connection/connectionTypes";
import { getPcDisplayName } from "../../foundation/pairing/pcDisplayName";
import type { ManualConnectionTarget } from "../../foundation/pairing/pairingLink";
import type { PcProfile } from "../../foundation/connection/pcProfiles";
import { PairingStatus } from "./PairingStatus";

interface PairingGateProps {
  activePc: PcProfile | null;
  connectManualHost: (target: ManualConnectionTarget) => void;
  confirmPendingPairing: () => void;
  diagnostics: string;
  isSettingsOpen: boolean;
  isPairingQrReading: boolean;
  manualReconnectProgress?: "reconnecting" | "connected" | undefined;
  message: string;
  pairingDeviceName: string;
  pairingDeviceNamePlaceholder: string;
  pairingStatusMessage: string;
  pendingPairing: boolean;
  reconnectablePcs: PcProfile[];
  scanFeedback: string | null;
  scanPairingQr: () => void;
  setPairingDeviceName: (name: string) => void;
  state: ConnectionState;
  tryManualReconnect: () => void;
  tryReconnectPc: (pcId: string) => void;
  unavailableTransportMode?: PcProfile["transportMode"];
  usesLivePairingQr: boolean;
}

export function PairingGate({
  activePc,
  connectManualHost,
  confirmPendingPairing,
  diagnostics,
  isSettingsOpen,
  isPairingQrReading,
  manualReconnectProgress,
  message,
  pairingDeviceName,
  pairingDeviceNamePlaceholder,
  pairingStatusMessage,
  pendingPairing,
  reconnectablePcs,
  scanFeedback,
  scanPairingQr,
  setPairingDeviceName,
  state,
  tryManualReconnect,
  tryReconnectPc,
  unavailableTransportMode,
  usesLivePairingQr,
}: PairingGateProps) {
  const pairingQrActionLabel = usesLivePairingQr ? "Scan QR code" : "Take photo of QR code";
  const newPairingQrActionLabel = usesLivePairingQr
    ? "Scan new QR code"
    : "Take photo of new QR code";
  const [selectedReconnectPcId, setSelectedReconnectPcId] = useState("");
  const selectedReconnectPc =
    reconnectablePcs.find((pc) => pc.id === selectedReconnectPcId) ?? reconnectablePcs[0] ?? null;

  if (isSettingsOpen) {
    return null;
  }

  if (manualReconnectProgress !== undefined && activePc) {
    return (
      <PairingStatus
        activePcUnavailable
        blocksAppInteraction
        connectionProgress={manualReconnectProgress}
        message={message}
        onPrimaryAction={tryManualReconnect}
        pcName={getPcDisplayName(activePc)}
        transportMode={activePc.transportMode}
      />
    );
  }

  if (state === "needs-pairing" || state === "disconnected") {
    const canReconnectSavedPc = !pendingPairing && selectedReconnectPc !== null;
    return (
      <PairingStatus
        blocksAppInteraction
        diagnostics={diagnostics}
        deviceName={pendingPairing ? pairingDeviceName : undefined}
        deviceNamePlaceholder={pendingPairing ? pairingDeviceNamePlaceholder : undefined}
        heading={state === "disconnected" ? "PC disconnected" : undefined}
        message={
          pendingPairing
            ? "Confirm the device name shown on the PC, or change it before pairing."
            : pairingStatusMessage
        }
        onDeviceNameChange={pendingPairing ? setPairingDeviceName : undefined}
        onPrimaryAction={
          pendingPairing
            ? confirmPendingPairing
            : canReconnectSavedPc
              ? () => {
                  tryReconnectPc(selectedReconnectPc.id);
                }
              : scanPairingQr
        }
        readingQr={isPairingQrReading ? (canReconnectSavedPc ? "secondary" : "primary") : undefined}
        onSecondaryAction={canReconnectSavedPc ? scanPairingQr : undefined}
        onManualHostSubmit={pendingPairing ? undefined : connectManualHost}
        primaryLabel={
          pendingPairing ? "Pair" : canReconnectSavedPc ? "Try reconnect" : pairingQrActionLabel
        }
        savedPcOptions={
          canReconnectSavedPc
            ? reconnectablePcs.map((pc) => ({ id: pc.id, label: getPcDisplayName(pc) }))
            : undefined
        }
        scanFeedback={canReconnectSavedPc ? scanFeedback : null}
        secondaryLabel={canReconnectSavedPc ? pairingQrActionLabel : undefined}
        selectedSavedPcId={selectedReconnectPc?.id}
        usesLivePairingQr={usesLivePairingQr}
        onSavedPcChange={setSelectedReconnectPcId}
      />
    );
  }

  if (state === "rejected") {
    return (
      <PairingStatus
        blocksAppInteraction
        diagnostics={diagnostics}
        message={message}
        onPrimaryAction={scanPairingQr}
        onManualHostSubmit={connectManualHost}
        primaryLabel={newPairingQrActionLabel}
        readingQr={isPairingQrReading ? "primary" : undefined}
        scanFeedback={scanFeedback}
      />
    );
  }

  if (state !== "unavailable") {
    return null;
  }

  return (
    <PairingStatus
      activePcUnavailable
      blocksAppInteraction
      diagnostics={diagnostics}
      message={message}
      onPrimaryAction={activePc ? tryManualReconnect : scanPairingQr}
      onSecondaryAction={activePc ? scanPairingQr : undefined}
      onManualHostSubmit={connectManualHost}
      primaryLabel={activePc ? undefined : newPairingQrActionLabel}
      readingQr={isPairingQrReading ? (activePc ? "secondary" : "primary") : undefined}
      scanFeedback={scanFeedback}
      secondaryLabel={newPairingQrActionLabel}
      transportMode={activePc ? activePc.transportMode : unavailableTransportMode}
      usesLivePairingQr={usesLivePairingQr}
    />
  );
}
