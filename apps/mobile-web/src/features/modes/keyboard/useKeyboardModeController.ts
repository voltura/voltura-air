import { useEffect, useId, useRef, useState } from "react";
import type React from "react";
import { liveKeyboardSentinel } from "../../../foundation/input/keyboardDelta";
import type { KeyboardInputMode } from "./KeyboardInputModeButtons";

const repeatStartDelayMs = 400;
const repeatIntervalMs = 55;

type RepeatableKey = "Backspace" | "Delete" | "Enter" | "Tab" | "ArrowUp" | "ArrowDown" | "ArrowLeft" | "ArrowRight" | "Home" | "End" | "PageUp" | "PageDown";
interface KeyboardModeControllerOptions {
  committedKeyboardTextRef: React.RefObject<string>;
  keyboardTextareaRef: React.RefObject<HTMLTextAreaElement | null>;
  liveKeyboard: boolean;
  sendSpecial: (key: string, modifiers?: string[]) => void;
  setKeyboardText: React.Dispatch<React.SetStateAction<string>>;
  setLiveTyping: (enabled: boolean) => void;
}

export function useKeyboardModeController({
  committedKeyboardTextRef,
  keyboardTextareaRef,
  liveKeyboard,
  sendSpecial,
  setKeyboardText,
  setLiveTyping
}: KeyboardModeControllerOptions) {
  const repeatTimeoutRef = useRef<number | null>(null);
  const repeatIntervalRef = useRef<number | null>(null);
  const ignoreNextClickRef = useRef(false);
  const liveKeyboardRef = useRef(liveKeyboard);
  const sendSpecialRef = useRef(sendSpecial);
  const [keyboardInputMode, setKeyboardInputMode] = useState<KeyboardInputMode>("text");
  const liveTypingId = useId();

  const stopRepeatingKey = () => {
    if (repeatTimeoutRef.current !== null) {
      window.clearTimeout(repeatTimeoutRef.current);
      repeatTimeoutRef.current = null;
    }

    if (repeatIntervalRef.current !== null) {
      window.clearInterval(repeatIntervalRef.current);
      repeatIntervalRef.current = null;
    }
  };

  useEffect(() => {
    const stopOnVisibilityLoss = () => {
      if (document.visibilityState === "hidden") {
        stopRepeatingKey();
      }
    };
    window.addEventListener("blur", stopRepeatingKey);
    document.addEventListener("visibilitychange", stopOnVisibilityLoss);
    return () => {
      window.removeEventListener("blur", stopRepeatingKey);
      document.removeEventListener("visibilitychange", stopOnVisibilityLoss);
      stopRepeatingKey();
    };
  }, []);

  useEffect(() => {
    liveKeyboardRef.current = liveKeyboard;
    stopRepeatingKey();
  }, [liveKeyboard]);

  useEffect(() => {
    sendSpecialRef.current = sendSpecial;
  }, [sendSpecial]);

  const focusLiveKeyboardTarget = (inputMode = keyboardInputMode) => {
    const textarea = keyboardTextareaRef.current;
    if (!textarea) {
      return;
    }

    textarea.inputMode = inputMode;
    textarea.focus({ preventScroll: true });
    window.requestAnimationFrame(() => {
      const currentTextarea = keyboardTextareaRef.current;
      if (!currentTextarea) {
        return;
      }

      currentTextarea.inputMode = inputMode;
      currentTextarea.focus({ preventScroll: true });
      const caretPosition = currentTextarea.value.length;
      currentTextarea.setSelectionRange(caretPosition, caretPosition);
    });
  };

  const handleLiveTypingChange = (event: React.ChangeEvent<HTMLInputElement>) => {
    const enabled = event.target.checked;
    if (enabled) {
      focusLiveKeyboardTarget();
    }

    setLiveTyping(enabled);
  };

  const showKeyboardInputMode = (inputMode: KeyboardInputMode) => {
    setKeyboardInputMode(inputMode);
    if (!liveKeyboard) {
      setLiveTyping(true);
    }
    focusLiveKeyboardTarget(inputMode);
  };

  const setBufferedKeyboardText = (nextText: string, caretPosition: number) => {
    const textarea = keyboardTextareaRef.current;
    if (textarea && !liveKeyboard) {
      textarea.value = nextText;
      textarea.setSelectionRange(caretPosition, caretPosition);
    }

    setKeyboardText(nextText);
    committedKeyboardTextRef.current = nextText;

    window.requestAnimationFrame(() => {
      const currentTextarea = keyboardTextareaRef.current;
      if (!currentTextarea || liveKeyboard) {
        return;
      }

      currentTextarea.focus();
      currentTextarea.setSelectionRange(caretPosition, caretPosition);
    });
  };

  const setLiveKeyboardText = (nextText: string, caretPosition: number) => {
    setKeyboardText(nextText);
    committedKeyboardTextRef.current = nextText;

    window.requestAnimationFrame(() => {
      const textarea = keyboardTextareaRef.current;
      if (!textarea) {
        return;
      }

      textarea.focus({ preventScroll: true });
      const nextCaret = liveKeyboardSentinel.length + Math.max(0, Math.min(caretPosition, nextText.length));
      textarea.setSelectionRange(nextCaret, nextCaret);
    });
  };

  const getLiveSelection = (currentText: string) => {
    const textarea = keyboardTextareaRef.current;
    if (!textarea) {
      return { start: currentText.length, end: currentText.length };
    }

    const selectionStart = textarea.selectionStart ?? textarea.value.length;
    const selectionEnd = textarea.selectionEnd ?? selectionStart;
    const start = Math.max(0, Math.min(selectionStart, selectionEnd) - liveKeyboardSentinel.length);
    const end = Math.max(0, Math.max(selectionStart, selectionEnd) - liveKeyboardSentinel.length);
    return {
      start: Math.min(start, currentText.length),
      end: Math.min(end, currentText.length)
    };
  };

  const insertLiveKeyboardText = (text: string) => {
    const currentText = committedKeyboardTextRef.current;
    const { start, end } = getLiveSelection(currentText);
    const nextText = `${currentText.slice(0, start)}${text}${currentText.slice(end)}`;
    setLiveKeyboardText(nextText, start + text.length);
  };

  const applyLiveSpecialKey = (key: RepeatableKey) => {
    const currentText = committedKeyboardTextRef.current;
    const { start, end } = getLiveSelection(currentText);

    if (key === "Backspace") {
      if (start !== end) {
        setLiveKeyboardText(`${currentText.slice(0, start)}${currentText.slice(end)}`, start);
        return;
      }

      if (start > 0) {
        const nextCaret = start - 1;
        setLiveKeyboardText(`${currentText.slice(0, nextCaret)}${currentText.slice(end)}`, nextCaret);
      }
      return;
    }

    if (key === "Delete") {
      if (start !== end) {
        setLiveKeyboardText(`${currentText.slice(0, start)}${currentText.slice(end)}`, start);
        return;
      }

      if (end < currentText.length) {
        setLiveKeyboardText(`${currentText.slice(0, start)}${currentText.slice(end + 1)}`, start);
      }
      return;
    }

    if (key === "Enter" || key === "Tab") {
      insertLiveKeyboardText(key === "Enter" ? "\n" : "\t");
      return;
    }

    if (key === "Home") {
      setLiveKeyboardText(currentText, getLineStart(currentText, start));
      return;
    }

    if (key === "End") {
      setLiveKeyboardText(currentText, getLineEnd(currentText, end));
      return;
    }

    if (key === "PageUp" || key === "PageDown") {
      setLiveKeyboardText(currentText, key === "PageUp" ? 0 : currentText.length);
      return;
    }

    if (key === "ArrowLeft") {
      setLiveKeyboardText(currentText, start === end ? Math.max(0, start - 1) : start);
      return;
    }

    if (key === "ArrowRight") {
      setLiveKeyboardText(currentText, start === end ? Math.min(currentText.length, end + 1) : end);
      return;
    }

    const nextCaret = getVerticalCaretPosition(currentText, start, key === "ArrowUp" ? -1 : 1);
    setLiveKeyboardText(currentText, nextCaret);
  };

  const applyBufferedSpecialKey = (key: RepeatableKey) => {
    const textarea = keyboardTextareaRef.current;
    if (!textarea) {
      return;
    }

    const value = textarea.value;
    const selectionStart = textarea.selectionStart ?? value.length;
    const selectionEnd = textarea.selectionEnd ?? selectionStart;
    const start = Math.min(selectionStart, selectionEnd);
    const end = Math.max(selectionStart, selectionEnd);

    if (key === "Backspace") {
      if (start !== end) {
        setBufferedKeyboardText(`${value.slice(0, start)}${value.slice(end)}`, start);
        return;
      }

      if (start > 0) {
        const nextCaret = start - 1;
        setBufferedKeyboardText(`${value.slice(0, nextCaret)}${value.slice(end)}`, nextCaret);
      }
      return;
    }

    if (key === "Delete") {
      if (start !== end) {
        setBufferedKeyboardText(`${value.slice(0, start)}${value.slice(end)}`, start);
        return;
      }

      if (end < value.length) {
        setBufferedKeyboardText(`${value.slice(0, start)}${value.slice(end + 1)}`, start);
      }
      return;
    }

    if (key === "Enter" || key === "Tab") {
      const insertedText = key === "Enter" ? "\n" : "\t";
      const nextCaret = start + insertedText.length;
      setBufferedKeyboardText(`${value.slice(0, start)}${insertedText}${value.slice(end)}`, nextCaret);
      return;
    }

    if (key === "Home") {
      setBufferedKeyboardText(value, getLineStart(value, start));
      return;
    }

    if (key === "End") {
      setBufferedKeyboardText(value, getLineEnd(value, end));
      return;
    }

    if (key === "PageUp" || key === "PageDown") {
      setBufferedKeyboardText(value, key === "PageUp" ? 0 : value.length);
      return;
    }

    if (key === "ArrowLeft") {
      setBufferedKeyboardText(value, start === end ? Math.max(0, start - 1) : start);
      return;
    }

    if (key === "ArrowRight") {
      setBufferedKeyboardText(value, start === end ? Math.min(value.length, end + 1) : end);
      return;
    }

    const nextCaret = getVerticalCaretPosition(value, start, key === "ArrowUp" ? -1 : 1);
    setBufferedKeyboardText(value, nextCaret);
  };

  const insertBufferedKeyboardText = (text: string) => {
    const textarea = keyboardTextareaRef.current;
    if (!textarea) {
      return;
    }

    const value = textarea.value;
    const selectionStart = textarea.selectionStart ?? value.length;
    const selectionEnd = textarea.selectionEnd ?? selectionStart;
    const start = Math.min(selectionStart, selectionEnd);
    const end = Math.max(selectionStart, selectionEnd);
    const nextCaret = start + text.length;
    setBufferedKeyboardText(`${value.slice(0, start)}${text}${value.slice(end)}`, nextCaret);
  };

  const pressRepeatableKey = (key: RepeatableKey) => {
    if (liveKeyboardRef.current) {
      sendSpecialRef.current(key);
      applyLiveSpecialKey(key);
      return;
    }

    applyBufferedSpecialKey(key);
  };

  const sendSpace = () => {
    if (liveKeyboard) {
      sendSpecial("Space");
      insertLiveKeyboardText(" ");
      return;
    }

    insertBufferedKeyboardText(" ");
  };

  const sendShortcut = (key: string, modifiers?: string[]) => {
    if (modifiers) {
      sendSpecial(key, modifiers);
      return;
    }

    sendSpecial(key);
  };

  const getRepeatableKeyProps = (key: RepeatableKey) => ({
    onPointerDown: (event: React.PointerEvent<HTMLButtonElement>) => {
      if (event.button !== 0) {
        return;
      }

      event.preventDefault();
      ignoreNextClickRef.current = true;
      event.currentTarget.setPointerCapture?.(event.pointerId);
      stopRepeatingKey();
      pressRepeatableKey(key);
      repeatTimeoutRef.current = window.setTimeout(() => {
        pressRepeatableKey(key);
        repeatIntervalRef.current = window.setInterval(() => { pressRepeatableKey(key); }, repeatIntervalMs);
      }, repeatStartDelayMs);
    },
    onPointerUp: stopRepeatingKey,
    onPointerCancel: stopRepeatingKey,
    onPointerLeave: stopRepeatingKey,
    onLostPointerCapture: stopRepeatingKey,
    onClick: () => {
      if (ignoreNextClickRef.current) {
        ignoreNextClickRef.current = false;
        return;
      }

      pressRepeatableKey(key);
    }
  });

  return {
    getRepeatableKeyProps,
    handleLiveTypingChange,
    keyboardInputMode,
    liveTypingId,
    sendShortcut,
    sendSpace,
    showKeyboardInputMode
  };
}
function getLineStart(value: string, caret: number): number {
  return value.lastIndexOf("\n", Math.max(0, caret - 1)) + 1;
}

function getLineEnd(value: string, caret: number): number {
  const nextLineBreak = value.indexOf("\n", caret);
  return nextLineBreak === -1 ? value.length : nextLineBreak;
}

function getVerticalCaretPosition(value: string, caret: number, direction: -1 | 1): number {
  const currentLineStart = getLineStart(value, caret);
  const currentColumn = caret - currentLineStart;

  if (direction === -1) {
    if (currentLineStart === 0) {
      return caret;
    }

    const previousLineEnd = currentLineStart - 1;
    const previousLineStart = value.lastIndexOf("\n", previousLineEnd - 1) + 1;
    return Math.min(previousLineStart + currentColumn, previousLineEnd);
  }

  const currentLineEndIndex = value.indexOf("\n", caret);
  if (currentLineEndIndex === -1) {
    return caret;
  }

  const nextLineStart = currentLineEndIndex + 1;
  const nextLineEndIndex = value.indexOf("\n", nextLineStart);
  const nextLineEnd = nextLineEndIndex === -1 ? value.length : nextLineEndIndex;
  return Math.min(nextLineStart + currentColumn, nextLineEnd);
}
