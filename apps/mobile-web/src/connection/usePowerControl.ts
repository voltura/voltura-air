import { useEffect, useRef, useState } from "react";
import type { ClientMessage, SystemPowerAction, SystemPowerResultMessage } from "../protocol";
import type { ConnectionState } from "./connectionTypes";

const responseTimeoutMs = 5000;

export function usePowerControl(state: ConnectionState, send: (payload: ClientMessage) => void) {
  const [pendingPowerAction, setPendingPowerAction] = useState<SystemPowerAction | null>(null);
  const [powerActionResult, setPowerActionResult] = useState<SystemPowerResultMessage | null>(null);
  const pendingRef = useRef<SystemPowerAction | null>(null);

  useEffect(() => {
    if (pendingPowerAction === null) {
      return;
    }

    const action = pendingPowerAction;
    const timeout = window.setTimeout(() => {
      if (pendingRef.current !== action) {
        return;
      }

      pendingRef.current = null;
      setPendingPowerAction(null);
      setPowerActionResult({
        type: "system.power.result",
        action,
        succeeded: false,
        code: "VAIR-POWER-RESPONSE-TIMEOUT",
        message: action === "lock"
          ? "The PC did not respond to the lock request. Check the host command log and try again."
          : "The PC did not respond to the power request. Check the host application log and try again."
      });
    }, responseTimeoutMs);

    return () => { window.clearTimeout(timeout); };
  }, [pendingPowerAction]);

  useEffect(() => {
    if (state === "paired") {
      return;
    }

    pendingRef.current = null;
    setPendingPowerAction(null);
    setPowerActionResult(null);
  }, [state]);

  const requestPowerAction = (action: SystemPowerAction) => {
    if (state !== "paired" || pendingRef.current !== null) {
      return;
    }

    pendingRef.current = action;
    setPendingPowerAction(action);
    setPowerActionResult(null);
    send({ type: "system.power", action });
  };

  const completePowerAction = (result: SystemPowerResultMessage) => {
    pendingRef.current = null;
    setPendingPowerAction(null);
    setPowerActionResult(result);
  };

  return { completePowerAction, pendingPowerAction, powerActionResult, requestPowerAction };
}
