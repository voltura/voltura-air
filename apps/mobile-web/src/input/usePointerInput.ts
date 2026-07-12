import { useEffect, useRef, type TouchEvent } from "react";
import type { ConnectionState } from "../connection/connectionTypes";
import { GestureRecognizer, touchesFromList, type TrackpadSettings } from "../gestures";
import type { ClientMessage, KeyboardSpecialMessage } from "../protocol";
import { triggerHapticFeedback } from "../hapticFeedback";

type PointerInputOptions = {
  send: (payload: ClientMessage) => void;
  state: ConnectionState;
  trackpadSettings: TrackpadSettings;
};

export function usePointerInput({ send, state, trackpadSettings }: PointerInputOptions) {
  const recognizerRef = useRef(new GestureRecognizer());
  const pointerFrameRef = useRef<number | null>(null);
  const pendingPointerMoveRef = useRef<{ dx: number; dy: number } | null>(null);
  const pendingPointerWheelRef = useRef<{ dx: number; dy: number } | null>(null);

  useEffect(() => () => {
    if (pointerFrameRef.current !== null) {
      window.cancelAnimationFrame(pointerFrameRef.current);
      pointerFrameRef.current = null;
    }
  }, []);

  const sendPendingPointerDeltas = () => {
    pointerFrameRef.current = null;
    const move = pendingPointerMoveRef.current;
    const wheel = pendingPointerWheelRef.current;
    pendingPointerMoveRef.current = null;
    pendingPointerWheelRef.current = null;

    if (state !== "paired") {
      return;
    }

    if (move && (move.dx !== 0 || move.dy !== 0)) {
      send({ type: "pointer.move", dx: roundDelta(move.dx), dy: roundDelta(move.dy) });
    }

    if (wheel && (wheel.dx !== 0 || wheel.dy !== 0)) {
      send({ type: "pointer.wheel", dx: roundDelta(wheel.dx), dy: roundDelta(wheel.dy) });
    }
  };

  const schedulePointerDeltaFlush = () => {
    if (pointerFrameRef.current === null) {
      pointerFrameRef.current = window.requestAnimationFrame(sendPendingPointerDeltas);
    }
  };

  const flushPendingPointerDeltas = () => {
    if (pointerFrameRef.current !== null) {
      window.cancelAnimationFrame(pointerFrameRef.current);
    }

    if (pendingPointerMoveRef.current || pendingPointerWheelRef.current) {
      sendPendingPointerDeltas();
      return;
    }

    pointerFrameRef.current = null;
  };

  const emit = (payload: ClientMessage) => {
    if (state !== "paired" && payload.type !== "pair.hello") {
      return;
    }

    if (payload.type === "pointer.move") {
      const current = pendingPointerMoveRef.current ?? { dx: 0, dy: 0 };
      pendingPointerMoveRef.current = { dx: current.dx + payload.dx, dy: current.dy + payload.dy };
      schedulePointerDeltaFlush();
      return;
    }

    if (payload.type === "pointer.wheel") {
      const current = pendingPointerWheelRef.current ?? { dx: 0, dy: 0 };
      pendingPointerWheelRef.current = { dx: current.dx + payload.dx, dy: current.dy + payload.dy };
      schedulePointerDeltaFlush();
      return;
    }

    flushPendingPointerDeltas();
    send(payload);
  };

  const onTouchStart = (event: TouchEvent<HTMLDivElement>) => {
    event.preventDefault();
    recognizerRef.current.start(touchesFromList(event.targetTouches), event.timeStamp);
  };

  const onTouchMove = (event: TouchEvent<HTMLDivElement>) => {
    event.preventDefault();
    recognizerRef.current.move(touchesFromList(event.targetTouches), event.timeStamp, trackpadSettings).forEach(emit);
  };

  const onTouchEnd = (event: TouchEvent<HTMLDivElement>) => {
    event.preventDefault();
    const outputs = recognizerRef.current.end(event.timeStamp, trackpadSettings);
    if (outputs.some((output) => output.type === "pointer.button" && output.action === "click")) {
      triggerHapticFeedback(trackpadSettings);
    }
    outputs.forEach(emit);
  };

  const onTouchCancel = (event: TouchEvent<HTMLDivElement>) => {
    event.preventDefault();
    recognizerRef.current.cancel();
  };

  const sendSpecial = (key: string, modifiers?: string[]) => {
    emit({ type: "keyboard.special", key, modifiers } satisfies KeyboardSpecialMessage);
  };

  const sendText = (text: string) => {
    if (text.length > 0) {
      emit({ type: "keyboard.text", text });
    }
  };

  const sleepPc = () => emit({ type: "system.sleep" });

  return { emit, onTouchCancel, onTouchEnd, onTouchMove, onTouchStart, sendSpecial, sendText, sleepPc };
}

function roundDelta(value: number): number {
  const rounded = Math.round(value * 100) / 100;
  return Object.is(rounded, -0) ? 0 : rounded;
}
