import type { ControllerSocket } from "./controllerSocket";

export type ConnectionFailureTrigger =
  | "connection-failure"
  | "health-send-failed"
  | "health-timeout"
  | "socket-close"
  | "socket-error";

interface ConnectionFailure {
  timestamp: string;
  code: string;
  trigger: ConnectionFailureTrigger;
  visibility: DocumentVisibilityState;
  readyState: ControllerSocket["readyState"] | null;
  bufferedBytes: number;
  lastHealthyAgeMs: number | null;
}

const failures: ConnectionFailure[] = [];

// In-memory metadata only: no traffic, credentials, URLs, or persistent storage.
export function recordConnectionFailure(
  code: string,
  trigger: ConnectionFailureTrigger,
  socket: ControllerSocket | undefined,
  lastHealthyAt: number,
) {
  const now = Date.now();
  failures.push({
    timestamp: new Date(now).toISOString(),
    code,
    trigger,
    visibility: document.visibilityState,
    readyState: socket?.readyState ?? null,
    bufferedBytes: socket?.bufferedAmount ?? 0,
    lastHealthyAgeMs: lastHealthyAt > 0 ? Math.max(0, now - lastHealthyAt) : null,
  });
  if (failures.length > 20) {
    failures.shift();
  }
}

export function getConnectionDiagnostics() {
  return { failures: failures.map((failure) => ({ ...failure })) };
}
