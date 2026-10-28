import { useCallback, useEffect, useRef, useState } from "react";
import { createLocalId } from "../identity/localId";
import type {
  ClientMessage,
  CustomScreenDefinition,
  CustomScreenGetResultMessage,
  CustomScreenInvokeResultMessage,
} from "../protocol/messages";
import type { ConnectionState } from "./connectionTypes";

export const incompatibleCustomScreenResponseCode = "VAIR-CUSTOM-SCREEN-INCOMPATIBLE";
export const incompatibleCustomScreenResponseMessage =
  "This custom screen uses features this version of the app cannot display. Refresh the app and try again.";

export function useCustomScreens(
  state: ConnectionState,
  connectionEpoch: number,
  catalogRevision: string | undefined,
  send: (payload: ClientMessage) => void,
) {
  const [definitionState, setDefinitionState] = useState<{
    catalogRevision: string | undefined;
    connectionEpoch: number;
    value: CustomScreenDefinition | null;
  } | null>(null);
  const [getResultState, setGetResultState] = useState<{
    connectionEpoch: number;
    value: CustomScreenGetResultMessage;
  } | null>(null);
  const [invokeResultState, setInvokeResultState] = useState<{
    connectionEpoch: number;
    value: CustomScreenInvokeResultMessage;
  } | null>(null);
  const [pendingButtonsState, setPendingButtonsState] = useState<{
    connectionEpoch: number;
    values: ReadonlySet<string>;
  }>(() => ({ connectionEpoch, values: new Set() }));
  const pendingGetRef = useRef<{
    connectionEpoch: number;
    operationId: string;
    screenId: string;
  } | null>(null);
  const pendingInvokesRef = useRef(
    new Map<
      string,
      {
        buttonId: string;
        connectionEpoch: number;
        suppressResult: boolean;
        timeout: number;
      }
    >(),
  );

  const getTimeoutRef = useRef<number | undefined>(undefined);

  useEffect(() => {
    const invokes = pendingInvokesRef.current;
    return () => {
      window.clearTimeout(getTimeoutRef.current);
      pendingGetRef.current = null;
      for (const pending of invokes.values()) {
        window.clearTimeout(pending.timeout);
      }
      invokes.clear();
    };
  }, [connectionEpoch, state]);

  const requestCustomScreen = useCallback(
    (screenId: string) => {
      if (state !== "paired") {
        return;
      }

      const operationId = createLocalId();
      pendingGetRef.current = { connectionEpoch, operationId, screenId };
      setGetResultState(null);
      window.clearTimeout(getTimeoutRef.current);
      getTimeoutRef.current = window.setTimeout(() => {
        if (pendingGetRef.current?.operationId !== operationId) {
          return;
        }
        pendingGetRef.current = null;
        setDefinitionState(null);
        setGetResultState({
          connectionEpoch,
          value: {
            type: "custom.screen.get.result",
            operationId,
            succeeded: false,
            code: "VAIR-CUSTOM-SCREEN-RESPONSE-TIMEOUT",
            message: "The PC did not return the custom screen. Reopen it to try again.",
          },
        });
      }, 15_000);
      send({ type: "custom.screen.get", operationId, screenId });
    },
    [connectionEpoch, send, state],
  );

  const completeCustomScreenGet = useCallback(
    (result: CustomScreenGetResultMessage) => {
      if (
        pendingGetRef.current?.operationId !== result.operationId ||
        pendingGetRef.current.connectionEpoch !== connectionEpoch
      ) {
        return false;
      }

      pendingGetRef.current = null;
      window.clearTimeout(getTimeoutRef.current);
      setGetResultState({ connectionEpoch, value: result });
      setDefinitionState({
        catalogRevision,
        connectionEpoch,
        value: result.succeeded && result.screen ? result.screen : null,
      });
      return true;
    },
    [catalogRevision, connectionEpoch],
  );

  const rejectCustomScreenGet = useCallback(
    (operationId: string) => {
      if (
        pendingGetRef.current?.operationId !== operationId ||
        pendingGetRef.current.connectionEpoch !== connectionEpoch
      ) {
        return false;
      }

      const result: CustomScreenGetResultMessage = {
        type: "custom.screen.get.result",
        operationId,
        succeeded: false,
        code: incompatibleCustomScreenResponseCode,
        message: incompatibleCustomScreenResponseMessage,
      };
      pendingGetRef.current = null;
      window.clearTimeout(getTimeoutRef.current);
      setGetResultState({ connectionEpoch, value: result });
      setDefinitionState({ catalogRevision, connectionEpoch, value: null });
      return true;
    },
    [catalogRevision, connectionEpoch],
  );

  const completeCustomScreenInvoke = useCallback(
    (result: CustomScreenInvokeResultMessage) => {
      const pending = pendingInvokesRef.current.get(result.operationId);
      if (pending?.connectionEpoch !== connectionEpoch) {
        return false;
      }

      window.clearTimeout(pending.timeout);
      pendingInvokesRef.current.delete(result.operationId);
      setPendingButtonsState((current) => {
        const next = new Set(current.connectionEpoch === connectionEpoch ? current.values : []);
        if (
          ![...pendingInvokesRef.current.values()].some(
            (value) =>
              value.connectionEpoch === connectionEpoch && value.buttonId === pending.buttonId,
          )
        ) {
          next.delete(pending.buttonId);
        }
        return { connectionEpoch, values: next };
      });
      if (!pending.suppressResult) {
        setInvokeResultState({ connectionEpoch, value: result });
      }
      if (result.code === "stale-screen") {
        setDefinitionState(null);
      }
      return !pending.suppressResult;
    },
    [connectionEpoch],
  );

  const invokeCustomScreenButton = useCallback(
    (
      screenId: string,
      screenRevision: string,
      buttonId: string,
      enabled?: boolean,
      suppressResult = false,
    ) => {
      if (state !== "paired") {
        return;
      }

      const operationId = createLocalId();
      const timeout = window.setTimeout(() => {
        completeCustomScreenInvoke({
          type: "custom.screen.invoke.result",
          operationId,
          screenId,
          buttonId,
          succeeded: false,
          code: "VAIR-CUSTOM-SCREEN-INVOKE-TIMEOUT",
          message: "The PC did not confirm the action. Check the PC before retrying.",
        });
      }, 30_000);
      pendingInvokesRef.current.set(operationId, {
        buttonId,
        connectionEpoch,
        suppressResult,
        timeout,
      });
      setPendingButtonsState((current) => ({
        connectionEpoch,
        values: new Set(current.connectionEpoch === connectionEpoch ? current.values : []).add(
          buttonId,
        ),
      }));
      send({
        type: "custom.screen.invoke",
        operationId,
        screenId,
        screenRevision,
        buttonId,
        ...(enabled === undefined ? {} : { enabled }),
      });
    },
    [completeCustomScreenInvoke, connectionEpoch, send, state],
  );

  const isCurrentConnection = state === "paired";
  const definition =
    isCurrentConnection &&
    definitionState?.connectionEpoch === connectionEpoch &&
    definitionState.catalogRevision === catalogRevision
      ? definitionState.value
      : null;
  const getResult =
    isCurrentConnection && getResultState?.connectionEpoch === connectionEpoch
      ? getResultState.value
      : null;
  const invokeResult =
    isCurrentConnection && invokeResultState?.connectionEpoch === connectionEpoch
      ? invokeResultState.value
      : null;

  return {
    completeCustomScreenGet,
    completeCustomScreenInvoke,
    customScreenDefinition: definition,
    customScreenGetResult: getResult,
    customScreenInvokeResult: invokeResult,
    invokeCustomScreenButton,
    pendingCustomScreenButtonIds:
      isCurrentConnection && pendingButtonsState.connectionEpoch === connectionEpoch
        ? pendingButtonsState.values
        : new Set<string>(),
    rejectCustomScreenGet,
    requestCustomScreen,
  };
}
