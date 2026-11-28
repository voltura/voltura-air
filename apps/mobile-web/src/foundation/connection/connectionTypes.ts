import type { PcProfile } from "./pcProfiles";

export type ConnectionState =
  | "connecting"
  | "paired"
  | "needs-pairing"
  | "rejected"
  | "disconnected"
  | "unavailable";

export interface ConnectionError {
  code: string;
  message: string;
  transportMode?: PcProfile["transportMode"];
}

export interface PairingAttempt {
  token: string | undefined;
  id: number;
}
