import { useRef, useState, type ChangeEvent, type Dispatch, type SetStateAction } from "react";
import type { ConnectionState } from "../../connection/connectionTypes";
import { parsePairingLink, type PairingLink } from "../../pairingLink";
import { decodeQrImage } from "../../qrCode";

interface PairingControllerOptions {
  beginNewPairing: () => void;
  connectManualPc: (target: string) => void;
  deviceName: string;
  initialPairing: PairingLink | null;
  message: string;
  pairWithToken: (token: string, pcUrl?: string, requestedDeviceName?: string) => void;
  setIsSettingsOpen: Dispatch<SetStateAction<boolean>>;
  state: ConnectionState;
}

export function usePairingController(options: PairingControllerOptions) {
  const { beginNewPairing, connectManualPc, deviceName, initialPairing, message, pairWithToken, setIsSettingsOpen, state } = options;
  const [pendingPairing, setPendingPairing] = useState<PairingLink | null>(initialPairing);
  const defaultScanMessage = message.trim() || "Scan the QR code shown on your PC to pair this home screen app.";
  const [pairingFeedback, setPairingFeedback] = useState({
    sourceMessage: message,
    scanMessage: defaultScanMessage
  });
  const pairingScanMessage = state === "needs-pairing" && !pendingPairing && pairingFeedback.sourceMessage !== message
    ? defaultScanMessage
    : pairingFeedback.scanMessage;
  const setPairingScanMessage = (scanMessage: string) => {
    setPairingFeedback({ sourceMessage: message, scanMessage });
  };
  const [enteredPairingDeviceName, setPairingDeviceName] = useState(deviceName);
  const pairingDeviceName = enteredPairingDeviceName || deviceName;
  const pairingQrInputRef = useRef<HTMLInputElement | null>(null);

  const confirmPendingPairing = () => {
    if (!pendingPairing) {
      return;
    }

    const name = pairingDeviceName.trim() || deviceName;
    setPairingScanMessage("Connecting...");
    setPendingPairing(null);
    setIsSettingsOpen(false);
    pairWithToken(pendingPairing.pairToken, pendingPairing.pcUrl, name);
  };

  const connectManualHost = (target: string) => {
    connectManualPc(target);
    setPendingPairing(null);
    setIsSettingsOpen(false);
    setPairingScanMessage("Connecting to manually entered PC...");
  };

  const scanPairingQr = () => pairingQrInputRef.current?.click();

  const onPairingQrSelected = async (event: ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    event.target.value = "";
    if (!file) {
      return;
    }

    try {
      setPairingScanMessage("Reading QR code...");

      let scannedText: string;
      try {
        scannedText = await decodeQrImage(file);
      } catch (decodeError) {
        console.error("QR decode error", decodeError, { name: file.name, type: file.type });
        setPairingScanMessage("Could not read the QR code. Try zooming in, retaking the picture, or scanning a new code.");
        return;
      }

      if (!scannedText) {
        setPairingScanMessage("Could not read the QR code. Try zooming in, retaking the picture, or scanning a new code.");
        return;
      }

      const pairingInfo = parsePairingLink(scannedText, window.location.origin);
      if (!pairingInfo) {
        setPairingScanMessage("No Voltura Air pairing link found in that QR code.");
        return;
      }

      beginNewPairing();
      setPendingPairing(pairingInfo);
      setPairingDeviceName(deviceName);
      setPairingScanMessage("Confirm the device name shown on the PC, or change it before pairing.");
      setIsSettingsOpen(false);
    } catch (error) {
      console.error("Pairing QR scan failed", error, { name: file.name, type: file.type });
      setPairingScanMessage("Could not read the QR code. Try zooming in, retaking the picture, or scanning a new code.");
    }
  };

  return {
    confirmPendingPairing,
    connectManualHost,
    onPairingQrSelected,
    pairingDeviceName,
    pairingQrInputRef,
    pairingScanMessage,
    pendingPairing,
    scanPairingQr,
    setPairingDeviceName
  };
}
