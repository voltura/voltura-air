export const relayProtocolVersion = 1;
export const routeIdPattern = /^[A-Za-z0-9_-]{22}$/;
export const maximumInnerMessageBytes = 64 * 1024;
export const relayEncryptionOverheadBytes = 26;
export const maximumRelayPayloadBytes = maximumInnerMessageBytes + relayEncryptionOverheadBytes;
export const maximumDevicesPerRoom = 64;
export const maximumControlMessageBytes = 4 * 1024;
export const maximumBufferedBytes = 256 * 1024;
export const relayHostTranscriptPrefix = "VolturaAir relay host:v1";

export const relayClose = {
  invalid: 4400,
  unauthorized: 4401,
  conflict: 4409,
  tooLarge: 4409,
  unavailable: 4410,
  overloaded: 4413
} as const;
