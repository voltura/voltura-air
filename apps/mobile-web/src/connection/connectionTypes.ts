export type ConnectionState = "connecting" | "paired" | "needs-pairing" | "rejected" | "disconnected" | "unavailable";

export type ConnectionError = {
  code: string;
  message: string;
};

export type PairingAttempt = {
  token?: string;
  id: number;
};
