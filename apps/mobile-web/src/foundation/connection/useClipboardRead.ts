import { useCallback, useEffect, useRef, useState } from "react";
import { createLocalId } from "../identity/localId";
import type { ClipboardGetResultMessage, ClientMessage } from "../protocol/messages";
import type { ConnectionState } from "./connectionTypes";

const responseTimeoutMs = 5000;
const resultVisibilityMs = 5000;

export function useClipboardRead(state: ConnectionState, send: (payload: ClientMessage) => void) {
  const [clipboardText, setClipboardText] = useState("");
  const [clipboardReadResult, setClipboardReadResult] = useState<ClipboardGetResultMessage | null>(
    null,
  );
  const [pendingClipboardRead, setPendingClipboardRead] = useState(false);
  const pendingOperationRef = useRef<string | null>(null);
  const pendingDeviceClipboardOperationRef = useRef<{
    operationId: string;
    resolve: (result: ClipboardGetResultMessage) => void;
    timeout: number;
  } | null>(null);

  const settlePendingDeviceClipboardRead = useCallback((code: string, message: string) => {
    const pendingDeviceOperation = pendingDeviceClipboardOperationRef.current;
    if (!pendingDeviceOperation) {
      return;
    }

    window.clearTimeout(pendingDeviceOperation.timeout);
    pendingDeviceClipboardOperationRef.current = null;
    pendingDeviceOperation.resolve({
      type: "clipboard.get.result",
      operationId: pendingDeviceOperation.operationId,
      succeeded: false,
      code,
      message,
    });
  }, []);

  const cancelClipboardReadForDevice = useCallback(() => {
    settlePendingDeviceClipboardRead(
      "VAIR-CLIPBOARD-CANCELED",
      "The clipboard request was canceled.",
    );
  }, [settlePendingDeviceClipboardRead]);

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
      setClipboardReadResult({
        type: "clipboard.get.result",
        operationId,
        succeeded: false,
        code: "VAIR-CLIPBOARD-RESPONSE-TIMEOUT",
        message: "The PC did not confirm the clipboard request.",
      });
    }, responseTimeoutMs);
    return () => {
      window.clearTimeout(timeout);
    };
  }, [pendingClipboardRead]);

  useEffect(() => {
    if (state === "paired") {
      return;
    }

    settlePendingDeviceClipboardRead(
      "VAIR-CLIPBOARD-DISCONNECTED",
      "The PC disconnected before returning clipboard text.",
    );
    pendingOperationRef.current = null;
    setClipboardText("");
    setClipboardReadResult(null);
    setPendingClipboardRead(false);
  }, [settlePendingDeviceClipboardRead, state]);

  useEffect(
    () => () => {
      cancelClipboardReadForDevice();
    },
    [cancelClipboardReadForDevice],
  );

  useEffect(() => {
    if (!clipboardReadResult?.succeeded) {
      return;
    }

    const timeout = window.setTimeout(() => {
      setClipboardReadResult(null);
    }, resultVisibilityMs);
    return () => {
      window.clearTimeout(timeout);
    };
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

  const requestClipboardReadForDevice = (): Promise<ClipboardGetResultMessage> | null => {
    if (state !== "paired") {
      return null;
    }

    const previousOperation = pendingDeviceClipboardOperationRef.current;
    if (previousOperation) {
      window.clearTimeout(previousOperation.timeout);
      previousOperation.resolve({
        type: "clipboard.get.result",
        operationId: previousOperation.operationId,
        succeeded: false,
        code: "VAIR-CLIPBOARD-SUPERSEDED",
        message: "A newer device clipboard request replaced this request.",
      });
    }

    const operationId = createLocalId();
    const result = new Promise<ClipboardGetResultMessage>((resolve) => {
      const timeout = window.setTimeout(() => {
        if (pendingDeviceClipboardOperationRef.current?.operationId !== operationId) {
          return;
        }

        pendingDeviceClipboardOperationRef.current = null;
        resolve({
          type: "clipboard.get.result",
          operationId,
          succeeded: false,
          code: "VAIR-CLIPBOARD-RESPONSE-TIMEOUT",
          message: "The PC did not confirm the clipboard request.",
        });
      }, responseTimeoutMs);
      pendingDeviceClipboardOperationRef.current = { operationId, resolve, timeout };
    });
    send({ type: "clipboard.get", operationId });
    return result;
  };

  const completeClipboardRead = (result: ClipboardGetResultMessage) => {
    const pendingDeviceOperation = pendingDeviceClipboardOperationRef.current;
    if (pendingDeviceOperation?.operationId === result.operationId) {
      window.clearTimeout(pendingDeviceOperation.timeout);
      pendingDeviceClipboardOperationRef.current = null;
      pendingDeviceOperation.resolve(result);
      return true;
    }

    if (pendingOperationRef.current !== result.operationId) {
      return false;
    }

    pendingOperationRef.current = null;
    setPendingClipboardRead(false);
    setClipboardReadResult(result);
    if (result.succeeded && typeof result.text === "string") {
      setClipboardText(result.text);
    }
    return true;
  };

  return {
    cancelClipboardReadForDevice,
    clipboardReadResult,
    clipboardText,
    completeClipboardRead,
    pendingClipboardRead,
    requestClipboardRead,
    requestClipboardReadForDevice,
    setClipboardText,
  };
}
