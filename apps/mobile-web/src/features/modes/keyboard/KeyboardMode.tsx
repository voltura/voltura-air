import { ArrowDown, ArrowLeft, ArrowRight, ArrowUp, Send } from "lucide-react";
import type React from "react";
import { InfoButton } from "../../../ui/overlays/InfoButton";
import { KeyboardInputModeButtons } from "./KeyboardInputModeButtons";
import { useKeyboardModeController } from "./useKeyboardModeController";

const functionKeys = Array.from({ length: 12 }, (_, index) => `F${index + 1}`);

interface ShortcutKey {
  label: string;
  key: string;
  modifiers?: string[];
}

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

interface KeyboardModeProps {
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
}

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
  const {
    getRepeatableKeyProps,
    handleLiveTypingChange,
    keyboardInputMode,
    liveTypingId,
    sendShortcut,
    sendSpace,
    showKeyboardInputMode
  } = useKeyboardModeController({
    committedKeyboardTextRef,
    keyboardText,
    keyboardTextareaRef,
    liveKeyboard,
    sendSpecial,
    setKeyboardText,
    setLiveTyping
  });
  return (
    <section className={`keyboard-mode ${liveKeyboard ? "live-typing" : ""}`}>
      <div className="live-typing-row">
        <div className="live-typing-switch">
          <span className="setting-label-with-info">
            <span>Live typing</span>
            <InfoButton
              title="Live typing"
              description="Sends each character to the focused application on your PC as you type. Turn it off to compose text on your device first, then press Send."
            />
          </span>
          <label className="switch-control" htmlFor={liveTypingId}>
            <span className="visually-hidden">Live typing</span>
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
          onChange={(event) => { onKeyboardTextChange(event.target.value); }}
          onFocus={placeLiveKeyboardCaret}
          onClick={placeLiveKeyboardCaret}
          onBeforeInput={(event) => {
            const inputType = (event.nativeEvent).inputType;
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
        <KeyboardInputModeButtons inputMode={keyboardInputMode} onInputModeChange={showKeyboardInputMode} />
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
        <button className="key-esc" onClick={() => { sendSpecial("Escape"); }}>Esc</button>
        <button className="key-tab" {...getRepeatableKeyProps("Tab")}>Tab</button>
        <button className="key-win" onClick={() => { sendSpecial("Win"); }}>Win</button>
        <button className="key-space" onClick={sendSpace} aria-label="Space">Space</button>
        <button className="key-enter" {...getRepeatableKeyProps("Enter")}>Enter</button>
        <button className="key-backspace" aria-label="Backspace" {...getRepeatableKeyProps("Backspace")}>
          <span className="key-backspace-label-full" aria-hidden="true">Backspace</span>
          <span className="key-backspace-label-short" aria-hidden="true">Back</span>
        </button>
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
            <button key={key} onClick={() => { sendSpecial(key); }}>
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
              <button key={label} onClick={() => { sendShortcut(key, modifiers); }} title={key === "Undo" ? "Undo" : key === "Redo" ? "Redo" : undefined}>
                {label}
              </button>
            ))}
          </div>
          <div className="app-switch-row" aria-label="App switching shortcuts">
            {appSwitchShortcutKeys.map(({ label, key, modifiers }) => (
              <button
                key={label}
                aria-label={label}
                onClick={() => { sendShortcut(key, modifiers); }}
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
