import { useEffect, useRef, useState } from "react";
import { createLocalId } from "../localId";
import type { ClipboardGetResultMessage, ClientMessage } from "../protocol";
import type { ConnectionState } from "./connectionTypes";

const responseTimeoutMs = 5000;
const resultVisibilityMs = 5000;

export function useClipboardRead(state: ConnectionState, send: (payload: ClientMessage) => void) {
  const [clipboardText, setClipboardText] = useState("");
  const [clipboardReadResult, setClipboardReadResult] = useState<ClipboardGetResultMessage | null>(null);
  const [pendingClipboardRead, setPendingClipboardRead] = useState(false);
  const pendingOperationRef = useRef<string | null>(null);

  useEffect(() => {
    if (!pendingClipboardRead || pendingOperationRef.current === null) {
      return;
    }

    const operationId = pendingOperationRef.current;
    const timeout = window.setTimeout(() => {
      if (pendingOperationRef.current !== operationId) {
        return;
      }

      pendingOperationRef.current = null;
      setPendingClipboardRead(false);
      setClipboardReadResult({ type: "clipboard.get.result", operationId, succeeded: false, code: "VAIR-CLIPBOARD-RESPONSE-TIMEOUT", message: "The PC did not confirm the clipboard request." });
    }, responseTimeoutMs);
    return () => { window.clearTimeout(timeout); };
  }, [pendingClipboardRead]);

  useEffect(() => {
    if (state === "paired") {
      return;
    }

    pendingOperationRef.current = null;
    setClipboardText("");
    setClipboardReadResult(null);
    setPendingClipboardRead(false);
  }, [state]);

  useEffect(() => {
    if (!clipboardReadResult?.succeeded) {
      return;
    }

    const timeout = window.setTimeout(() => { setClipboardReadResult(null); }, resultVisibilityMs);
    return () => { window.clearTimeout(timeout); };
  }, [clipboardReadResult]);

  const requestClipboardRead = (): string | null => {
    if (state !== "paired" || pendingOperationRef.current !== null) {
      return null;
    }

    const operationId = createLocalId();
    pendingOperationRef.current = operationId;
    setPendingClipboardRead(true);
    setClipboardReadResult(null);
    send({ type: "clipboard.get", operationId });
    return operationId;
  };

  const completeClipboardRead = (result: ClipboardGetResultMessage) => {
    if (pendingOperationRef.current !== result.operationId) {
      return;
    }

    pendingOperationRef.current = null;
    setPendingClipboardRead(false);
    setClipboardReadResult(result);
    if (result.succeeded && typeof result.text === "string") {
      setClipboardText(result.text);
    }
  };

  return { clipboardReadResult, clipboardText, completeClipboardRead, pendingClipboardRead, requestClipboardRead, setClipboardText };
}
