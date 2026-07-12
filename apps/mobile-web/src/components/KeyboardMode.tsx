import { useEffect, useId, useRef, useState } from "react";
import { ArrowDown, ArrowLeft, ArrowRight, ArrowUp, Send } from "lucide-react";
import { liveKeyboardSentinel } from "../keyboardDelta";
import { InfoButton } from "./InfoButton";

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
  { label: "Select all", key: "A", modifiers: ["Control"] },
  { label: "Cut", key: "X", modifiers: ["Control"] },
  { label: "Copy", key: "C", modifiers: ["Control"] },
  { label: "Paste", key: "V", modifiers: ["Control"] },
  { label: "Undo", key: "Undo" },
  { label: "Redo", key: "Redo" }
];

const appSwitchShortcutKeys: ShortcutKey[] = [
  { label: "Next app", key: "Tab", modifiers: ["Alt"] },
  { label: "Previous app", key: "Tab", modifiers: ["Shift", "Alt"] }
];

type KeyboardModeProps = {
  committedKeyboardTextRef: React.RefObject<string>;
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
  isComposingRef: React.RefObject<boolean>;
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
    if (liveKeyboard) {
      sendSpecial(key);
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
        <div className="live-typing-switch">
          <span className="setting-label-with-info">
            <label htmlFor={liveTypingId}>Live typing</label>
            <InfoButton
              title="Live typing"
              description="Sends each character to the focused application on your PC as you type. Turn it off to compose text on your device first, then press Send."
            />
          </span>
          <label className="switch-control" htmlFor={liveTypingId}>
            <input id={liveTypingId} type="checkbox" role="switch" aria-checked={liveKeyboard} checked={liveKeyboard} onChange={handleLiveTypingChange} />
            <span className="switch-track" aria-hidden="true">
              <span className="switch-thumb" />
            </span>
          </label>
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
          placeholder="Tap here and type..."
        />
        <div className="keyboard-input-mode-buttons segmented-control" role="tablist" aria-label="Device keyboard type">
          <button
            type="button"
            className={keyboardInputMode === "text" ? "active" : ""}
            aria-label="Show regular keyboard"
            aria-selected={keyboardInputMode === "text"}
            role="tab"
            onClick={() => showKeyboardInputMode("text")}
          >
            ABC
          </button>
          <button
            type="button"
            className={keyboardInputMode === "numeric" ? "active" : ""}
            aria-label="Show numeric keyboard"
            aria-selected={keyboardInputMode === "numeric"}
            role="tab"
            onClick={() => showKeyboardInputMode("numeric")}
          >
            123
          </button>
        </div>
      </div>
      {!liveKeyboard && (
        <div className="keyboard-send-row">
          <button
            className="key-send"
            onClick={() => {
              sendText(keyboardText);
              setKeyboardText("");
              committedKeyboardTextRef.current = "";
            }}
          >
            <Send aria-hidden="true" />
            <span>Send</span>
          </button>
        </div>
      )}
      <div className={`command-row keyboard-primary-keys ${showSleepButton ? "has-sleep-key" : ""}`} aria-label="Primary keyboard keys">
        <button className="key-esc" onClick={() => sendSpecial("Escape")}>Esc</button>
        <button className="key-tab" {...getRepeatableKeyProps("Tab")}>Tab</button>
        <button className="key-win" onClick={() => sendSpecial("Win")}>Win</button>
        <button className="key-space" onClick={sendSpace} aria-label="Space">Space</button>
        <button className="key-enter" {...getRepeatableKeyProps("Enter")}>Enter</button>
        <button className="key-backspace" {...getRepeatableKeyProps("Backspace")}>Backspace</button>
        <button className="key-delete" {...getRepeatableKeyProps("Delete")}>Delete</button>
        {showSleepButton && (
          <button className="key-sleep" type="button" onClick={onSleep}>
            <span>Sleep</span>
          </button>
        )}
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
        <div className="keyboard-navigation-keys" aria-label="Navigation keys">
          <div className="navigation-key-block" aria-label="Document navigation keys">
            <button className="nav-home" {...getRepeatableKeyProps("Home")}>Home</button>
            <button className="nav-page-up" {...getRepeatableKeyProps("PageUp")} aria-label="Page Up">
              PgUp
            </button>
            <button className="nav-page-down" {...getRepeatableKeyProps("PageDown")} aria-label="Page Down">
              PgDn
            </button>
            <button className="nav-end" {...getRepeatableKeyProps("End")}>End</button>
          </div>
          <div className="arrow-pad" aria-label="Arrow keys">
            <button className="arrow-up" {...getRepeatableKeyProps("ArrowUp")} aria-label="Arrow up">
              <ArrowUp aria-hidden="true" />
            </button>
            <button className="arrow-left" {...getRepeatableKeyProps("ArrowLeft")} aria-label="Arrow left">
              <ArrowLeft aria-hidden="true" />
            </button>
            <button className="arrow-down" {...getRepeatableKeyProps("ArrowDown")} aria-label="Arrow down">
              <ArrowDown aria-hidden="true" />
            </button>
            <button className="arrow-right" {...getRepeatableKeyProps("ArrowRight")} aria-label="Arrow right">
              <ArrowRight aria-hidden="true" />
            </button>
          </div>
        </div>
      )}
      {showControlKeys && (
        <div className="keyboard-shortcut-groups" aria-label="Keyboard shortcuts">
          <div className="shortcut-row" aria-label="Editing shortcuts">
            {shortcutKeys.map(({ label, key, modifiers }) => (
              <button key={label} onClick={() => sendShortcut(key, modifiers)} title={key === "Undo" ? "Undo" : key === "Redo" ? "Redo" : undefined}>
                {label}
              </button>
            ))}
          </div>
          <div className="app-switch-row" aria-label="App switching shortcuts">
            {appSwitchShortcutKeys.map(({ label, key, modifiers }) => (
              <button
                key={label}
                aria-label={label}
                onClick={() => sendShortcut(key, modifiers)}
              >
                <span>{label}</span>
              </button>
            ))}
          </div>
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
