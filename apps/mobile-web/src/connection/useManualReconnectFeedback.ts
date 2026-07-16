import { useCallback, useEffect, useState } from "react";
import type { ConnectionState } from "./connectionTypes";

export type ManualReconnectProgress = "reconnecting" | "connected";

type ManualReconnectAttempt = {
  pcId: string;
  hasStarted: boolean;
  phase: ManualReconnectProgress;
};

const successDisplayMs = 500;

export function useManualReconnectFeedback(
  activePcId: string | null,
  state: ConnectionState,
  selectPc: (pcId: string) => void
) {
  const [attempt, setAttempt] = useState<ManualReconnectAttempt | null>(null);

  useEffect(() => {
    if (!attempt) {
      return;
    }

    if (!activePcId || activePcId !== attempt.pcId || state === "needs-pairing" || state === "rejected") {
      setAttempt(null);
      return;
    }

    if (state === "connecting" && !attempt.hasStarted) {
      setAttempt({ ...attempt, hasStarted: true });
      return;
    }

    if (state === "paired" && attempt.phase !== "connected") {
      setAttempt({ ...attempt, phase: "connected" });
      return;
    }

    if (state === "unavailable" && attempt.hasStarted) {
      setAttempt(null);
    }
  }, [activePcId, attempt, state]);

  useEffect(() => {
    if (attempt?.phase !== "connected") {
      return;
    }

    const timeout = window.setTimeout(() => setAttempt(null), successDisplayMs);
    return () => window.clearTimeout(timeout);
  }, [attempt]);

  const reconnect = useCallback(() => {
    if (!activePcId) {
      return;
    }

    setAttempt({ pcId: activePcId, hasStarted: false, phase: "reconnecting" });
    selectPc(activePcId);
  }, [activePcId, selectPc]);

  return {
    progress: attempt?.pcId === activePcId ? attempt.phase : undefined,
    reconnect
  };
}
