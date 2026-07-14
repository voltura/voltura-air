import { useCallback, type RefObject } from "react";
import type { ClientMessage } from "../protocol";
import {
  isMovementInput,
  isUserActivityMessage,
  shouldTrackInputAck,
  trimPendingInputAcks
} from "./connectionProtocol";

const maxBufferedMovementBytes = 1024;
const maxMovementsAfterAckBarrier = 4;

export type PendingMovementAck = {
  sequence: number;
  followingMovementCount: number;
};

type ConnectionSenderOptions = {
  lastMovementAckAtRef: RefObject<number>;
  lastUserActivityAtRef: RefObject<number>;
  nextInputSequenceRef: RefObject<number>;
  pendingInputAcksRef: RefObject<Map<number, number>>;
  pendingMovementAckRef: RefObject<PendingMovementAck | null>;
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
    pendingMovementAckRef,
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
    const isMovement = isMovementInput(payload);
    if (isUserActivityMessage(payload)) {
      lastUserActivityAtRef.current = now;
      if (!isMovement) {
        rescheduleHealthCheckRef.current?.();
      }
    }

    const pendingMovementAck = pendingMovementAckRef.current;
    if (isMovement &&
      (socket.bufferedAmount >= maxBufferedMovementBytes ||
        (pendingMovementAck?.followingMovementCount ?? 0) >= maxMovementsAfterAckBarrier)) {
      return;
    }

    let sequence: number | undefined;
    let payloadToSend: ClientMessage = payload;
    if (supportsInputAckRef.current &&
      shouldTrackInputAck(payload, now, lastMovementAckAtRef.current) &&
      (!isMovement || pendingMovementAck === null)) {
      sequence = nextInputSequenceRef.current;
      nextInputSequenceRef.current = sequence >= Number.MAX_SAFE_INTEGER ? 1 : sequence + 1;
      payloadToSend = { ...payload, seq: sequence };
      pendingInputAcksRef.current.set(sequence, Date.now());
      trimPendingInputAcks(pendingInputAcksRef.current);
      if (isMovement) {
        lastMovementAckAtRef.current = now;
        pendingMovementAckRef.current = { sequence, followingMovementCount: 0 };
      }
    }

    try {
      socket.send(JSON.stringify(payloadToSend));
      if (isMovement && sequence === undefined && pendingMovementAck !== null &&
        pendingMovementAckRef.current?.sequence === pendingMovementAck.sequence) {
        pendingMovementAckRef.current = {
          ...pendingMovementAck,
          followingMovementCount: pendingMovementAck.followingMovementCount + 1
        };
      }
    } catch {
      if (sequence !== undefined) {
        pendingInputAcksRef.current.delete(sequence);
        if (pendingMovementAckRef.current?.sequence === sequence) {
          pendingMovementAckRef.current = null;
        }
      }
      reconnectRef.current?.();
    }
  }, [lastMovementAckAtRef, lastUserActivityAtRef, nextInputSequenceRef, pendingInputAcksRef, pendingMovementAckRef, reconnectRef, rescheduleHealthCheckRef, socketRef, supportsInputAckRef]);

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
