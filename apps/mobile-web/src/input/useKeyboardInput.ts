import { useEffect, useRef, useState } from "react";
import { loadLiveKeyboardDefault, saveLiveKeyboardPreference } from "../appStorage";
import {
  didDeleteLiveKeyboardSentinel,
  fromLiveKeyboardValue,
  getEmptyDeleteMessage,
  getKeyboardDeltaMessages,
  liveKeyboardSentinel
} from "../keyboardDelta";
import type { ClientMessage } from "../protocol";

export function useKeyboardInput(emit: (payload: ClientMessage) => void) {
  const [keyboardText, setKeyboardText] = useState("");
  const [liveKeyboard, setLiveKeyboard] = useState(() => loadLiveKeyboardDefault());
  const committedKeyboardTextRef = useRef("");
  const isComposingRef = useRef(false);
  const lastEmptyDeleteRef = useRef<{ key: string; timeStamp: number } | null>(null);
  const keyboardTextareaRef = useRef<HTMLTextAreaElement | null>(null);

  const placeLiveKeyboardCaret = () => {
    window.requestAnimationFrame(() => {
      const textarea = keyboardTextareaRef.current;
      if (!textarea || !liveKeyboard || document.activeElement !== textarea) {
        return;
      }

      const caretPosition = liveKeyboardSentinel.length + keyboardText.length;
      textarea.setSelectionRange(caretPosition, caretPosition);
    });
  };

  useEffect(() => {
    if (liveKeyboard) {
      placeLiveKeyboardCaret();
    }
  }, [keyboardText, liveKeyboard]);

  const setLiveTyping = (enabled: boolean) => {
    setLiveKeyboard(enabled);
    saveLiveKeyboardPreference(enabled);
    committedKeyboardTextRef.current = keyboardText;
  };

  const onKeyboardTextChange = (next: string) => {
    if (liveKeyboard && didDeleteLiveKeyboardSentinel(keyboardText, next)) {
      emit({ type: "keyboard.special", key: "Backspace" });
      setKeyboardText("");
      committedKeyboardTextRef.current = "";
      placeLiveKeyboardCaret();
      return;
    }

    const normalizedNext = liveKeyboard ? fromLiveKeyboardValue(next) : next;
    setKeyboardText(normalizedNext);

    if (!liveKeyboard) {
      committedKeyboardTextRef.current = normalizedNext;
      return;
    }

    if (isComposingRef.current) {
      return;
    }

    getKeyboardDeltaMessages(committedKeyboardTextRef.current, normalizedNext).forEach(emit);
    committedKeyboardTextRef.current = normalizedNext;
  };

  const sendEmptyDelete = (inputTypeOrKey: string, timeStamp: number) => {
    if (!liveKeyboard || isComposingRef.current) {
      return false;
    }

    const message = getEmptyDeleteMessage(inputTypeOrKey, keyboardText);
    if (!message) {
      return false;
    }

    const previous = lastEmptyDeleteRef.current;
    if (previous?.key === message.key && Math.abs(timeStamp - previous.timeStamp) < 40) {
      return true;
    }

    lastEmptyDeleteRef.current = { key: message.key, timeStamp };
    emit(message);
    return true;
  };

  return {
    committedKeyboardTextRef,
    isComposingRef,
    keyboardText,
    keyboardTextareaRef,
    liveKeyboard,
    onKeyboardTextChange,
    placeLiveKeyboardCaret,
    sendEmptyDelete,
    setKeyboardText,
    setLiveTyping
  };
}
