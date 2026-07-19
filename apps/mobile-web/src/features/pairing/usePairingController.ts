import { useEffect, useRef, useState, type ChangeEvent, type Dispatch, type SetStateAction } from "react";
import {
  parsePairingLink,
  type ManualConnectionTarget,
  type PairingLink
} from "../../foundation/pairing/pairingLink";
import { decodeQrImage } from "../../foundation/pairing/qrCode";

interface PairingControllerOptions {
  beginNewPairing: () => void;
  connectManualPc: (target: string) => void;
  deviceName: string;
  initialPairing: PairingLink | null;
  message: string;
  pairWithToken: (token: string, pcUrl?: string, requestedDeviceName?: string) => void;
  setIsSettingsOpen: Dispatch<SetStateAction<boolean>>;
}

export function usePairingController(options: PairingControllerOptions) {
  const { beginNewPairing, connectManualPc, deviceName, initialPairing, message, pairWithToken, setIsSettingsOpen } = options;
  const [pendingPairing, setPendingPairing] = useState<PairingLink | null>(initialPairing);
  const defaultScanMessage = "Scan the QR code shown on your PC to pair this device.";
  const [pairingFeedback, setPairingFeedback] = useState({
    sourceMessage: message,
    scanMessage: null as string | null
  });
  const currentScanMessage = pairingFeedback.sourceMessage === message ? pairingFeedback.scanMessage : null;
  const pairingScanMessage = currentScanMessage ?? defaultScanMessage;
  const pairingStatusMessage = (currentScanMessage ?? message.trim()) || defaultScanMessage;
  const setPairingScanMessage = (scanMessage: string) => {
    setPairingFeedback({ sourceMessage: message, scanMessage });
  };
  const [enteredPairingDeviceName, setPairingDeviceName] = useState(deviceName);
  const pairingDeviceName = enteredPairingDeviceName || deviceName;
  const pairingQrInputRef = useRef<HTMLInputElement | null>(null);
  const scanGenerationRef = useRef(0);

  useEffect(() => () => { scanGenerationRef.current += 1; }, []);

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

  const connectManualHost = (target: ManualConnectionTarget) => {
    if (target.kind === "pairing") {
      beginNewPairing();
      setPendingPairing({ pairToken: target.pairToken, pcUrl: target.pcUrl });
      setPairingDeviceName(deviceName);
      setPairingScanMessage("Confirm the device name shown on the PC, or change it before pairing.");
      setIsSettingsOpen(false);
      return;
    }

    connectManualPc(target.pcUrl);
    setPendingPairing(null);
    setIsSettingsOpen(false);
    setPairingScanMessage("Connecting to manually entered PC...");
  };

  const scanPairingQr = () => pairingQrInputRef.current?.click();

  const onPairingQrSelected = async (event: ChangeEvent<HTMLInputElement>) => {
    const scanGeneration = scanGenerationRef.current + 1;
    scanGenerationRef.current = scanGeneration;
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
        if (scanGenerationRef.current !== scanGeneration) {
          return;
        }
        console.error("QR decode error", decodeError, { name: file.name, type: file.type });
        setPairingScanMessage("Could not read the QR code. Try zooming in, retaking the picture, or scanning a new code.");
        return;
      }

      if (scanGenerationRef.current !== scanGeneration) {
        return;
      }

      if (!scannedText) {
        setPairingScanMessage("Could not read the QR code. Try zooming in, retaking the picture, or scanning a new code.");
        return;
      }

      const pairingInfo = parsePairingLink(scannedText);
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
      if (scanGenerationRef.current !== scanGeneration) {
        return;
      }
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
    pairingStatusMessage,
    pendingPairing,
    scanPairingQr,
    setPairingDeviceName
  };
}
