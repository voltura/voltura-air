import { useEffect, useRef, useState } from "react";
import { createLocalId } from "../identity/localId";
import type { ClientMessage, SystemPowerAction, SystemPowerResultMessage } from "../protocol/messages";
import type { ConnectionState } from "./connectionTypes";

const responseTimeoutMs = 5000;

interface PendingPowerOperation {
  operationId: string;
  action: SystemPowerAction;
}

export function usePowerControl(state: ConnectionState, send: (payload: ClientMessage) => void) {
  const [pendingOperation, setPendingOperation] = useState<PendingPowerOperation | null>(null);
  const [powerActionResult, setPowerActionResult] = useState<SystemPowerResultMessage | null>(null);
  const pendingRef = useRef<PendingPowerOperation | null>(null);

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
      setPowerActionResult({
        type: "system.power.result",
        ...pendingOperation,
        succeeded: false,
        code: "VAIR-POWER-RESPONSE-TIMEOUT",
        message: pendingOperation.action === "lock"
          ? "The PC did not respond to the lock request. Check the host command log and try again."
          : "The PC did not respond to the power request. Check the host application log and try again."
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
    setPowerActionResult(null);
  }, [state]);

  const requestPowerAction = (action: SystemPowerAction) => {
    if (state !== "paired" || pendingRef.current !== null) {
      return;
    }

    const pending = { operationId: createLocalId(), action } satisfies PendingPowerOperation;
    pendingRef.current = pending;
    setPendingOperation(pending);
    setPowerActionResult(null);
    send({ type: "system.power", ...pending });
  };

  const completePowerAction = (result: SystemPowerResultMessage) => {
    if (pendingRef.current?.operationId !== result.operationId) {
      return false;
    }

    pendingRef.current = null;
    setPendingOperation(null);
    setPowerActionResult(result);
    return true;
  };

  const pendingPowerAction = pendingOperation?.action ?? null;
  return { completePowerAction, pendingPowerAction, powerActionResult, requestPowerAction };
}
