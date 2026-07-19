import { useEffect, useRef, useState } from "react";
import type { ClientMessage, TextSendResultMessage } from "../protocol/messages";
import type { ConnectionState } from "./connectionTypes";
import { createLocalId } from "../identity/localId";

// Managed host destinations may use the full 8-second Windows startup budget;
// leave room for window activation and the acknowledged WebSocket response.
const responseTimeoutMs = 15000;
const resultVisibilityMs = 5000;

export function useTextTransfer(state: ConnectionState, send: (payload: ClientMessage) => void) {
  const [pendingTextTransfer, setPendingTextTransfer] = useState(false);
  const [textTransferResult, setTextTransferResult] = useState<TextSendResultMessage | null>(null);
  const pendingOperationRef = useRef<string | null>(null);

  useEffect(() => {
    if (!pendingTextTransfer || pendingOperationRef.current === null) {
      return;
    }

    const operationId = pendingOperationRef.current;
    const timeout = window.setTimeout(() => {
      if (pendingOperationRef.current !== operationId) {
        return;
      }

      pendingOperationRef.current = null;
      setPendingTextTransfer(false);
      setTextTransferResult({
        type: "text.send.result",
        operationId,
        succeeded: false,
        code: "VAIR-TEXT-RESPONSE-TIMEOUT",
        message: "The PC did not confirm the text transfer. Check the destination before retrying."
      });
    }, responseTimeoutMs);
    return () => { window.clearTimeout(timeout); };
  }, [pendingTextTransfer]);

  useEffect(() => {
    if (state === "paired") {
      return;
    }

    pendingOperationRef.current = null;
    setPendingTextTransfer(false);
    setTextTransferResult(null);
  }, [state]);

  useEffect(() => {
    if (textTransferResult === null) {
      return;
    }

    const timeout = window.setTimeout(() => { setTextTransferResult(null); }, resultVisibilityMs);
    return () => { window.clearTimeout(timeout); };
  }, [textTransferResult]);

  const requestTextTransfer = (text: string, sendEnter = false): string | null => {
    if (state !== "paired" || pendingOperationRef.current !== null || text.length === 0) {
      return null;
    }

    const operationId = createLocalId();
    pendingOperationRef.current = operationId;
    setPendingTextTransfer(true);
    setTextTransferResult(null);
    send({ type: "text.send", operationId, text, sendEnter });
    return operationId;
  };

  const completeTextTransfer = (result: TextSendResultMessage) => {
    if (pendingOperationRef.current !== result.operationId) {
      return false;
    }

    pendingOperationRef.current = null;
    setPendingTextTransfer(false);
    setTextTransferResult(result);
    return true;
  };

  return { completeTextTransfer, pendingTextTransfer, requestTextTransfer, textTransferResult };
}
