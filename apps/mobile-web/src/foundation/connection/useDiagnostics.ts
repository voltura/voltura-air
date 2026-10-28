import { useCallback, useEffect, useRef, useState } from "react";
import { createLocalId } from "../identity/localId";
import type {
  ClientMessage,
  DiagnosticsGetResultMessage,
  MobileHostDiagnosticsSnapshot,
} from "../protocol/messages";
import type { ConnectionState } from "./connectionTypes";

export interface DiagnosticsFailure {
  code: string;
  message: string;
}

export function useDiagnostics(
  state: ConnectionState,
  connectionEpoch: number,
  send: (payload: ClientMessage) => void,
) {
  const [snapshotState, setSnapshotState] = useState<{
    epoch: number;
    value: MobileHostDiagnosticsSnapshot;
  } | null>(null);
  const [failureState, setFailureState] = useState<{
    epoch: number;
    value: DiagnosticsFailure;
  } | null>(null);
  const [pendingState, setPendingState] = useState<{ epoch: number; operationId: string } | null>(
    null,
  );
  const pendingOperationRef = useRef<{ epoch: number; operationId: string } | null>(null);

  useEffect(() => {
    if (state !== "paired" || pendingOperationRef.current?.epoch !== connectionEpoch) {
      pendingOperationRef.current = null;
      setPendingState(null);
    }
  }, [connectionEpoch, state]);

  useEffect(() => {
    if (!pendingState || state !== "paired" || pendingState.epoch !== connectionEpoch) {
      return;
    }
    const timeout = window.setTimeout(() => {
      if (pendingOperationRef.current !== pendingState) {
        return;
      }
      pendingOperationRef.current = null;
      setPendingState(null);
      setFailureState({
        epoch: pendingState.epoch,
        value: {
          code: "diagnostics-timeout",
          message: "The PC did not respond to the diagnostics request. Try again.",
        },
      });
    }, 15_000);
    return () => window.clearTimeout(timeout);
  }, [connectionEpoch, pendingState, state]);

  const requestDiagnostics = useCallback((): string | null => {
    if (state !== "paired" || pendingOperationRef.current?.epoch === connectionEpoch) {
      return null;
    }

    const operationId = createLocalId();
    const pending = { epoch: connectionEpoch, operationId };
    pendingOperationRef.current = pending;
    setPendingState(pending);
    setFailureState(null);
    send({ type: "diagnostics.get", operationId });
    return operationId;
  }, [connectionEpoch, send, state]);

  const completeDiagnostics = useCallback((result: DiagnosticsGetResultMessage): boolean => {
    const pending = pendingOperationRef.current;
    if (pending?.operationId !== result.operationId) {
      return false;
    }

    pendingOperationRef.current = null;
    setPendingState(null);
    if (result.succeeded && result.snapshot) {
      setSnapshotState({ epoch: pending.epoch, value: result.snapshot });
      setFailureState(null);
    } else {
      setFailureState({
        epoch: pending.epoch,
        value: {
          code: result.code ?? "diagnostics-unavailable",
          message: result.message,
        },
      });
    }
    return true;
  }, []);

  return {
    completeDiagnostics,
    failure:
      state === "paired" && failureState?.epoch === connectionEpoch ? failureState.value : null,
    pending: state === "paired" && pendingState?.epoch === connectionEpoch,
    requestDiagnostics,
    snapshot:
      state === "paired" && snapshotState?.epoch === connectionEpoch ? snapshotState.value : null,
  };
}
