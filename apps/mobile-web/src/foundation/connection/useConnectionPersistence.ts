import { useEffect, type RefObject } from "react";
import { saveActivePcId, savePcProfiles, type PcProfile } from "./pcProfiles";
import { clearPairTokenFromAddress, deviceNameKey, ensureClientMetadataInAddress } from "./clientIdentity";
import type { PairingAttempt } from "./connectionTypes";

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
    localStorage.setItem(deviceNameKey, deviceName);
  }, [deviceName, deviceNameRef]);

  useEffect(() => {
    pairingAttemptRef.current = pairingAttempt;
  }, [pairingAttempt, pairingAttemptRef]);

  useEffect(() => { savePcProfiles(pairedPcs); }, [pairedPcs]);
  useEffect(() => { saveActivePcId(activePcId); }, [activePcId]);
}
