import { useCallback, type RefObject } from "react";
import type { ClientMessage } from "../protocol";
import {
  isMovementInput,
  isUserActivityMessage,
  shouldTrackInputAck,
  trimPendingInputAcks
} from "./connectionProtocol";

type ConnectionSenderOptions = {
  lastMovementAckAtRef: RefObject<number>;
  lastUserActivityAtRef: RefObject<number>;
  nextInputSequenceRef: RefObject<number>;
  pendingInputAcksRef: RefObject<Map<number, number>>;
  reconnectRef: RefObject<(() => void) | null>;
  rescheduleHealthCheckRef: RefObject<(() => void) | null>;
  socketRef: RefObject<WebSocket | null>;
  supportsInputAckRef: RefObject<boolean>;
  supportsVolumeControlRef: RefObject<boolean>;
};

export function useConnectionSender(options: ConnectionSenderOptions) {
  const {
    lastMovementAckAtRef,
    lastUserActivityAtRef,
    nextInputSequenceRef,
    pendingInputAcksRef,
    reconnectRef,
    rescheduleHealthCheckRef,
    socketRef,
    supportsInputAckRef,
    supportsVolumeControlRef
  } = options;

  const send = useCallback((payload: ClientMessage) => {
    const socket = socketRef.current;
    if (socket?.readyState !== WebSocket.OPEN) {
      reconnectRef.current?.();
      return;
    }

    const now = Date.now();
    if (isUserActivityMessage(payload)) {
      lastUserActivityAtRef.current = now;
      rescheduleHealthCheckRef.current?.();
    }

    let sequence: number | undefined;
    let payloadToSend: ClientMessage = payload;
    if (supportsInputAckRef.current && shouldTrackInputAck(payload, now, lastMovementAckAtRef.current)) {
      sequence = nextInputSequenceRef.current;
      nextInputSequenceRef.current = sequence >= Number.MAX_SAFE_INTEGER ? 1 : sequence + 1;
      payloadToSend = { ...payload, seq: sequence };
      pendingInputAcksRef.current.set(sequence, Date.now());
      trimPendingInputAcks(pendingInputAcksRef.current);
      if (isMovementInput(payload)) {
        lastMovementAckAtRef.current = now;
      }
    }

    try {
      socket.send(JSON.stringify(payloadToSend));
    } catch {
      if (sequence !== undefined) {
        pendingInputAcksRef.current.delete(sequence);
      }
      reconnectRef.current?.();
    }
  }, [lastMovementAckAtRef, lastUserActivityAtRef, nextInputSequenceRef, pendingInputAcksRef, reconnectRef, rescheduleHealthCheckRef, socketRef, supportsInputAckRef]);

  const requestAudioState = useCallback(() => {
    const socket = socketRef.current;
    if (socket?.readyState !== WebSocket.OPEN || !supportsVolumeControlRef.current) {
      return;
    }

    try {
      socket.send(JSON.stringify({ type: "audio.get" } satisfies ClientMessage));
    } catch {
      reconnectRef.current?.();
    }
  }, [reconnectRef, socketRef, supportsVolumeControlRef]);

  return { requestAudioState, send };
}
