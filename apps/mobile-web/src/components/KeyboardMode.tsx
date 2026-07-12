import { useEffect, useRef, useState } from "react";
import { ArrowDown, ArrowLeft, ArrowRight, ArrowUp, Send, Space } from "lucide-react";
import { liveKeyboardSentinel } from "../keyboardDelta";

const functionKeys = Array.from({ length: 12 }, (_, index) => `F${index + 1}`);
const repeatStartDelayMs = 400;
const repeatIntervalMs = 55;

type RepeatableKey = "Backspace" | "Delete" | "Enter" | "Tab" | "ArrowUp" | "ArrowDown" | "ArrowLeft" | "ArrowRight" | "Home" | "End" | "PageUp" | "PageDown";
type KeyboardInputMode = "text" | "numeric";
type ShortcutKey = {
  label: string;
  key: string;
  modifiers?: string[];
};

const shortcutKeys: ShortcutKey[] = [
  { label: "Ctrl A", key: "A", modifiers: ["Control"] },
  { label: "Ctrl C", key: "C", modifiers: ["Control"] },
  { label: "Ctrl V", key: "V", modifiers: ["Control"] },
  { label: "Ctrl X", key: "X", modifiers: ["Control"] },
  { label: "Ctrl Z", key: "Undo" },
  { label: "Ctrl Y", key: "Redo" },
  { label: "Alt Tab", key: "Tab", modifiers: ["Alt"] },
  { label: "Shift Alt Tab", key: "Tab", modifiers: ["Shift", "Alt"] }
];

type KeyboardModeProps = {
  committedKeyboardTextRef: React.MutableRefObject<string>;
  keyboardText: string;
  keyboardTextareaRef: React.RefObject<HTMLTextAreaElement | null>;
  liveKeyboard: boolean;
  onKeyboardTextChange: (next: string) => void;
  onSleep: () => void;
  placeLiveKeyboardCaret: () => void;
  sendEmptyDelete: (inputTypeOrKey: string, timeStamp: number) => boolean;
  sendSpecial: (key: string, modifiers?: string[]) => void;
  sendText: (text: string) => void;
  setKeyboardText: React.Dispatch<React.SetStateAction<string>>;
  setLiveTyping: (enabled: boolean) => void;
  showArrowKeys: boolean;
  showControlKeys: boolean;
  showFunctionKeys: boolean;
  showSleepButton: boolean;
  toLiveKeyboardValue: (value: string) => string;
  isComposingRef: React.MutableRefObject<boolean>;
};

