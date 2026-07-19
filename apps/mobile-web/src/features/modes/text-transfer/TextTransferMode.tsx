import { Keyboard, MousePointer2, Send } from "lucide-react";
import { useEffect, useRef, useState } from "react";
import "./text-transfer.css";
import type { TextSendResultMessage, TextTransferTarget } from "../../../foundation/protocol/messages";
import { maxSnippetLength, type SavedTextSnippet } from "../../../foundation/settings/textSnippets";
import { InfoButton } from "../../../ui/overlays/InfoButton";
import { KeyboardInputModeButtons, type KeyboardInputMode } from "../keyboard/KeyboardInputModeButtons";
import { SavedTextSnippets } from "./SavedTextSnippets";

const longTextConfirmationThreshold = 2000;

interface TextTransferModeProps {
  clearAfterSending: boolean;
  clientId: string;
  draft: string;
  leftHandedButtons: boolean;
  onClearAfterSendingChange: (value: boolean) => void;
  onDraftChange: (value: string) => void;
  onPointerButtonClick: (button: "left" | "right") => void;
  onTouchCancel: React.TouchEventHandler<HTMLDivElement>;
  onTouchEnd: React.TouchEventHandler<HTMLDivElement>;
  onTouchMove: React.TouchEventHandler<HTMLDivElement>;
  onTouchStart: React.TouchEventHandler<HTMLDivElement>;
  pending: boolean;
  requestTextTransfer: (text: string, sendEnter?: boolean) => string | null;
  result: TextSendResultMessage | null;
  supported: boolean;
  target?: TextTransferTarget | undefined;
}

