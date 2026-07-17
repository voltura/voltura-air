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
    attempt.pcId !== activePcId || state === "needs-pairing" || state === "rejected" ||
    (state === "unavailable" && attempt.hasStarted)
  )) {
    setAttempt(null);
  }

  const progress: ManualReconnectProgress | undefined = attempt?.pcId !== activePcId ||
    state === "needs-pairing" || state === "rejected"
    ? undefined
    : state === "paired" ? "connected" : "reconnecting";

  useEffect(() => {
    if (progress !== "connected") {
      return;
    }

    const timeout = window.setTimeout(() => { setAttempt(null); }, successDisplayMs);
    return () => { window.clearTimeout(timeout); };
  }, [progress]);

  const reconnect = useCallback(() => {
    if (!activePcId) {
      return;
    }

    setAttempt({ pcId: activePcId, hasStarted: false });
    selectPc(activePcId);
  }, [activePcId, selectPc]);

  return {
    progress,
    reconnect
  };
}
