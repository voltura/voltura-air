import { useEffect } from "react";
import { Camera, Power, RefreshCw } from "lucide-react";

const pcUnavailableMessage = "PC not available. Make sure Voltura Air is running and both devices are on the same Wi-Fi/LAN. Retrying…";

 type PairingStatusProps = {
  activePcUnavailable?: boolean;
  deviceName?: string;
  message: string;
  onDeviceNameChange?: (deviceName: string) => void;
  onPrimaryAction: () => void;
  onSecondaryAction?: () => void;
  primaryLabel?: string;
};

export function PairingStatus({
  activePcUnavailable = false,
  deviceName,
  message,
  onDeviceNameChange,
  onPrimaryAction,
  onSecondaryAction,
  primaryLabel
}: PairingStatusProps) {
  useEffect(() => {
    if (!activePcUnavailable) {
      return;
    }

    if (document.activeElement instanceof HTMLElement) {
      document.activeElement.blur();
    }
  }, [activePcUnavailable]);

  if (activePcUnavailable) {
    return (
      <>
        <div className="pairing-backdrop" aria-hidden="true" />
        <section className="pairing-required connection-unavailable" role="status" aria-live="polite">
          <Power aria-hidden="true" />
          <h1>PC not available</h1>
          <p>{pcUnavailableMessage}</p>
          <button type="button" onClick={onPrimaryAction}>
            <RefreshCw aria-hidden="true" />
            <span>{primaryLabel ?? "Try again"}</span>
          </button>
          {onSecondaryAction && (
            <button type="button" onClick={onSecondaryAction}>
              <Camera aria-hidden="true" />
              <span>Take photo of another QR code</span>
            </button>
          )}
        </section>
      </>
    );
  }

  return (
    <section className="pairing-required" role="status" aria-live="polite">
      <Camera aria-hidden="true" />
      <h1>Pair this app</h1>
      <p>{message}</p>
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
      <button type="button" onClick={onPrimaryAction}>
        <Camera aria-hidden="true" />
        <span>{primaryLabel ?? "Take photo of QR code"}</span>
      </button>
    </section>
  );
}
