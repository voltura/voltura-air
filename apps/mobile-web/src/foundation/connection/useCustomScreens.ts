import { useCallback, useRef, useState } from "react";
import { createLocalId } from "../identity/localId";
import type {
  ClientMessage,
  CustomScreenDefinition,
  CustomScreenGetResultMessage,
  CustomScreenInvokeResultMessage
} from "../protocol/messages";
import type { ConnectionState } from "./connectionTypes";

export function useCustomScreens(
  state: ConnectionState,
  connectionEpoch: number,
  catalogRevision: string | undefined,
  send: (payload: ClientMessage) => void
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
  const pendingInvokesRef = useRef(new Map<string, {
    buttonId: string;
    connectionEpoch: number;
  }>());

  const requestCustomScreen = useCallback((screenId: string) => {
    if (state !== "paired") {
      return;
    }

    const operationId = createLocalId();
    pendingGetRef.current = { connectionEpoch, operationId, screenId };
    setGetResultState(null);
    send({ type: "custom.screen.get", operationId, screenId });
  }, [connectionEpoch, send, state]);

  const invokeCustomScreenButton = useCallback((
    screenId: string,
    screenRevision: string,
    buttonId: string
  ) => {
    if (state !== "paired") {
      return;
    }

    const operationId = createLocalId();
    pendingInvokesRef.current.set(operationId, { buttonId, connectionEpoch });
    setPendingButtonsState((current) => ({
      connectionEpoch,
      values: new Set(current.connectionEpoch === connectionEpoch ? current.values : []).add(buttonId)
    }));
    send({
      type: "custom.screen.invoke",
      operationId,
      screenId,
      screenRevision,
      buttonId
    });
  }, [connectionEpoch, send, state]);

  const completeCustomScreenGet = useCallback((result: CustomScreenGetResultMessage) => {
    if (pendingGetRef.current?.operationId !== result.operationId ||
        pendingGetRef.current.connectionEpoch !== connectionEpoch) {
      return false;
    }

    pendingGetRef.current = null;
    setGetResultState({ connectionEpoch, value: result });
    setDefinitionState({
      catalogRevision,
      connectionEpoch,
      value: result.succeeded && result.screen ? result.screen : null
    });
    return true;
  }, [catalogRevision, connectionEpoch]);

  const completeCustomScreenInvoke = useCallback((result: CustomScreenInvokeResultMessage) => {
    const pending = pendingInvokesRef.current.get(result.operationId);
    if (pending?.connectionEpoch !== connectionEpoch) {
      return false;
    }

    pendingInvokesRef.current.delete(result.operationId);
    setPendingButtonsState((current) => {
      const next = new Set(current.connectionEpoch === connectionEpoch ? current.values : []);
      if (![...pendingInvokesRef.current.values()].some(
        value => value.connectionEpoch === connectionEpoch && value.buttonId === pending.buttonId)) {
        next.delete(pending.buttonId);
      }
      return { connectionEpoch, values: next };
    });
    setInvokeResultState({ connectionEpoch, value: result });
    if (result.code === "stale-screen") {
      setDefinitionState(null);
    }
    return true;
  }, [connectionEpoch]);

  const isCurrentConnection = state === "paired";
  const definition = isCurrentConnection
    && definitionState?.connectionEpoch === connectionEpoch
    && definitionState.catalogRevision === catalogRevision
    ? definitionState.value
    : null;
  const getResult = isCurrentConnection && getResultState?.connectionEpoch === connectionEpoch
    ? getResultState.value
    : null;
  const invokeResult = isCurrentConnection && invokeResultState?.connectionEpoch === connectionEpoch
    ? invokeResultState.value
    : null;

  return {
    completeCustomScreenGet,
    completeCustomScreenInvoke,
    customScreenDefinition: definition,
    customScreenGetResult: getResult,
    customScreenInvokeResult: invokeResult,
    invokeCustomScreenButton,
    pendingCustomScreenButtonIds: isCurrentConnection &&
      pendingButtonsState.connectionEpoch === connectionEpoch
      ? pendingButtonsState.values
      : new Set<string>(),
    requestCustomScreen
  };
}
