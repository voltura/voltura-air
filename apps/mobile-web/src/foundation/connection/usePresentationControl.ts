import { useCallback, useEffect, useRef, useState } from "react";
import { createLocalId } from "../identity/localId";
import type { ClientMessage, PresentationAction, PresentationCommandResultMessage, PresentationTarget } from "../protocol/messages";
import type { ConnectionState } from "./connectionTypes";

const responseTimeoutMs = 5000;
const resultVisibilityMs = 5000;

export interface PendingPresentationCommand {
  operationId: string;
  target: PresentationTarget;
  action: PresentationAction;
  enabled?: boolean | undefined;
}

export function usePresentationControl(state: ConnectionState, send: (payload: ClientMessage) => void) {
  const [pendingPresentationCommand, setPendingPresentationCommand] = useState<PendingPresentationCommand | null>(null);
  const [presentationResult, setPresentationResult] = useState<PresentationCommandResultMessage | null>(null);
  const pendingRef = useRef<PendingPresentationCommand | null>(null);

  useEffect(() => {
    const pending = pendingPresentationCommand;
    if (pending === null) {
      return;
    }

    const timeout = window.setTimeout(() => {
      if (pendingRef.current?.operationId !== pending.operationId) {
        return;
      }

      pendingRef.current = null;
      setPendingPresentationCommand(null);
      setPresentationResult({
        type: "presentation.command.result",
        ...pending,
        succeeded: false,
        code: "VAIR-PRESENTATION-RESPONSE-TIMEOUT",
        message: "The PC did not confirm the presentation command. Check the connection before retrying.",
        laserPointerActive: false
      });
    }, responseTimeoutMs);

    return () => { window.clearTimeout(timeout); };
  }, [pendingPresentationCommand]);

  useEffect(() => {
    if (state === "paired") {
      return;
    }

    pendingRef.current = null;
    setPendingPresentationCommand(null);
    setPresentationResult(null);
  }, [state]);

  useEffect(() => {
    if (presentationResult === null) {
      return;
    }

    const timeout = window.setTimeout(() => { setPresentationResult(null); }, resultVisibilityMs);
    return () => { window.clearTimeout(timeout); };
  }, [presentationResult]);

  const requestPresentationCommand = useCallback((
    target: PresentationTarget,
    action: PresentationAction,
    enabled?: boolean
  ): string | null => {
    if (state !== "paired") {
      return null;
    }

    const pending = {
      operationId: createLocalId(),
      target,
      action,
      ...(enabled === undefined ? {} : { enabled })
    } satisfies PendingPresentationCommand;
    if (pendingRef.current !== null) {
      if (action === "pointer" && enabled === false) {
        send({ type: "presentation.command", ...pending });
        return pending.operationId;
      }

      return null;
    }

    pendingRef.current = pending;
    setPendingPresentationCommand(pending);
    setPresentationResult(null);
    send({ type: "presentation.command", ...pending });
    return pending.operationId;
  }, [send, state]);

  const completePresentationCommand = (result: PresentationCommandResultMessage) => {
    if (pendingRef.current?.operationId !== result.operationId) {
      return false;
    }

    pendingRef.current = null;
    setPendingPresentationCommand(null);
    setPresentationResult(result);
    return true;
  };

  return { completePresentationCommand, pendingPresentationCommand, presentationResult, requestPresentationCommand };
}
