import { useCallback, useEffect, useRef, useState } from "react";
import { createLocalId } from "../identity/localId";
import type { ClientMessage, PowerPointLaunchResultMessage, PowerPointRefreshResultMessage, PresentationAction, PresentationCommandOptions, PresentationCommandResultMessage, PresentationSessionAction, PresentationSessionResultMessage, PresentationTarget } from "../protocol/messages";
import type { ConnectionState } from "./connectionTypes";

// A started PowerPoint COM mutation cannot be cancelled safely. The host keeps
// the request pending until it can report authoritative post-command state.
export const presentationCommandResponseTimeoutMs = 30000;
export const presentationSessionResponseTimeoutMs = 30000;
export const powerPointRefreshResponseTimeoutMs = 30000;
const resultVisibilityMs = 5000;

export interface PendingPresentationCommand {
  operationId: string;
  target: PresentationTarget;
  action: PresentationAction;
  enabled?: boolean | undefined;
  runtimePresentationId?: string | undefined;
  slideNumber?: number | undefined;
}

export interface PendingPresentationSession {
  operationId: string;
  action: PresentationSessionAction;
}

export interface PendingPowerPointRefresh {
  operationId: string;
}

export interface PendingPowerPointLaunch {
  operationId: string;
  presentationId: string;
}

