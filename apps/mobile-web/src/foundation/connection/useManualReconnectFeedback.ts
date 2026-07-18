import { useCallback, useEffect, useState } from "react";
import type { ConnectionState } from "./connectionTypes";

export type ManualReconnectProgress = "reconnecting" | "connected";

interface ManualReconnectAttempt {
  pcId: string;
  hasStarted: boolean;
}

const successDisplayMs = 500;

export function useManualReconnectFeedback(
  activePcId: string | null,
  state: ConnectionState,
  selectPc: (pcId: string) => void
) {
  const [attempt, setAttempt] = useState<ManualReconnectAttempt | null>(null);

  if (attempt?.pcId === activePcId && state === "connecting" && !attempt.hasStarted) {
    setAttempt({ ...attempt, hasStarted: true });
  } else if (attempt && (
    attempt.pcId !== activePcId || state === "rejected" ||
    (state === "needs-pairing" && attempt.hasStarted) ||
    (state === "unavailable" && attempt.hasStarted)
  )) {
    setAttempt(null);
  }

  const progress: ManualReconnectProgress | undefined = attempt?.pcId !== activePcId ||
    state === "rejected" || (state === "needs-pairing" && attempt.hasStarted)
    ? undefined
    : state === "paired" ? "connected" : "reconnecting";

  useEffect(() => {
    if (progress !== "connected") {
      return;
    }

    const timeout = window.setTimeout(() => { setAttempt(null); }, successDisplayMs);
    return () => { window.clearTimeout(timeout); };
  }, [progress]);

  const reconnect = useCallback((pcId: string | null = activePcId) => {
    if (!pcId) {
      return;
    }

    setAttempt({ pcId, hasStarted: false });
    selectPc(pcId);
  }, [activePcId, selectPc]);

  return {
    progress,
    reconnect
  };
}