export function TextTransferMode(props: TextTransferModeProps) {
  const { clearAfterSending, onDraftChange, result } = props;
  const [snippetCopyFeedback, setSnippetCopyFeedback] = useState<{ name: string } | null>(null);
  const [isEditing, setIsEditing] = useState(true);
  const [keyboardInputMode, setKeyboardInputMode] = useState<KeyboardInputMode>("text");
  const editorRef = useRef<HTMLTextAreaElement>(null);
  const draftRevision = useRef(0);
  const pendingClearOperation = useRef<{
    operationId: string;
    submittedText: string;
    clearAfterSending: boolean;
    draftRevision: number;
  } | null>(null);
  const target = props.target ?? { mode: "focused" as const, displayName: "Currently focused application", available: props.supported };
  const canSend = props.supported && target.available && !props.pending && props.draft.length > 0;
  const clickButtons = props.leftHandedButtons
    ? [
        { label: "Right", button: "right" as const },
        { label: "Left", button: "left" as const }
      ]
    : [
        { label: "Left", button: "left" as const },
      { label: "Right", button: "right" as const }
    ];
  const destinationGuidance = target.mode === "focused"
    ? {
        summary: "Destination follows the active PC window.",
        title: "Focused destination",
        description: "Changing focus on the PC before sending changes where the text goes."
      }
    : target.mode === "clipboard"
      ? {
          summary: "Text is copied for manual paste.",
          title: "Windows clipboard",
          description: "Voltura Air does not paste automatically. Open the destination on the PC and paste when ready."
        }
      : {
          summary: "The PC creates a new item or draft.",
          title: "Managed destination",
          description: "Before pasting, Voltura Air verifies that the intended window is in the foreground. If it cannot confirm that, the text stays on the Windows clipboard for manual paste."
        };

  useEffect(() => {
    draftRevision.current += 1;
  }, [props.draft]);

  useEffect(() => {
    const pendingClear = pendingClearOperation.current;
    if (!result || result.operationId !== pendingClear?.operationId) {
      return;
    }

    pendingClearOperation.current = null;
    if (result.succeeded &&
      pendingClear.clearAfterSending &&
      props.draft === pendingClear.submittedText &&
      draftRevision.current === pendingClear.draftRevision) {
      onDraftChange("");
    }
  }, [onDraftChange, props.draft, result]);

  useEffect(() => {
    if (!snippetCopyFeedback) {
      return;
    }

    const timeout = window.setTimeout(() => { setSnippetCopyFeedback(null); }, 1000);
    return () => { window.clearTimeout(timeout); };
  }, [snippetCopyFeedback]);

  const send = (sendEnter: boolean) => {
    if (!canSend || (props.draft.length >= longTextConfirmationThreshold &&
      !window.confirm(`Send ${props.draft.length.toLocaleString()} characters to the PC? Check that the correct application has focus before continuing.`))) {
      return;
    }

    const operationId = props.requestTextTransfer(props.draft, sendEnter);
    pendingClearOperation.current = operationId
      ? {
          operationId,
          submittedText: props.draft,
          clearAfterSending,
          draftRevision: draftRevision.current
        }
      : null;
  };

  const loadSnippet = (snippet: SavedTextSnippet) => {
    props.onDraftChange(snippet.text);
    setSnippetCopyFeedback({ name: snippet.name });
  };

  const focusEditor = (inputMode = keyboardInputMode) => {
    setKeyboardInputMode(inputMode);
    setIsEditing(true);
    const editor = editorRef.current;
    if (!editor) {
      return;
    }

    editor.readOnly = false;
    editor.inputMode = inputMode;
    editor.focus({ preventScroll: true });
    window.requestAnimationFrame(() => {
      const currentEditor = editorRef.current;
      if (!currentEditor) {
        return;
      }

      currentEditor.inputMode = inputMode;
      currentEditor.focus({ preventScroll: true });
    });
  };

  const useAsTouchpad = () => {
    setIsEditing(false);
    editorRef.current?.blur();
  };

  const stopTouchPropagation: React.TouchEventHandler<HTMLElement> = (event) => { event.stopPropagation(); };

  return (
    <section className="text-transfer-mode">
      <header className="tool-page-header">
        <div>
          <h1>Send text to PC</h1>
          <p>{target.mode === "focused" ? "Text will be sent to the currently focused application." : target.mode === "clipboard" ? "Text will be copied to the Windows clipboard." : `Text will be sent to ${target.displayName}.`}</p>
        </div>
      </header>

      <div className="text-transfer-warning text-transfer-warning-with-info">
        <span>{destinationGuidance.summary}</span>
        <InfoButton title={destinationGuidance.title} description={destinationGuidance.description} />
      </div>

      <div className="text-transfer-editor">
        <label htmlFor="text-transfer-draft">Text to send</label>
        <div
          className={`text-transfer-editor-surface${isEditing ? " is-editing" : ""}${snippetCopyFeedback ? " snippet-copied" : ""}`}
          onContextMenu={(event) => {
            if (!isEditing) {
              event.preventDefault();
            }
          }}
          onTouchCancel={isEditing ? undefined : props.onTouchCancel}
          onTouchEnd={isEditing ? undefined : props.onTouchEnd}
          onTouchMove={isEditing ? undefined : props.onTouchMove}
          onTouchStart={isEditing ? undefined : props.onTouchStart}
        >
          <div className="text-transfer-editor-toolbar">
            <div className="text-transfer-editor-mode segmented-control" aria-label="Text box mode">
              <button
                type="button"
                className={isEditing ? "active" : ""}
                aria-label="Use device keyboard"
                aria-pressed={isEditing}
                onClick={(event) => {
                  event.stopPropagation();
                  focusEditor();
                }}
                onTouchCancel={stopTouchPropagation}
                onTouchEnd={stopTouchPropagation}
                onTouchMove={stopTouchPropagation}
                onTouchStart={stopTouchPropagation}
              >
                <Keyboard aria-hidden="true" />
                {!isEditing && <span>Keyboard</span>}
              </button>
              <button
                type="button"
                className={!isEditing ? "active" : ""}
                aria-label="Touchpad"
                aria-pressed={!isEditing}
                onClick={useAsTouchpad}
                onTouchCancel={stopTouchPropagation}
                onTouchEnd={stopTouchPropagation}
                onTouchMove={stopTouchPropagation}
                onTouchStart={stopTouchPropagation}
              >
                <MousePointer2 aria-hidden="true" />
                {isEditing && <span>Touchpad</span>}
              </button>
            </div>
            {isEditing && (
              <div className="text-transfer-keyboard-type" onPointerDown={(event) => { event.stopPropagation(); }}>
                <KeyboardInputModeButtons inputMode={keyboardInputMode} onInputModeChange={focusEditor} />
              </div>
            )}
          </div>
          {!isEditing && (
            <div className="text-transfer-touchpad-hint" aria-hidden="true">
              <MousePointer2 />
            </div>
          )}
          <textarea
            id="text-transfer-draft"
            ref={editorRef}
            autoCapitalize="sentences"
            inputMode={keyboardInputMode}
            maxLength={maxSnippetLength}
            placeholder={isEditing ? "Type or paste text here…" : undefined}
            readOnly={!isEditing}
            value={props.draft}
            onChange={(event) => { props.onDraftChange(event.target.value); }}
            tabIndex={isEditing ? 0 : -1}
            onPointerDown={(event) => {
              if (!isEditing) {
                event.preventDefault();
              }
            }}
          />
        </div>
        {snippetCopyFeedback && <span className="visually-hidden" role="status">{snippetCopyFeedback.name} copied to the text box.</span>}
      </div>

      {isEditing ? (
        <>
          <div className="text-transfer-actions">
            <button type="button" disabled={!canSend} onClick={() => { send(false); }}>
              <Send aria-hidden="true" />
              <span>{props.pending ? "Waiting for PC…" : "Send text"}</span>
            </button>
            <button type="button" disabled={!canSend} onClick={() => { send(true); }}>
              <span>{props.pending ? "Waiting for PC…" : "Send text + Enter"}</span>
            </button>
          </div>

          <label className="toggle-row text-transfer-clear-setting">
            <span>Clear after sending</span>
            <input type="checkbox" checked={props.clearAfterSending} onChange={(event) => { props.onClearAfterSendingChange(event.target.checked); }} />
          </label>
        </>
      ) : (
        <div className="text-transfer-actions text-transfer-pointer-actions" aria-label="Mouse buttons">
          {clickButtons.map(({ label, button }) => (
            <button key={button} type="button" onClick={() => { props.onPointerButtonClick(button); }}>{label}</button>
          ))}
        </div>
      )}

      {!props.supported && <p className="text-transfer-feedback error" role="alert">Update the Windows host to use acknowledged text transfer.</p>}
      {props.pending && <p className="text-transfer-feedback pending" role="status">Waiting for the PC.</p>}
      {!props.pending && props.result && <p className={`text-transfer-feedback ${props.result.succeeded ? "success" : "error"}`} role={props.result.succeeded ? "status" : "alert"}>{props.result.message}</p>}

      {isEditing && <SavedTextSnippets key={props.clientId} clientId={props.clientId} draft={props.draft} onLoadSnippet={loadSnippet} />}
    </section>
  );
}
