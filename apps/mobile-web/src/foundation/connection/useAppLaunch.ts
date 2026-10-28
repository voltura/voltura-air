import { useEffect, useRef, useState } from "react";
import { createLocalId } from "../identity/localId";
import type { AppLaunchResultMessage, ClientMessage } from "../protocol/messages";
import type { ConnectionState } from "./connectionTypes";

const responseTimeoutMs = 5000;
const resultVisibilityMs = 4000;

export function useAppLaunch(state: ConnectionState, send: (payload: ClientMessage) => void) {
  const [pendingOperation, setPendingOperation] = useState<{
    actionId: string;
    operationId: string;
  } | null>(null);
  const pendingAppLaunchId = pendingOperation?.actionId ?? null;
  const [appLaunchResult, setAppLaunchResult] = useState<AppLaunchResultMessage | null>(null);
  const pendingRef = useRef<{ actionId: string; operationId: string } | null>(null);

  useEffect(() => {
    const pending = pendingOperation;
    if (pending === null) {
      return;
    }

    const timeout = window.setTimeout(() => {
      if (pendingRef.current?.operationId !== pending.operationId) {
        return;
      }

      pendingRef.current = null;
      setPendingOperation(null);
      setAppLaunchResult({
        type: "app.launch.result",
        actionId: pending.actionId,
        operationId: pending.operationId,
        succeeded: false,
        code: "VAIR-APP-LAUNCH-RESPONSE-TIMEOUT",
        message: "The PC did not respond to the application launch request.",
      });
    }, responseTimeoutMs);

    return () => {
      window.clearTimeout(timeout);
    };
  }, [pendingOperation]);

  useEffect(() => {
    if (state === "paired") {
      return;
    }

    pendingRef.current = null;
    setPendingOperation(null);
    setAppLaunchResult(null);
  }, [state]);

  useEffect(() => {
    if (appLaunchResult === null) {
      return;
    }

    const timeout = window.setTimeout(() => {
      setAppLaunchResult(null);
    }, resultVisibilityMs);
    return () => {
      window.clearTimeout(timeout);
    };
  }, [appLaunchResult]);

  const requestAppLaunch = (actionId: string) => {
    if (state !== "paired" || pendingRef.current !== null) {
      return;
    }

    const operationId = createLocalId();
    pendingRef.current = { actionId, operationId };
    setPendingOperation({ actionId, operationId });
    setAppLaunchResult(null);
    send({ type: "app.launch", actionId, operationId });
  };

  const completeAppLaunch = (result: AppLaunchResultMessage) => {
    if (pendingRef.current?.operationId !== result.operationId) {
      return false;
    }

    pendingRef.current = null;
    setPendingOperation(null);
    setAppLaunchResult(result);
    return true;
  };

  return { appLaunchResult, completeAppLaunch, pendingAppLaunchId, requestAppLaunch };
}