export function usePresentationControl(state: ConnectionState, send: (payload: ClientMessage) => void) {
  const [pendingPresentationCommand, setPendingPresentationCommand] = useState<PendingPresentationCommand | null>(null);
  const [pendingPresentationSession, setPendingPresentationSession] = useState<PendingPresentationSession | null>(null);
  const [pendingPowerPointRefresh, setPendingPowerPointRefresh] = useState<PendingPowerPointRefresh | null>(null);
  const [pendingPowerPointLaunch, setPendingPowerPointLaunch] = useState<PendingPowerPointLaunch | null>(null);
  const [presentationResult, setPresentationResult] = useState<PresentationCommandResultMessage | null>(null);
  const [presentationSessionResult, setPresentationSessionResult] = useState<PresentationSessionResultMessage | null>(null);
  const [powerPointRefreshResult, setPowerPointRefreshResult] = useState<PowerPointRefreshResultMessage | null>(null);
  const [powerPointLaunchResult, setPowerPointLaunchResult] = useState<PowerPointLaunchResultMessage | null>(null);
  const pendingRef = useRef<PendingPresentationCommand | null>(null);
  const pendingSessionRef = useRef<PendingPresentationSession | null>(null);
  const pendingRefreshRef = useRef<PendingPowerPointRefresh | null>(null);
  const pendingLaunchRef = useRef<PendingPowerPointLaunch | null>(null);

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
    }, presentationCommandResponseTimeoutMs);

    return () => { window.clearTimeout(timeout); };
  }, [pendingPresentationCommand]);

  useEffect(() => {
    const pending = pendingPowerPointLaunch;
    if (pending === null) {
      return;
    }

    const timeout = window.setTimeout(() => {
      if (pendingLaunchRef.current?.operationId !== pending.operationId) {
        return;
      }

      pendingLaunchRef.current = null;
      setPendingPowerPointLaunch(null);
      setPowerPointLaunchResult({
        type: "presentation.powerpoint.launch.result",
        ...pending,
        succeeded: false,
        code: "VAIR-POWERPOINT-LAUNCH-RESPONSE-TIMEOUT",
        message: "The PC did not confirm the presentation launch. Check PowerPoint before retrying."
      });
    }, presentationCommandResponseTimeoutMs);

    return () => { window.clearTimeout(timeout); };
  }, [pendingPowerPointLaunch]);

  useEffect(() => {
    const pending = pendingPresentationSession;
    if (pending === null) {
      return;
    }

    const timeout = window.setTimeout(() => {
      if (pendingSessionRef.current?.operationId !== pending.operationId) {
        return;
      }

      pendingSessionRef.current = null;
      setPendingPresentationSession(null);
      setPresentationSessionResult({
        type: "presentation.session.result",
        operationId: pending.operationId,
        action: pending.action,
        succeeded: false,
        code: "VAIR-PRESENTATION-SESSION-RESPONSE-TIMEOUT",
        message: "The PC did not confirm the session change. Check the connection and retry."
      });
    }, presentationSessionResponseTimeoutMs);

    return () => { window.clearTimeout(timeout); };
  }, [pendingPresentationSession]);

  useEffect(() => {
    const pending = pendingPowerPointRefresh;
    if (pending === null) {
      return;
    }

    const timeout = window.setTimeout(() => {
      if (pendingRefreshRef.current?.operationId !== pending.operationId) {
        return;
      }

      pendingRefreshRef.current = null;
      setPendingPowerPointRefresh(null);
      setPowerPointRefreshResult({
        type: "presentation.powerpoint.refresh.result",
        operationId: pending.operationId,
        succeeded: false,
        code: "VAIR-POWERPOINT-REFRESH-RESPONSE-TIMEOUT",
        message: "The PC did not confirm the refresh. Check the connection and retry.",
        state: "unavailable",
        presentations: []
      });
    }, powerPointRefreshResponseTimeoutMs);

    return () => { window.clearTimeout(timeout); };
  }, [pendingPowerPointRefresh]);

  useEffect(() => {
    if (state === "paired") {
      return;
    }

    pendingRef.current = null;
    pendingSessionRef.current = null;
    pendingRefreshRef.current = null;
    pendingLaunchRef.current = null;
    setPendingPresentationCommand(null);
    setPendingPresentationSession(null);
    setPendingPowerPointRefresh(null);
    setPendingPowerPointLaunch(null);
    setPresentationResult(null);
    setPresentationSessionResult(null);
    setPowerPointRefreshResult(null);
    setPowerPointLaunchResult(null);
  }, [state]);

  useEffect(() => {
    if (presentationResult?.succeeded !== true) {
      return;
    }

    const timeout = window.setTimeout(() => { setPresentationResult(null); }, resultVisibilityMs);
    return () => { window.clearTimeout(timeout); };
  }, [presentationResult]);

  const requestPresentationCommand = useCallback((
    target: PresentationTarget,
    action: PresentationAction,
    options?: boolean | PresentationCommandOptions
  ): string | null => {
    if (state !== "paired") {
      return null;
    }

    const commandOptions = typeof options === "boolean"
      ? { enabled: options }
      : options;
    const pending = {
      operationId: createLocalId(),
      target,
      action,
      ...commandOptions
    } satisfies PendingPresentationCommand;
    if (pendingRef.current !== null) {
      if (action === "pointer" && commandOptions?.enabled === false) {
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

  const requestPresentationSession = useCallback((
    action: PresentationSessionAction,
    options?: { enabled?: boolean; runtimePresentationId?: string }
  ): string | null => {
    if (state !== "paired") {
      return null;
    }

    if (pendingSessionRef.current !== null) {
      return null;
    }

    const pending = {
      operationId: createLocalId(),
      action
    } satisfies PendingPresentationSession;
    pendingSessionRef.current = pending;
    setPendingPresentationSession(pending);
    setPresentationSessionResult(null);
    send({
      type: "presentation.session",
      operationId: pending.operationId,
      action,
      ...options
    });
    return pending.operationId;
  }, [send, state]);

  const completePresentationSession = (result: PresentationSessionResultMessage) => {
    if (pendingSessionRef.current?.operationId !== result.operationId) {
      return false;
    }

    pendingSessionRef.current = null;
    setPendingPresentationSession(null);
    setPresentationSessionResult(result);
    return true;
  };

  const requestPowerPointRefresh = useCallback((): string | null => {
    if (state !== "paired" || pendingRefreshRef.current !== null) {
      return null;
    }

    const operationId = createLocalId();
    const pending = { operationId } satisfies PendingPowerPointRefresh;
    pendingRefreshRef.current = pending;
    setPendingPowerPointRefresh(pending);
    setPowerPointRefreshResult(null);
    send({ type: "presentation.powerpoint.refresh", operationId });
    return operationId;
  }, [send, state]);

  const completePowerPointRefresh = (result: PowerPointRefreshResultMessage) => {
    if (pendingRefreshRef.current?.operationId !== result.operationId) {
      return false;
    }

    pendingRefreshRef.current = null;
    setPendingPowerPointRefresh(null);
    setPowerPointRefreshResult(result);
    return true;
  };

  const requestPowerPointLaunch = useCallback((presentationId: string): string | null => {
    if (state !== "paired" || pendingLaunchRef.current !== null) {
      return null;
    }

    const pending = { operationId: createLocalId(), presentationId } satisfies PendingPowerPointLaunch;
    pendingLaunchRef.current = pending;
    setPendingPowerPointLaunch(pending);
    setPowerPointLaunchResult(null);
    send({ type: "presentation.powerpoint.launch", ...pending });
    return pending.operationId;
  }, [send, state]);

  const completePowerPointLaunch = (result: PowerPointLaunchResultMessage) => {
    if (pendingLaunchRef.current?.operationId !== result.operationId) {
      return false;
    }

    pendingLaunchRef.current = null;
    setPendingPowerPointLaunch(null);
    setPowerPointLaunchResult(result);
    return true;
  };

  return {
    completePowerPointRefresh,
    completePowerPointLaunch,
    completePresentationCommand,
    completePresentationSession,
    pendingPowerPointRefresh,
    pendingPowerPointLaunch,
    pendingPresentationCommand,
    pendingPresentationSession,
    powerPointRefreshResult,
    powerPointLaunchResult,
    presentationResult,
    presentationSessionResult,
    requestPowerPointRefresh,
    requestPowerPointLaunch,
    requestPresentationCommand,
    requestPresentationSession
  };
}
