import { useEffect, useRef, useState } from "react";
import { createLocalId } from "../identity/localId";
import type {
  ClientMessage,
  PresentationReportSavePayload,
  PresentationReportSaveResultMessage
} from "../protocol/messages";
import type { ConnectionState } from "./connectionTypes";

const responseTimeoutMs = 8000;

interface PendingPresentationReportSave {
  operationId: string;
  reportId: string;
}

export function usePresentationReportSave(
  state: ConnectionState,
  send: (payload: ClientMessage) => void
) {
  const [pendingPresentationReportSave, setPendingPresentationReportSave] =
    useState<PendingPresentationReportSave | null>(null);
  const [presentationReportSaveResult, setPresentationReportSaveResult] =
    useState<PresentationReportSaveResultMessage | null>(null);
  const pendingRef = useRef<PendingPresentationReportSave | null>(null);
  const retryIdentityRef = useRef<PendingPresentationReportSave | null>(null);

  useEffect(() => {
    const pending = pendingPresentationReportSave;
    if (pending === null) {
      return;
    }

    const timeout = window.setTimeout(() => {
      if (pendingRef.current?.operationId !== pending.operationId) {
        return;
      }

      pendingRef.current = null;
      setPendingPresentationReportSave(null);
      setPresentationReportSaveResult({
        type: "presentation.report.save.result",
        ...pending,
        succeeded: false,
        code: "VAIR-PRESENTATION-SAVE-TIMEOUT",
        message: "The PC did not confirm the save. Reconnect if needed, then try again."
      });
    }, responseTimeoutMs);
    return () => { window.clearTimeout(timeout); };
  }, [pendingPresentationReportSave]);

  useEffect(() => {
    if (state === "paired" || pendingRef.current === null) {
      return;
    }

    const pending = pendingRef.current;
    pendingRef.current = null;
    setPendingPresentationReportSave(null);
    setPresentationReportSaveResult({
      type: "presentation.report.save.result",
      ...pending,
      succeeded: false,
      code: "VAIR-PRESENTATION-SAVE-DISCONNECTED",
      message: "The connection was lost before the PC confirmed the save. Reconnect and try again."
    });
  }, [state]);

  const requestPresentationReportSave = (payload: PresentationReportSavePayload) => {
    if (state !== "paired" || pendingRef.current !== null) {
      return null;
    }

    const existingIdentity = retryIdentityRef.current?.reportId === payload.reportId
      ? retryIdentityRef.current
      : null;
    const pending = existingIdentity ?? {
      operationId: createLocalId(),
      reportId: payload.reportId
    };
    retryIdentityRef.current = pending;
    pendingRef.current = pending;
    setPendingPresentationReportSave(pending);
    setPresentationReportSaveResult(null);
    send({ type: "presentation.report.save", operationId: pending.operationId, ...payload });
    return pending.operationId;
  };

  const completePresentationReportSave = (result: PresentationReportSaveResultMessage) => {
    if (pendingRef.current?.operationId !== result.operationId ||
        pendingRef.current.reportId !== result.reportId) {
      return false;
    }

    pendingRef.current = null;
    setPendingPresentationReportSave(null);
    setPresentationReportSaveResult(result);
    return true;
  };

  return {
    completePresentationReportSave,
    pendingPresentationReportSave,
    presentationReportSaveResult,
    requestPresentationReportSave
  };
}
