import {
  useEffect,
  useLayoutEffect,
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
  requestConnectionChange: (action: () => void) => void;
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
    requestConnectionChange,
    setIsSettingsOpen,
  } = options;
  const [pendingPairing, setPendingPairing] = useState<PairingLink | null>(initialPairing);
  const defaultScanMessage = "Scan the QR code shown on your PC to pair this device.";
  const [pairingFeedback, setPairingFeedback] = useState({
    sourceMessage: message,
    scanMessage: null as string | null,
  });
  const messageRef = useRef(message);
  useLayoutEffect(() => {
    messageRef.current = message;
  }, [message]);
  const currentScanMessage =
    pairingFeedback.sourceMessage === message ? pairingFeedback.scanMessage : null;
  const pairingScanMessage = currentScanMessage ?? defaultScanMessage;
  const pairingStatusMessage = (currentScanMessage ?? message.trim()) || defaultScanMessage;
  const setPairingScanMessage = (scanMessage: string) => {
    setPairingFeedback({ sourceMessage: messageRef.current, scanMessage });
  };
  const pairingDeviceNamePlaceholder = getDefaultDeviceName();
  const [pairingDeviceName, setPairingDeviceName] = useState(
    deviceName === pairingDeviceNamePlaceholder ? "" : deviceName,
  );
  const pairingQrInputRef = useRef<HTMLInputElement | null>(null);
  const scanGenerationRef = useRef(0);
  const photoAbortRef = useRef<AbortController | null>(null);
  const readingQrRef = useRef(false);
  const [isPairingQrReading, setIsPairingQrReading] = useState(false);
  const [livePairingScannerAttempt, setLivePairingScannerAttempt] = useState<number | null | false>(
    null,
  );
  const usesLivePairingQr = canUseLivePairingQrScanner() && livePairingScannerAttempt !== false;

  const retireScan = () => {
    scanGenerationRef.current += 1;
    photoAbortRef.current?.abort();
    photoAbortRef.current = null;
    readingQrRef.current = false;
    setIsPairingQrReading(false);
    setLivePairingScannerAttempt((current) => (current === false ? false : null));
    return scanGenerationRef.current;
  };

  const requestPairingAction = (action: () => void) => {
    const generation = retireScan();
    requestConnectionChange(() => {
      if (scanGenerationRef.current === generation) {
        scanGenerationRef.current += 1;
        action();
      }
    });
  };

  useEffect(
    () => () => {
      scanGenerationRef.current += 1;
      photoAbortRef.current?.abort();
    },
    [],
  );

  const showPairingConfirmation = (pairingInfo: PairingLink) => {
    beginNewPairing();
    setPendingPairing(pairingInfo);
    setPairingDeviceName(deviceName === pairingDeviceNamePlaceholder ? "" : deviceName);
    setPairingScanMessage("Confirm the device name shown on the PC, or change it before pairing.");
    setIsSettingsOpen(false);
  };

  const acceptScannedPairingText = (scannedText: string, scanGeneration: number): boolean => {
    if (scanGenerationRef.current !== scanGeneration) {
      return false;
    }

    const pairingInfo = parsePairingLink(scannedText);
    if (!pairingInfo) {
      setPairingScanMessage("No Voltura Air pairing link found in that QR code.");
      return false;
    }

    requestPairingAction(() => showPairingConfirmation(pairingInfo));
    return true;
  };

  const confirmPendingPairing = () => {
    if (!pendingPairing) {
      return;
    }

    const name = pairingDeviceName.trim() || pairingDeviceNamePlaceholder;
    requestPairingAction(() => {
      setPairingScanMessage("Connecting...");
      setPendingPairing(null);
      setIsSettingsOpen(false);
      pairWithToken(pendingPairing.pairToken, pendingPairing.pcUrl, name);
    });
  };

  const connectManualHost = (target: ManualConnectionTarget) => {
    requestPairingAction(() => {
      if (target.kind === "pairing") {
        showPairingConfirmation({ pairToken: target.pairToken, pcUrl: target.pcUrl });
        return;
      }

      connectManualPc(target.pcUrl);
      setPendingPairing(null);
      setIsSettingsOpen(false);
      setPairingScanMessage("Connecting to manually entered PC...");
    });
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
    const file = event.target.files?.[0];
    event.target.value = "";
    if (!file) {
      return;
    }
    const scanGeneration = scanGenerationRef.current + 1;
    scanGenerationRef.current = scanGeneration;
    photoAbortRef.current?.abort();
    const photoAbort = new AbortController();
    photoAbortRef.current = photoAbort;
    setLivePairingScannerAttempt((current) => (current === false ? false : null));

    readingQrRef.current = true;
    setIsPairingQrReading(true);
    try {
      setPairingScanMessage("Reading QR code...");

      let scannedText: string;
      try {
        scannedText = await decodePairingQrImage(file, photoAbort.signal);
      } catch {
        if (scanGenerationRef.current !== scanGeneration) {
          return;
        }
        console.error("QR image could not be read.");
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
    } catch {
      if (scanGenerationRef.current !== scanGeneration) {
        return;
      }
      console.error("Pairing QR scan could not be completed.");
      setPairingScanMessage(
        "Could not read the QR code. Try zooming in, retaking the picture, or scanning a new code.",
      );
    } finally {
      if (scanGenerationRef.current === scanGeneration) {
        photoAbortRef.current = null;
        readingQrRef.current = false;
        setIsPairingQrReading(false);
      }
    }
  };

  return {
    acceptLivePairingQr,
    cancelPairingScan: retireScan,
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
    pairingScanFeedback: currentScanMessage,
    pairingScanMessage,
    pairingStatusMessage,
    pendingPairing,
    scanPairingQr,
    setPairingDeviceName,
    usesLivePairingQr,
  };
}
