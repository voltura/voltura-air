import { useEffect, useRef, useState } from "react";
import { createLocalId } from "../identity/localId";
import type {
  ClientMessage,
  SystemPowerAction,
  SystemPowerResultMessage,
} from "../protocol/messages";
import type { ConnectionState } from "./connectionTypes";
import { useCommandConnectionLifetime } from "./useCommandConnectionLifetime";

const responseTimeoutMs = 5000;

interface PendingPowerOperation {
  operationId: string;
  action: SystemPowerAction;
}

export function usePowerControl(
  state: ConnectionState,
  connectionEpoch: number,
  send: (payload: ClientMessage) => boolean | void,
) {
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
        message:
          pendingOperation.action === "lock"
            ? "The PC did not respond to the lock request. Check the host command log and try again."
            : "The PC did not respond to the power request. Check the host application log and try again.",
      });
    }, responseTimeoutMs);

    return () => {
      window.clearTimeout(timeout);
    };
  }, [pendingOperation]);

  useCommandConnectionLifetime(state, connectionEpoch, () => {
    pendingRef.current = null;
    setPendingOperation(null);
    setPowerActionResult(null);
  });

  const requestPowerAction = (action: SystemPowerAction) => {
    if (state !== "paired" || pendingRef.current !== null) {
      return false;
    }

    const pending = { operationId: createLocalId(), action } satisfies PendingPowerOperation;
    if (send({ type: "system.power", ...pending }) === false) {
      setPowerActionResult({
        type: "system.power.result",
        ...pending,
        succeeded: false,
        code: "VAIR-POWER-SEND-FAILED",
        message: "Could not send the power request. Reconnect and try again.",
      });
      return false;
    }
    pendingRef.current = pending;
    setPendingOperation(pending);
    setPowerActionResult(null);
    return true;
  };

  const completePowerAction = (result: SystemPowerResultMessage) => {
    const pending = pendingRef.current;
    if (pending?.operationId !== result.operationId || pending.action !== result.action) {
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
