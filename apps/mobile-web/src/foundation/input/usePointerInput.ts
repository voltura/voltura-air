import { useCallback, useEffect, useRef, type TouchEvent } from "react";
import type { ConnectionState } from "../connection/connectionTypes";
import { GestureRecognizer, touchesFromList, type TrackpadSettings, type TwoFingerMode } from "./gestures";
import type { ClientMessage, InputContext, KeyboardSpecialMessage } from "../protocol/messages";
import { triggerHapticFeedback } from "./hapticFeedback";

interface PointerInputOptions {
  send: (payload: ClientMessage) => void;
  state: ConnectionState;
  trackpadSettings: TrackpadSettings;
  twoFingerMode?: TwoFingerMode;
  inputContext?: InputContext | null;
}

interface PendingPointerDelta {
  dx: number;
  dy: number;
  inputContext: InputContext | null;
}

type PointerEmission = Extract<ClientMessage, { type:
  | "pointer.move"
  | "pointer.button"
  | "pointer.wheel"
  | "pointer.zoom"
  | "keyboard.text"
  | "keyboard.special"
  | "audio.mute.toggle"
  | "audio.volume.set"
  | "system.sleep"
}>;

export function usePointerInput({ send, state, trackpadSettings, twoFingerMode = "scroll", inputContext = "trackpad" }: PointerInputOptions) {
  const recognizerRef = useRef(new GestureRecognizer());
  const pointerFrameRef = useRef<number | null>(null);
  const pendingPointerMoveRef = useRef<PendingPointerDelta | null>(null);
  const pendingPointerWheelRef = useRef<PendingPointerDelta | null>(null);
  const sendRef = useRef(send);
  const stateRef = useRef(state);

  const cancel = useCallback(() => {
    if (pointerFrameRef.current !== null) {
      window.cancelAnimationFrame(pointerFrameRef.current);
      pointerFrameRef.current = null;
    }
    pendingPointerMoveRef.current = null;
    pendingPointerWheelRef.current = null;
    recognizerRef.current.cancel();
  }, []);

  useEffect(() => {
    sendRef.current = send;
    stateRef.current = state;
  }, [send, state]);

  useEffect(() => () => {
    cancel();
  }, [cancel]);

  const sendPendingPointerDeltas = () => {
    pointerFrameRef.current = null;
    const move = pendingPointerMoveRef.current;
    const wheel = pendingPointerWheelRef.current;
    pendingPointerMoveRef.current = null;
    pendingPointerWheelRef.current = null;

    if (stateRef.current !== "paired") {
      return;
    }

    if (move && (move.dx !== 0 || move.dy !== 0)) {
      sendRef.current({
        type: "pointer.move",
        ...(move.inputContext === null ? {} : { inputContext: move.inputContext }),
        dx: roundDelta(move.dx),
        dy: roundDelta(move.dy)
      });
    }

    if (wheel && (wheel.dx !== 0 || wheel.dy !== 0)) {
      sendRef.current({
        type: "pointer.wheel",
        ...(wheel.inputContext === null ? {} : { inputContext: wheel.inputContext }),
        dx: roundDelta(wheel.dx),
        dy: roundDelta(wheel.dy)
      });
    }
  };

  const schedulePointerDeltaFlush = () => {
    pointerFrameRef.current ??= window.requestAnimationFrame(sendPendingPointerDeltas);
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

  const emit = (payload: PointerEmission) => {
    if (state !== "paired") {
      return;
    }

    if (payload.type === "pointer.move" || payload.type === "pointer.wheel") {
      const pendingRef = payload.type === "pointer.move" ? pendingPointerMoveRef : pendingPointerWheelRef;
      const contextualInput = payload.inputContext ?? inputContext;
      let pending = pendingRef.current;
      if (pending && pending.inputContext !== contextualInput) {
        flushPendingPointerDeltas();
        pending = null;
      }
      pendingRef.current = pending
        ? { dx: pending.dx + payload.dx, dy: pending.dy + payload.dy, inputContext: pending.inputContext }
        : { dx: payload.dx, dy: payload.dy, inputContext: contextualInput };
      schedulePointerDeltaFlush();
      return;
    }

    flushPendingPointerDeltas();
    if (payload.type === "system.sleep") {
      send(payload);
      return;
    }
    const contextualInput = payload.inputContext ?? inputContext;
    send(contextualInput === null ? payload : { ...payload, inputContext: contextualInput });
  };

  const onTouchStart = (event: TouchEvent<HTMLDivElement>) => {
    event.preventDefault();
    recognizerRef.current.start(touchesFromList(event.targetTouches), event.timeStamp);
  };

  const onTouchMove = (event: TouchEvent<HTMLDivElement>) => {
    event.preventDefault();
    recognizerRef.current.move(touchesFromList(event.targetTouches), event.timeStamp, trackpadSettings, twoFingerMode).forEach(emit);
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
    cancel();
  };

  const sendSpecial = (key: string, modifiers?: string[], context?: InputContext) => {
    emit({
      type: "keyboard.special",
      key,
      modifiers,
      ...(context ? { inputContext: context } : {}),
    } satisfies KeyboardSpecialMessage);
  };

  const sendText = (text: string) => {
    if (text.length > 0) {
      emit({ type: "keyboard.text", text });
    }
  };

  const sleepPc = () => { emit({ type: "system.sleep" }); };

  return { cancel, emit, onTouchCancel, onTouchEnd, onTouchMove, onTouchStart, sendSpecial, sendText, sleepPc };
}

function roundDelta(value: number): number {
  const rounded = Math.round(value * 100) / 100;
  return Object.is(rounded, -0) ? 0 : rounded;
}
