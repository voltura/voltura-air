export type ConnectionState = "connecting" | "paired" | "needs-pairing" | "rejected" | "disconnected" | "unavailable";

export interface ConnectionError {
  code: string;
  message: string;
}

export interface PairingAttempt {
  token: string | undefined;
  id: number;
}
