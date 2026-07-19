import { useEffect, useRef, useState } from "react";
import { createLocalId } from "../identity/localId";
import type { AwakeResultMessage, ClientMessage } from "../protocol/messages";
import type { ConnectionState } from "./connectionTypes";

const responseTimeoutMs = 5000;

interface PendingAwakeOperation {
  operationId: string;
  enabled: boolean;
}

export function useAwakeControl(state: ConnectionState, send: (payload: ClientMessage) => void) {
  const [pendingOperation, setPendingOperation] = useState<PendingAwakeOperation | null>(null);
  const [awakeResult, setAwakeResult] = useState<AwakeResultMessage | null>(null);
  const pendingRef = useRef<PendingAwakeOperation | null>(null);

  useEffect(() => {
    if (pendingOperation === null) {
      return;
    }

    const timeout = window.setTimeout(() => {
      if (pendingRef.current?.operationId !== pendingOperation.operationId) {
        return;
      }

      pendingRef.current = null;
      setPendingOperation(null);
      setAwakeResult({
        type: "awake.result",
        ...pendingOperation,
        succeeded: false,
        code: "VAIR-AWAKE-RESPONSE-TIMEOUT",
        message: "The PC did not respond to the Keep awake request."
      });
    }, responseTimeoutMs);

    return () => { window.clearTimeout(timeout); };
  }, [pendingOperation]);

  useEffect(() => {
    if (state === "paired") {
      return;
    }

    pendingRef.current = null;
    setPendingOperation(null);
    setAwakeResult(null);
  }, [state]);

  const requestAwakeChange = (enabled: boolean) => {
    if (state !== "paired" || pendingRef.current !== null) {
      return;
    }

    const pending = { operationId: createLocalId(), enabled } satisfies PendingAwakeOperation;
    pendingRef.current = pending;
    setPendingOperation(pending);
    setAwakeResult(null);
    send({ type: "awake.set", ...pending });
  };

  const completeAwakeChange = (result: AwakeResultMessage) => {
    if (pendingRef.current?.operationId !== result.operationId) {
      return false;
    }

    pendingRef.current = null;
    setPendingOperation(null);
    setAwakeResult(result);
    return true;
  };

  const pendingAwakeChange = pendingOperation?.enabled ?? null;
  return { awakeResult, completeAwakeChange, pendingAwakeChange, requestAwakeChange };
}
