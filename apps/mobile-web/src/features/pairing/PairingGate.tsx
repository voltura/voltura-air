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
  manualReconnectProgress?: "reconnecting" | "connected" | undefined;
  message: string;
  pairingDeviceName: string;
  pairingStatusMessage: string;
  pendingPairing: boolean;
  reconnectablePcs: PcProfile[];
  scanPairingQr: () => void;
  setPairingDeviceName: (name: string) => void;
  state: ConnectionState;
  tryManualReconnect: () => void;
  tryReconnectPc: (pcId: string) => void;
}

export function PairingGate({
  activePc,
  connectManualHost,
  confirmPendingPairing,
  diagnostics,
  isSettingsOpen,
  manualReconnectProgress,
  message,
  pairingDeviceName,
  pairingStatusMessage,
  pendingPairing,
  reconnectablePcs,
  scanPairingQr,
  setPairingDeviceName,
  state,
  tryManualReconnect,
  tryReconnectPc
}: PairingGateProps) {
  const [selectedReconnectPcId, setSelectedReconnectPcId] = useState("");
  const selectedReconnectPc = reconnectablePcs.find((pc) => pc.id === selectedReconnectPcId) ?? reconnectablePcs[0] ?? null;

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
        heading={state === "disconnected" ? "PC disconnected" : undefined}
        message={pendingPairing ? "Confirm the device name shown on the PC, or change it before pairing." : pairingStatusMessage}
        onDeviceNameChange={pendingPairing ? setPairingDeviceName : undefined}
        onPrimaryAction={pendingPairing
          ? confirmPendingPairing
          : canReconnectSavedPc
            ? () => { tryReconnectPc(selectedReconnectPc.id); }
            : scanPairingQr}
        onSecondaryAction={canReconnectSavedPc ? scanPairingQr : undefined}
        onManualHostSubmit={connectManualHost}
        primaryLabel={pendingPairing ? "Pair" : canReconnectSavedPc ? "Try reconnect" : undefined}
        savedPcOptions={canReconnectSavedPc
          ? reconnectablePcs.map((pc) => ({ id: pc.id, label: getPcDisplayName(pc) }))
          : undefined}
        secondaryLabel={canReconnectSavedPc ? "Take photo of QR code" : undefined}
        selectedSavedPcId={selectedReconnectPc?.id}
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
        primaryLabel="Take photo of new QR code"
      />
    );
  }

  if (!activePc) {
    return null;
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
      onPrimaryAction={tryManualReconnect}
      onSecondaryAction={scanPairingQr}
      onManualHostSubmit={connectManualHost}
    />
  );
}
