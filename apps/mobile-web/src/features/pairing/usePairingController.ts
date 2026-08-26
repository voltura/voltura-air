import {
  useEffect,
  useRef,
  useState,
  type ChangeEvent,
  type Dispatch,
  type SetStateAction,
} from "react";
import {
  parsePairingLink,
  type ManualConnectionTarget,
  type PairingLink,
} from "../../foundation/pairing/pairingLink";
import { getDefaultDeviceName } from "../../foundation/platform/clientEnvironment";
import { canUseLivePairingQrScanner, decodePairingQrImage } from "./pairingQrCapability";

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
  const {
    beginNewPairing,
    connectManualPc,
    deviceName,
    initialPairing,
    message,
    pairWithToken,
    setIsSettingsOpen,
  } = options;
  const [pendingPairing, setPendingPairing] = useState<PairingLink | null>(initialPairing);
  const defaultScanMessage = "Scan the QR code shown on your PC to pair this device.";
  const [pairingFeedback, setPairingFeedback] = useState({
    sourceMessage: message,
    scanMessage: null as string | null,
  });
  const currentScanMessage =
    pairingFeedback.sourceMessage === message ? pairingFeedback.scanMessage : null;
  const pairingScanMessage = currentScanMessage ?? defaultScanMessage;
  const pairingStatusMessage = (currentScanMessage ?? message.trim()) || defaultScanMessage;
  const setPairingScanMessage = (scanMessage: string) => {
    setPairingFeedback({ sourceMessage: message, scanMessage });
  };
  const pairingDeviceNamePlaceholder = getDefaultDeviceName();
  const [pairingDeviceName, setPairingDeviceName] = useState(
    deviceName === pairingDeviceNamePlaceholder ? "" : deviceName,
  );
  const pairingQrInputRef = useRef<HTMLInputElement | null>(null);
  const scanGenerationRef = useRef(0);
  const readingQrRef = useRef(false);
  const [isPairingQrReading, setIsPairingQrReading] = useState(false);
  const [livePairingScannerAttempt, setLivePairingScannerAttempt] = useState<number | null | false>(
    null,
  );
  const usesLivePairingQr = canUseLivePairingQrScanner() && livePairingScannerAttempt !== false;

  useEffect(
    () => () => {
      scanGenerationRef.current += 1;
    },
    [],
  );

  const acceptScannedPairingText = (scannedText: string, scanGeneration: number): boolean => {
    if (scanGenerationRef.current !== scanGeneration) {
      return false;
    }

    const pairingInfo = parsePairingLink(scannedText);
    if (!pairingInfo) {
      setPairingScanMessage("No Voltura Air pairing link found in that QR code.");
      return false;
    }

    beginNewPairing();
    setLivePairingScannerAttempt(null);
    setPendingPairing(pairingInfo);
    setPairingDeviceName(deviceName === pairingDeviceNamePlaceholder ? "" : deviceName);
    setPairingScanMessage("Confirm the device name shown on the PC, or change it before pairing.");
    setIsSettingsOpen(false);
    return true;
  };

  const confirmPendingPairing = () => {
    if (!pendingPairing) {
      return;
    }

    const name = pairingDeviceName.trim() || pairingDeviceNamePlaceholder;
    setPairingScanMessage("Connecting...");
    setPendingPairing(null);
    setIsSettingsOpen(false);
    pairWithToken(pendingPairing.pairToken, pendingPairing.pcUrl, name);
  };

  const connectManualHost = (target: ManualConnectionTarget) => {
    if (target.kind === "pairing") {
      beginNewPairing();
      setPendingPairing({ pairToken: target.pairToken, pcUrl: target.pcUrl });
      setPairingDeviceName(deviceName === pairingDeviceNamePlaceholder ? "" : deviceName);
      setPairingScanMessage(
        "Confirm the device name shown on the PC, or change it before pairing.",
      );
      setIsSettingsOpen(false);
      return;
    }

    connectManualPc(target.pcUrl);
    setPendingPairing(null);
    setIsSettingsOpen(false);
    setPairingScanMessage("Connecting to manually entered PC...");
  };

  const scanPairingQr = () => {
    if (readingQrRef.current || typeof livePairingScannerAttempt === "number") {
      return;
    }
    if (usesLivePairingQr) {
      const scanGeneration = scanGenerationRef.current + 1;
      scanGenerationRef.current = scanGeneration;
      setPairingScanMessage("Allow camera access to scan the QR code shown on the PC.");
      setLivePairingScannerAttempt(scanGeneration);
      return;
    }

    pairingQrInputRef.current?.click();
  };

  const acceptLivePairingQr = (attemptId: number, scannedText: string): boolean =>
    acceptScannedPairingText(scannedText, attemptId);

  const fallbackFromLivePairingQr = (
    attemptId: number,
    scanMessage: string,
    openPhoto: boolean,
  ) => {
    if (scanGenerationRef.current !== attemptId) {
      return;
    }

    scanGenerationRef.current += 1;
    setLivePairingScannerAttempt(false);
    setPairingScanMessage(scanMessage);
    if (openPhoto) {
      pairingQrInputRef.current?.click();
    }
  };

  const onPairingQrSelected = async (event: ChangeEvent<HTMLInputElement>) => {
    const scanGeneration = scanGenerationRef.current + 1;
    scanGenerationRef.current = scanGeneration;
    const file = event.target.files?.[0];
    event.target.value = "";
    if (!file) {
      return;
    }

    readingQrRef.current = true;
    setIsPairingQrReading(true);
    try {
      setPairingScanMessage("Reading QR code...");

      let scannedText: string;
      try {
        scannedText = await decodePairingQrImage(file);
      } catch (decodeError) {
        if (scanGenerationRef.current !== scanGeneration) {
          return;
        }
        console.error("QR decode error", decodeError, { name: file.name, type: file.type });
        setPairingScanMessage(
          "Could not read the QR code. Try zooming in, retaking the picture, or scanning a new code.",
        );
        return;
      }

      if (scanGenerationRef.current !== scanGeneration) {
        return;
      }

      if (!scannedText) {
        setPairingScanMessage(
          "Could not read the QR code. Try zooming in, retaking the picture, or scanning a new code.",
        );
        return;
      }

      acceptScannedPairingText(scannedText, scanGeneration);
    } catch (error) {
      if (scanGenerationRef.current !== scanGeneration) {
        return;
      }
      console.error("Pairing QR scan failed", error, { name: file.name, type: file.type });
      setPairingScanMessage(
        "Could not read the QR code. Try zooming in, retaking the picture, or scanning a new code.",
      );
    } finally {
      if (scanGenerationRef.current === scanGeneration) {
        readingQrRef.current = false;
        setIsPairingQrReading(false);
      }
    }
  };

  return {
    acceptLivePairingQr,
    confirmPendingPairing,
    connectManualHost,
    fallbackFromLivePairingQr,
    onPairingQrSelected,
    isPairingQrReading,
    livePairingScannerAttempt:
      typeof livePairingScannerAttempt === "number" ? livePairingScannerAttempt : null,
    pairingDeviceName,
    pairingDeviceNamePlaceholder,
    pairingQrInputRef,
    pairingScanMessage,
    pairingStatusMessage,
    pendingPairing,
    scanPairingQr,
    setPairingDeviceName,
    usesLivePairingQr,
  };
}
