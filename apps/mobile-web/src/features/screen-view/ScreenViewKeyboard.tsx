import { useLayoutEffect, useState } from "react";
import { toLiveKeyboardValue } from "../../foundation/input/keyboardDelta";
import { useKeyboardInput } from "../../foundation/input/useKeyboardInput";
import type { ClientMessage } from "../../foundation/protocol/messages";

export function ScreenViewKeyboard({
  enabled,
  send,
}: {
  enabled: boolean;
  send: (message: ClientMessage) => void;
}) {
  const [inputMode, setInputMode] = useState<"text" | "numeric">("text");
  const keyboard = useKeyboardInput((message) => {
    if (enabled) {
      send({ ...message, inputContext: "screen-view" });
    }
  }, true);
  const { keyboardTextareaRef, isComposingRef } = keyboard;
  const [supportsInputMode] = useState(() => "inputMode" in document.createElement("textarea"));

  useLayoutEffect(() => {
    if (!enabled || !supportsInputMode) {
      return;
    }
    const textarea = keyboardTextareaRef.current;
    const virtualKeyboard = "virtualKeyboard" in navigator ? navigator.virtualKeyboard : null;
    const overlayKeyboard =
      virtualKeyboard &&
      typeof virtualKeyboard === "object" &&
      "overlaysContent" in virtualKeyboard &&
      typeof virtualKeyboard.overlaysContent === "boolean"
        ? virtualKeyboard
        : null;
    const previousOverlay = overlayKeyboard?.overlaysContent;
    if (overlayKeyboard) {
      overlayKeyboard.overlaysContent = true;
    }
    return () => {
      textarea?.blur();
      if (textarea) {
        textarea.value = "";
      }
      if (overlayKeyboard) {
        overlayKeyboard.overlaysContent = previousOverlay;
      }
    };
  }, [enabled, keyboardTextareaRef, supportsInputMode]);

  function showKeyboard(mode: "text" | "numeric") {
    const textarea = keyboardTextareaRef.current;
    if (!enabled || !textarea) {
      return;
    }
    setInputMode(mode);
    // Keep focus inside the fullscreen element and inside this user activation.
    // Refocusing also requests a new layout on keyboards that cache inputMode.
    if (textarea.inputMode !== mode) {
      textarea.blur();
    }
    textarea.inputMode = mode;
    textarea.focus({ preventScroll: true });
    textarea.setSelectionRange(textarea.value.length, textarea.value.length);
  }

  if (!supportsInputMode) {
    return null;
  }

  return (
    <>
      {(["text", "numeric"] as const).map((mode) => (
        <button
          key={mode}
          type="button"
          className="screen-view-two-finger-mode"
          disabled={!enabled}
          aria-label={mode === "text" ? "Show device keyboard" : "Show device numeric keyboard"}
          title={enabled ? undefined : "Allow Pointer and keyboard for this device on the PC"}
          onTouchStart={(event) => event.stopPropagation()}
          onTouchMove={(event) => event.stopPropagation()}
          onTouchEnd={(event) => event.stopPropagation()}
          onTouchCancel={(event) => event.stopPropagation()}
          onClick={() => showKeyboard(mode)}
        >
          {mode === "text" ? "ABC" : "123"}
        </button>
      ))}
      <textarea
        ref={keyboardTextareaRef}
        className="screen-view-keyboard-input"
        aria-label="Type on PC"
        tabIndex={-1}
        disabled={!enabled}
        inputMode={inputMode}
        autoComplete="off"
        autoCapitalize="off"
        spellCheck={false}
        value={toLiveKeyboardValue(keyboard.keyboardText)}
        onFocus={keyboard.placeLiveKeyboardCaret}
        onChange={(event) => keyboard.onKeyboardTextChange(event.currentTarget.value)}
        onBeforeInput={(event) => {
          if (keyboard.sendEmptyDelete(event.nativeEvent.inputType, event.timeStamp)) {
            event.preventDefault();
          }
        }}
        onKeyDown={(event) => {
          if (
            (event.key === "Backspace" || event.key === "Delete") &&
            keyboard.sendEmptyDelete(event.key, event.timeStamp)
          ) {
            event.preventDefault();
          }
        }}
        onCompositionStart={() => {
          isComposingRef.current = true;
        }}
        onCompositionEnd={(event) => {
          isComposingRef.current = false;
          keyboard.onKeyboardTextChange(event.currentTarget.value);
        }}
      />
    </>
  );
}
