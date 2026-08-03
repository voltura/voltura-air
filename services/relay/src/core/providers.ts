export interface RelayPublicEndpointProvider {
  serviceId: string;
  httpsBase: string;
  webSocketBase: string;
  supportsTurn: boolean;
}

export interface RelayTurnCredentialProvider {
  issue(routeId: string): Promise<{
    allowed: boolean;
    expiresAt: string;
    iceServers: { urls: string[]; username: string; credential: string }[];
  }>;
}

export interface RelayMonthlyUsageProvider {
  readCurrentMonth(): Promise<{
    bytes: number;
    checkedAt: string;
    warningBytes: number | null;
    cutoffBytes: number | null;
  }>;
}

export interface RelaySocketLifecycleProvider {
  registerHost(routeId: string, publicKey: string): Promise<void>;
  registerDevice(routeId: string, sessionId: Uint8Array): Promise<void>;
  disconnect(routeId: string, sessionId?: Uint8Array): Promise<void>;
}
