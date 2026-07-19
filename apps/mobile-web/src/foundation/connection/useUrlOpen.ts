import { useEffect, useRef, useState } from "react";
import { createLocalId } from "../identity/localId";
import type { ClientMessage, UrlOpenResultMessage } from "../protocol/messages";
import type { ConnectionState } from "./connectionTypes";

const responseTimeoutMs = 8000;
const resultVisibilityMs = 8000;

export function useUrlOpen(state: ConnectionState, send: (payload: ClientMessage) => void) {
  const [pendingUrlOpen, setPendingUrlOpen] = useState(false);
  const [urlOpenResult, setUrlOpenResult] = useState<UrlOpenResultMessage | null>(null);
  const pendingOperationRef = useRef<string | null>(null);

  useEffect(() => {
    if (!pendingUrlOpen || pendingOperationRef.current === null) {
      return;
    }

    const operationId = pendingOperationRef.current;
    const timeout = window.setTimeout(() => {
      if (pendingOperationRef.current !== operationId) {
        return;
      }

      pendingOperationRef.current = null;
      setPendingUrlOpen(false);
      setUrlOpenResult({
        type: "url.open.result",
        operationId,
        succeeded: false,
        code: "VAIR-URL-RESPONSE-TIMEOUT",
        message: "The PC did not confirm the open request. Retry when the connection is available."
      });
    }, responseTimeoutMs);
    return () => { window.clearTimeout(timeout); };
  }, [pendingUrlOpen]);

  useEffect(() => {
    if (state === "paired") {
      return;
    }

    pendingOperationRef.current = null;
    setPendingUrlOpen(false);
    setUrlOpenResult(null);
  }, [state]);

  useEffect(() => {
    if (urlOpenResult === null) {
      return;
    }

    const timeout = window.setTimeout(() => { setUrlOpenResult(null); }, resultVisibilityMs);
    return () => { window.clearTimeout(timeout); };
  }, [urlOpenResult]);

  const requestUrlOpen = (url: string): string | null => {
    if (state !== "paired" || pendingOperationRef.current !== null || url.trim().length === 0) {
      return null;
    }

    const operationId = createLocalId();
    pendingOperationRef.current = operationId;
    setPendingUrlOpen(true);
    setUrlOpenResult(null);
    send({ type: "url.open", operationId, url });
    return operationId;
  };

  const completeUrlOpen = (result: UrlOpenResultMessage) => {
    if (pendingOperationRef.current !== result.operationId) {
      return false;
    }

    pendingOperationRef.current = null;
    setPendingUrlOpen(false);
    setUrlOpenResult(result);
    return true;
  };

  return { completeUrlOpen, pendingUrlOpen, requestUrlOpen, urlOpenResult };
}
