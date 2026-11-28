import { useMemo } from "react";
import { isScreenshotMode } from "../platform/clientEnvironment";
import {
  hasPairingTokenParameter,
  isDirectPairingPath,
  parseHostedConnectionAddress,
  parsePairingLink,
  parsePcUrl,
} from "../pairing/pairingLink";
import {
  createPcProfile,
  getEffectiveStoredActivePcId,
  loadActivePcId,
  loadPcProfiles,
} from "./pcProfiles";
import { getClientIdFromAddress, getOrCreateClientId, hasPcUrlInAddress } from "./clientIdentity";

export function useInitialConnectionProfileState() {
  const screenshotMode = useMemo(() => isScreenshotMode(window.location.href), []);
  const addressClientId = useMemo(() => getClientIdFromAddress(window.location.href), []);
  const addressPcUrl = useMemo(() => parsePcUrl(window.location.href, window.location.origin), []);
  const hostedAddress = useMemo(() => parseHostedConnectionAddress(window.location.href), []);
  const initialPairing = useMemo(() => {
    if (hostedAddress?.pairToken) {
      return { pairToken: hostedAddress.pairToken, pcUrl: hostedAddress.pcUrl };
    }
    const address = new URL(window.location.href);
    if (
      isDirectPairingPath(address.pathname) &&
      address.searchParams.getAll("refresh").length === 1
    ) {
      address.searchParams.delete("refresh");
    }
    return parsePairingLink(address.href);
  }, [hostedAddress]);
  const addressHasInvalidPairing = useMemo(
    () => initialPairing === null && hasPairingTokenParameter(window.location.href),
    [initialPairing],
  );
  const addressHasPcUrl = useMemo(
    () => hostedAddress !== null || hasPcUrlInAddress(window.location.href),
    [hostedAddress],
  );
  const addressPcProfile = useMemo(
    () => createPcProfile(initialPairing?.pcUrl ?? hostedAddress?.pcUrl ?? addressPcUrl),
    [addressPcUrl, hostedAddress?.pcUrl, initialPairing?.pcUrl],
  );
  const storedPcProfiles = useMemo(() => loadPcProfiles(), []);
  const storedActivePcId = useMemo(() => loadActivePcId(), []);
  const hostedAddressIsStored =
    hostedAddress === null ||
    storedPcProfiles.some((profile) => profile.id === addressPcProfile.id);
  const effectiveStoredActivePcId = useMemo(
    () =>
      getEffectiveStoredActivePcId(
        storedActivePcId,
        storedPcProfiles,
        addressPcProfile.id,
        window.location.href,
      ),
    [addressPcProfile.id, storedActivePcId, storedPcProfiles],
  );
  const hasOnlyAddressIdentity =
    addressClientId !== null && storedActivePcId === null && storedPcProfiles.length === 0;
  const clientId = useMemo(() => getOrCreateClientId(window.location.href), []);
  // The Direct pairing page keeps its routing hint after the token is cleared.
  // On reload, only the saved active profile should drive a connection from that page.
  const shouldUseAddressPc =
    !isDirectPairingPath(window.location.pathname) &&
    !addressHasInvalidPairing &&
    initialPairing === null &&
    hostedAddressIsStored &&
    (addressHasPcUrl || hasOnlyAddressIdentity);

  return {
    addressPcProfile,
    clientId,
    effectiveStoredActivePcId,
    initialPairing,
    screenshotMode,
    shouldActivateAddressPc: shouldUseAddressPc,
    shouldStoreAddressPc: shouldUseAddressPc,
    storedPcProfiles,
  };
}
