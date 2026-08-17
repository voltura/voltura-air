import { useEffect, useRef, type RefObject } from "react";
import { saveActivePcId, savePcProfiles, type PcProfile } from "./pcProfiles";
import { clearPairTokenFromAddress, deviceNameKey, ensureClientMetadataInAddress } from "./clientIdentity";
import type { PairingAttempt } from "./connectionTypes";
import { writeLocalStorage } from "../platform/browserStorage";

interface ConnectionPersistenceOptions {
  activePcId: string | null;
  clientId: string;
  deviceName: string;
  deviceNameRef: RefObject<string>;
  hasInitialPairing: boolean;
  pairedPcs: PcProfile[];
  pairingAttempt: PairingAttempt;
  pairingAttemptRef: RefObject<PairingAttempt>;
}

export function useConnectionPersistence(options: ConnectionPersistenceOptions): void {
  const { activePcId, clientId, deviceName, deviceNameRef, hasInitialPairing, pairedPcs, pairingAttempt, pairingAttemptRef } = options;
  const initialPairingPendingRef = useRef(hasInitialPairing);

  useEffect(() => {
    ensureClientMetadataInAddress(clientId, deviceName);
  }, [clientId, deviceName]);

  useEffect(() => {
    if (hasInitialPairing) {
      clearPairTokenFromAddress();
    }
  }, [hasInitialPairing]);

  useEffect(() => {
    deviceNameRef.current = deviceName;
    writeLocalStorage(deviceNameKey, deviceName);
  }, [deviceName, deviceNameRef]);

  useEffect(() => {
    pairingAttemptRef.current = pairingAttempt;
  }, [pairingAttempt, pairingAttemptRef]);

  useEffect(() => { savePcProfiles(pairedPcs); }, [pairedPcs]);
  useEffect(() => {
    if (initialPairingPendingRef.current) {
      if (activePcId === null) {return;}
      initialPairingPendingRef.current = false;
    }
    saveActivePcId(activePcId);
  }, [activePcId]);
}
