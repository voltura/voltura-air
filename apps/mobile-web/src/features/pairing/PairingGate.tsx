import type { ConnectionState } from "../../connection/connectionTypes";
import { getPcDisplayName } from "../../pcDisplayName";
import type { PcProfile } from "../../pcProfiles";
import { PairingStatus } from "./PairingStatus";

interface PairingGateProps {
  activePc: PcProfile | null;
  connectManualHost: (target: string) => void;
  confirmPendingPairing: () => void;
  diagnostics: string;
  isSettingsOpen: boolean;
  manualReconnectProgress?: "reconnecting" | "connected" | undefined;
  message: string;
  pairingDeviceName: string;
  pairingScanMessage: string;
  pendingPairing: boolean;
  scanPairingQr: () => void;
  setPairingDeviceName: (name: string) => void;
  state: ConnectionState;
  tryManualReconnect: () => void;
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
  pairingScanMessage,
  pendingPairing,
  scanPairingQr,
  setPairingDeviceName,
  state,
  tryManualReconnect
}: PairingGateProps) {
  if (isSettingsOpen) {
    return null;
  }

  if (state === "needs-pairing") {
    return (
      <PairingStatus
        diagnostics={diagnostics}
        deviceName={pendingPairing ? pairingDeviceName : undefined}
        message={pendingPairing ? "Confirm the device name shown on the PC, or change it before pairing." : pairingScanMessage}
        onDeviceNameChange={pendingPairing ? setPairingDeviceName : undefined}
        onPrimaryAction={pendingPairing ? confirmPendingPairing : scanPairingQr}
        onManualHostSubmit={connectManualHost}
        primaryLabel={pendingPairing ? "Pair" : undefined}
      />
    );
  }

  if (state === "rejected") {
    return (
      <PairingStatus
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

  if (manualReconnectProgress !== undefined) {
    return (
      <PairingStatus
        activePcUnavailable
        connectionProgress={manualReconnectProgress}
        message={message}
        onPrimaryAction={tryManualReconnect}
        pcName={getPcDisplayName(activePc)}
      />
    );
  }

  if (state !== "unavailable") {
    return null;
  }

  return (
    <PairingStatus
      activePcUnavailable
      diagnostics={diagnostics}
      message={message}
      onPrimaryAction={tryManualReconnect}
      onSecondaryAction={scanPairingQr}
      onManualHostSubmit={connectManualHost}
    />
  );
}
