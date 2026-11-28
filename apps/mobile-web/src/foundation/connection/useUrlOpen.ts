import { useEffect, useRef, useState } from "react";
import { createLocalId } from "../identity/localId";
import {
  maxUrlOpenLength,
  type ClientMessage,
  type UrlOpenResultMessage,
} from "../protocol/messages";
import type { ConnectionState } from "./connectionTypes";
import { useCommandConnectionLifetime } from "./useCommandConnectionLifetime";

const responseTimeoutMs = 8000;
const resultVisibilityMs = 8000;

export function useUrlOpen(
  state: ConnectionState,
  connectionEpoch: number,
  send: (payload: ClientMessage) => boolean | void,
) {
  const [pendingOperationId, setPendingOperationId] = useState<string | null>(null);
  const pendingUrlOpen = pendingOperationId !== null;
  const [urlOpenResult, setUrlOpenResult] = useState<UrlOpenResultMessage | null>(null);
  const pendingOperationRef = useRef<string | null>(null);

  useEffect(() => {
    if (pendingOperationId === null || pendingOperationRef.current !== pendingOperationId) {
      return;
    }

    const operationId = pendingOperationId;
    const timeout = window.setTimeout(() => {
      if (pendingOperationRef.current !== operationId) {
        return;
      }

      pendingOperationRef.current = null;
      setPendingOperationId(null);
      setUrlOpenResult({
        type: "url.open.result",
        operationId,
        succeeded: false,
        code: "VAIR-URL-RESPONSE-TIMEOUT",
        message: "The PC did not confirm the open request. Retry when the connection is available.",
      });
    }, responseTimeoutMs);
    return () => {
      window.clearTimeout(timeout);
    };
  }, [pendingOperationId]);

  useCommandConnectionLifetime(state, connectionEpoch, () => {
    pendingOperationRef.current = null;
    setPendingOperationId(null);
    setUrlOpenResult(null);
  });

  useEffect(() => {
    if (urlOpenResult === null) {
      return;
    }

    const timeout = window.setTimeout(() => {
      setUrlOpenResult(null);
    }, resultVisibilityMs);
    return () => {
      window.clearTimeout(timeout);
    };
  }, [urlOpenResult]);

  const requestUrlOpen = (url: string): string | null => {
    if (
      state !== "paired" ||
      pendingOperationRef.current !== null ||
      url.length > maxUrlOpenLength ||
      url.trim().length === 0
    ) {
      return null;
    }

    const operationId = createLocalId();
    if (send({ type: "url.open", operationId, url }) === false) {
      return null;
    }
    pendingOperationRef.current = operationId;
    setPendingOperationId(operationId);
    setUrlOpenResult(null);
    return operationId;
  };

  const completeUrlOpen = (result: UrlOpenResultMessage) => {
    if (pendingOperationRef.current !== result.operationId) {
      return false;
    }

    pendingOperationRef.current = null;
    setPendingOperationId(null);
    setUrlOpenResult(result);
    return true;
  };

  return { completeUrlOpen, pendingUrlOpen, requestUrlOpen, urlOpenResult };
}
