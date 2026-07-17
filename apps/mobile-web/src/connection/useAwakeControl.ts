import { useEffect, useRef, useState } from "react";
import type { AwakeResultMessage, ClientMessage } from "../protocol";
import type { ConnectionState } from "./connectionTypes";

const responseTimeoutMs = 5000;

export function useAwakeControl(state: ConnectionState, send: (payload: ClientMessage) => void) {
  const [pendingAwakeChange, setPendingAwakeChange] = useState<boolean | null>(null);
  const [awakeResult, setAwakeResult] = useState<AwakeResultMessage | null>(null);
  const pendingRef = useRef<boolean | null>(null);

  useEffect(() => {
    if (pendingAwakeChange === null) {
      return;
    }

    const requestedState = pendingAwakeChange;
    const timeout = window.setTimeout(() => {
      if (pendingRef.current !== requestedState) {
        return;
      }

      pendingRef.current = null;
      setPendingAwakeChange(null);
      setAwakeResult({
        type: "awake.result",
        enabled: requestedState,
        succeeded: false,
        code: "VAIR-AWAKE-RESPONSE-TIMEOUT",
        message: "The PC did not respond to the Keep awake request."
      });
    }, responseTimeoutMs);

    return () => { window.clearTimeout(timeout); };
  }, [pendingAwakeChange]);

  useEffect(() => {
    if (state === "paired") {
      return;
    }

    pendingRef.current = null;
    setPendingAwakeChange(null);
    setAwakeResult(null);
  }, [state]);

  const requestAwakeChange = (enabled: boolean) => {
    if (state !== "paired" || pendingRef.current !== null) {
      return;
    }

    pendingRef.current = enabled;
    setPendingAwakeChange(enabled);
    setAwakeResult(null);
    send({ type: "awake.set", enabled });
  };

  const completeAwakeChange = (result: AwakeResultMessage) => {
    pendingRef.current = null;
    setPendingAwakeChange(null);
    setAwakeResult(result);
  };

  return { awakeResult, completeAwakeChange, pendingAwakeChange, requestAwakeChange };
}
