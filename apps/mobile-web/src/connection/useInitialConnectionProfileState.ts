import { useMemo } from "react";
import { isScreenshotMode } from "../clientEnvironment";
import { parsePairingLink, parsePcUrl } from "../pairingLink";
import {
  createPcProfile,
  getEffectiveStoredActivePcId,
  loadActivePcId,
  loadPcProfiles
} from "../pcProfiles";
import { getClientIdFromAddress, getOrCreateClientId, hasPcUrlInAddress } from "./clientIdentity";

export function useInitialConnectionProfileState() {
  const screenshotMode = useMemo(() => isScreenshotMode(window.location.href), []);
  const addressClientId = useMemo(() => getClientIdFromAddress(window.location.href), []);
  const addressPcUrl = useMemo(() => parsePcUrl(window.location.href, window.location.origin), []);
  const initialPairing = useMemo(() => parsePairingLink(window.location.href, window.location.origin), []);
  const addressHasPcUrl = useMemo(() => hasPcUrlInAddress(window.location.href), []);
  const addressPcProfile = useMemo(() => createPcProfile(initialPairing?.pcUrl ?? addressPcUrl), [addressPcUrl, initialPairing?.pcUrl]);
  const storedPcProfiles = useMemo(() => loadPcProfiles(), []);
  const storedActivePcId = useMemo(() => loadActivePcId(), []);
  const effectiveStoredActivePcId = useMemo(
    () => getEffectiveStoredActivePcId(storedActivePcId, storedPcProfiles, addressPcProfile.id, window.location.href),
    [addressPcProfile.id, storedActivePcId, storedPcProfiles]
  );
  const hasOnlyAddressIdentity = addressClientId !== null && storedActivePcId === null && storedPcProfiles.length === 0;
  const clientId = useMemo(() => getOrCreateClientId(window.location.href), []);

  return {
    addressPcProfile,
    clientId,
    effectiveStoredActivePcId,
    initialPairing,
    screenshotMode,
    shouldActivateAddressPc: initialPairing === null && (addressHasPcUrl || hasOnlyAddressIdentity),
    shouldStoreAddressPc: initialPairing !== null || addressHasPcUrl || hasOnlyAddressIdentity,
    storedPcProfiles
  };
}