export function KeyboardMode({
  committedKeyboardTextRef,
  keyboardText,
  keyboardTextareaRef,
  liveKeyboard,
  onKeyboardTextChange,
  onSleep,
  placeLiveKeyboardCaret,
  sendEmptyDelete,
  sendSpecial,
  sendText,
  setKeyboardText,
  setLiveTyping,
  showArrowKeys,
  showControlKeys,
  showFunctionKeys,
  showSleepButton,
  toLiveKeyboardValue,
  isComposingRef
}: KeyboardModeProps) {
  const repeatTimeoutRef = useRef<number | null>(null);
  const repeatIntervalRef = useRef<number | null>(null);
  const ignoreNextClickRef = useRef(false);
  const [keyboardInputMode, setKeyboardInputMode] = useState<KeyboardInputMode>("text");

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

  useEffect(() => stopRepeatingKey, []);

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

  const getLiveSelection = () => {
    const textarea = keyboardTextareaRef.current;
    if (!textarea) {
      return { start: keyboardText.length, end: keyboardText.length };
    }

    const selectionStart = textarea.selectionStart ?? textarea.value.length;
    const selectionEnd = textarea.selectionEnd ?? selectionStart;
    const start = Math.max(0, Math.min(selectionStart, selectionEnd) - liveKeyboardSentinel.length);
    const end = Math.max(0, Math.max(selectionStart, selectionEnd) - liveKeyboardSentinel.length);
    return {
      start: Math.min(start, keyboardText.length),
      end: Math.min(end, keyboardText.length)
    };
  };

  const insertLiveKeyboardText = (text: string) => {
    const { start, end } = getLiveSelection();
    const nextText = `${keyboardText.slice(0, start)}${text}${keyboardText.slice(end)}`;
    setLiveKeyboardText(nextText, start + text.length);
  };

  const applyLiveSpecialKey = (key: RepeatableKey) => {
    const { start, end } = getLiveSelection();

    if (key === "Backspace") {
      if (start !== end) {
        setLiveKeyboardText(`${keyboardText.slice(0, start)}${keyboardText.slice(end)}`, start);
        return;
      }

      if (start > 0) {
        const nextCaret = start - 1;
        setLiveKeyboardText(`${keyboardText.slice(0, nextCaret)}${keyboardText.slice(end)}`, nextCaret);
      }
      return;
    }

    if (key === "Delete") {
      if (start !== end) {
        setLiveKeyboardText(`${keyboardText.slice(0, start)}${keyboardText.slice(end)}`, start);
        return;
      }

      if (end < keyboardText.length) {
        setLiveKeyboardText(`${keyboardText.slice(0, start)}${keyboardText.slice(end + 1)}`, start);
      }
      return;
    }

    if (key === "Enter" || key === "Tab") {
      insertLiveKeyboardText(key === "Enter" ? "\n" : "\t");
      return;
    }

    if (key === "Home") {
      setLiveKeyboardText(keyboardText, getLineStart(keyboardText, start));
      return;
    }

    if (key === "End") {
      setLiveKeyboardText(keyboardText, getLineEnd(keyboardText, end));
      return;
    }

    if (key === "PageUp" || key === "PageDown") {
      setLiveKeyboardText(keyboardText, key === "PageUp" ? 0 : keyboardText.length);
      return;
    }

    if (key === "ArrowLeft") {
      setLiveKeyboardText(keyboardText, start === end ? Math.max(0, start - 1) : start);
      return;
    }

    if (key === "ArrowRight") {
      setLiveKeyboardText(keyboardText, start === end ? Math.min(keyboardText.length, end + 1) : end);
      return;
    }

    const nextCaret = getVerticalCaretPosition(keyboardText, start, key === "ArrowUp" ? -1 : 1);
    setLiveKeyboardText(keyboardText, nextCaret);
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

  const pressRepeatableKey = (key: RepeatableKey) => {
    if (liveKeyboard) {
      sendSpecial(key);
      applyLiveSpecialKey(key);
      return;
    }

    applyBufferedSpecialKey(key);
  };

  const sendSpace = () => {
    sendText(" ");
    if (liveKeyboard) {
      insertLiveKeyboardText(" ");
    }
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
        repeatIntervalRef.current = window.setInterval(() => pressRepeatableKey(key), repeatIntervalMs);
      }, repeatStartDelayMs);
    },
    onPointerUp: stopRepeatingKey,
    onPointerCancel: stopRepeatingKey,
    onPointerLeave: stopRepeatingKey,
    onClick: () => {
      if (ignoreNextClickRef.current) {
        ignoreNextClickRef.current = false;
        return;
      }

      pressRepeatableKey(key);
    }
  });

  return (
    <section className={`keyboard-mode ${liveKeyboard ? "live-typing" : ""}`}>
      <div className="live-typing-row">
        <label className="toggle-row live-typing-toggle">
          <span>Live typing</span>
          <input type="checkbox" checked={liveKeyboard} onChange={handleLiveTypingChange} />
        </label>
        <div className="keyboard-input-mode-buttons" aria-label="Device keyboard type">
          <button type="button" className={keyboardInputMode === "text" ? "active" : ""} aria-label="Show regular keyboard" onClick={() => showKeyboardInputMode("text")}>
            ABC
          </button>
          <button type="button" className={keyboardInputMode === "numeric" ? "active" : ""} aria-label="Show numeric keyboard" onClick={() => showKeyboardInputMode("numeric")}>
            123
          </button>
        </div>
      </div>
      <textarea
        ref={keyboardTextareaRef}
        inputMode={keyboardInputMode}
        rows={liveKeyboard ? 1 : undefined}
        value={liveKeyboard ? toLiveKeyboardValue(keyboardText) : keyboardText}
        onChange={(event) => onKeyboardTextChange(event.target.value)}
        onFocus={placeLiveKeyboardCaret}
        onClick={placeLiveKeyboardCaret}
        onBeforeInput={(event) => {
          const inputType = (event.nativeEvent as InputEvent).inputType;
          if (sendEmptyDelete(inputType, event.timeStamp)) {
            event.preventDefault();
          }
        }}
        onKeyDown={(event) => {
          if ((event.key === "Backspace" || event.key === "Delete") && sendEmptyDelete(event.key, event.timeStamp)) {
            event.preventDefault();
          }
        }}
        onCompositionStart={() => {
          isComposingRef.current = true;
        }}
        onCompositionEnd={(event) => {
          isComposingRef.current = false;
          onKeyboardTextChange(event.currentTarget.value);
        }}
        placeholder={liveKeyboard ? "Typing is sent directly to Windows" : "Type here, then send to Windows"}
      />
      <div className="command-row">
        {!liveKeyboard && (
          <button
            onClick={() => {
              sendText(keyboardText);
              setKeyboardText("");
              committedKeyboardTextRef.current = "";
            }}
          >
            <Send aria-hidden="true" />
            <span>Send</span>
          </button>
        )}
        <button {...getRepeatableKeyProps("Backspace")}>Backspace</button>
        <button {...getRepeatableKeyProps("Enter")}>Enter</button>
        <button {...getRepeatableKeyProps("Tab")}>Tab</button>
        <button onClick={() => sendSpecial("Escape")}>Esc</button>
        <button onClick={() => sendSpecial("Win")}>Win</button>
        {showSleepButton && (
          <button type="button" onClick={onSleep}>
            <span>Sleep</span>
          </button>
        )}
        <button onClick={sendSpace} aria-label="Space">
          <Space aria-hidden="true" />
        </button>
        <button {...getRepeatableKeyProps("Delete")}>Delete</button>
      </div>
      {showFunctionKeys && (
        <div className="function-key-row" aria-label="Function keys">
          {functionKeys.map((key) => (
            <button key={key} onClick={() => sendSpecial(key)}>
              {key}
            </button>
          ))}
        </div>
      )}
      {showArrowKeys && (
        <div className="arrow-pad">
          <button {...getRepeatableKeyProps("PageDown")} aria-label="Page Down">
            PgDn
          </button>
          <button {...getRepeatableKeyProps("ArrowUp")} aria-label="Arrow up">
            <ArrowUp aria-hidden="true" />
          </button>
          <button {...getRepeatableKeyProps("PageUp")} aria-label="Page Up">
            PgUp
          </button>
          <button {...getRepeatableKeyProps("Home")}>Home</button>
          <button {...getRepeatableKeyProps("ArrowLeft")} aria-label="Arrow left">
            <ArrowLeft aria-hidden="true" />
          </button>
          <button {...getRepeatableKeyProps("ArrowDown")} aria-label="Arrow down">
            <ArrowDown aria-hidden="true" />
          </button>
          <button {...getRepeatableKeyProps("ArrowRight")} aria-label="Arrow right">
            <ArrowRight aria-hidden="true" />
          </button>
          <button {...getRepeatableKeyProps("End")}>End</button>
        </div>
      )}
      {showControlKeys && (
        <div className="function-key-row shortcut-row" aria-label="Keyboard shortcuts">
          {shortcutKeys.map(({ label, key, modifiers }) => (
            <button key={label} onClick={() => sendShortcut(key, modifiers)} title={key === "Undo" ? "Undo" : key === "Redo" ? "Redo" : undefined}>
              {label}
            </button>
          ))}
        </div>
      )}
    </section>
  );
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
