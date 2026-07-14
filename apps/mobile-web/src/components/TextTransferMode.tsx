import { Save, Send, Trash2 } from "lucide-react";
import { useEffect, useRef, useState } from "react";
import type { TextSendResultMessage, TextTransferTarget } from "../protocol";
import { createLocalId } from "../localId";
import { loadTextSnippets, maxSavedSnippets, maxSnippetLength, normalizeSnippetName, saveTextSnippets, type SavedTextSnippet } from "../textSnippets";

const longTextConfirmationThreshold = 2000;

type SnippetAction = {
  kind: "rename" | "update" | "delete";
  snippet: SavedTextSnippet;
};

type TextTransferModeProps = {
  clearAfterSending: boolean;
  clientId: string;
  draft: string;
  onClearAfterSendingChange: (value: boolean) => void;
  onDraftChange: (value: string) => void;
  pending: boolean;
  requestTextTransfer: (text: string, sendEnter?: boolean) => string | null;
  result: TextSendResultMessage | null;
  supported: boolean;
  target?: TextTransferTarget;
};

export function TextTransferMode(props: TextTransferModeProps) {
  const [snippets, setSnippets] = useState(() => loadTextSnippets(props.clientId));
  const [snippetName, setSnippetName] = useState("");
  const [snippetAction, setSnippetAction] = useState<SnippetAction | null>(null);
  const pendingClearOperation = useRef<string | null>(null);
  const target = props.target ?? { mode: "focused" as const, displayName: "Currently focused application", available: props.supported };
  const canSend = props.supported && target.available && !props.pending && props.draft.length > 0;

  useEffect(() => {
    setSnippets(loadTextSnippets(props.clientId));
  }, [props.clientId]);

  useEffect(() => {
    if (!props.result?.succeeded || props.result.operationId !== pendingClearOperation.current) {
      return;
    }

    pendingClearOperation.current = null;
    if (props.clearAfterSending) {
      props.onDraftChange("");
    }
  }, [props.clearAfterSending, props.onDraftChange, props.result]);

  const send = (sendEnter: boolean) => {
    if (!canSend || (props.draft.length >= longTextConfirmationThreshold &&
      !window.confirm(`Send ${props.draft.length.toLocaleString()} characters to the PC? Check that the correct application has focus before continuing.`))) {
      return;
    }

    pendingClearOperation.current = props.requestTextTransfer(props.draft, sendEnter);
  };

  const updateSnippets = (next: SavedTextSnippet[]) => {
    setSnippets(next);
    saveTextSnippets(props.clientId, next);
  };

  const addSnippet = () => {
    const name = normalizeSnippetName(snippetName);
    if (!name || !props.draft || snippets.length >= maxSavedSnippets) {
      return;
    }

    updateSnippets([...snippets, { id: createLocalId(), name, text: props.draft.slice(0, maxSnippetLength) }]);
    setSnippetName("");
  };

  const completeSnippetAction = (name?: string) => {
    if (!snippetAction) {
      return;
    }

    const { kind, snippet } = snippetAction;
    if (kind === "rename") {
      const normalizedName = normalizeSnippetName(name ?? "");
      if (!normalizedName) {
        return;
      }
      updateSnippets(snippets.map((candidate) => candidate.id === snippet.id ? { ...candidate, name: normalizedName } : candidate));
    } else if (kind === "update" && props.draft) {
      updateSnippets(snippets.map((candidate) => candidate.id === snippet.id ? { ...candidate, text: props.draft.slice(0, maxSnippetLength) } : candidate));
    } else if (kind === "delete") {
      updateSnippets(snippets.filter((candidate) => candidate.id !== snippet.id));
    }

    setSnippetAction(null);
  };

  return (
    <section className="text-transfer-mode">
      <header className="tool-page-header">
        <div>
          <h1>Send text to PC</h1>
          <p>{target.mode === "focused" ? "Text will be sent to the currently focused application." : `Text will be sent to ${target.displayName}.`}</p>
        </div>
      </header>

      {target.mode === "focused" ? (
        <p className="text-transfer-warning">Changing focus on the PC before sending changes the destination.</p>
      ) : (
        <p className="text-transfer-warning">Text is sent to the active field, document, cell, or insertion point in the configured application.</p>
      )}

      <label className="text-transfer-editor">
        <span>Text to send</span>
        <textarea
          autoCapitalize="sentences"
          maxLength={maxSnippetLength}
          placeholder="Type or paste text here…"
          value={props.draft}
          onChange={(event) => props.onDraftChange(event.target.value)}
        />
      </label>

      <div className="text-transfer-actions">
        <button type="button" disabled={!canSend} onClick={() => send(false)}>
          <Send aria-hidden="true" />
          <span>{props.pending ? "Waiting for PC…" : "Send text"}</span>
        </button>
        <button type="button" disabled={!canSend} onClick={() => send(true)}>
          <span>{props.pending ? "Waiting for PC…" : "Send text + Enter"}</span>
        </button>
      </div>

      <label className="toggle-row text-transfer-clear-setting">
        <span>Clear after sending</span>
        <input type="checkbox" checked={props.clearAfterSending} onChange={(event) => props.onClearAfterSendingChange(event.target.checked)} />
      </label>

      {!props.supported && <p className="text-transfer-feedback error" role="alert">Update the Windows host to use acknowledged text transfer.</p>}
      {props.pending && <p className="text-transfer-feedback pending" role="status">Waiting for the PC.</p>}
      {!props.pending && props.result && <p className={`text-transfer-feedback ${props.result.succeeded ? "success" : "error"}`} role={props.result.succeeded ? "status" : "alert"}>{props.result.message}</p>}

      <section className="saved-snippets" aria-labelledby="saved-snippets-title">
        <div className="saved-snippets-heading">
          <div>
            <h2 id="saved-snippets-title">Saved snippets</h2>
            <p>Stored only on this device until you choose to send one.</p>
          </div>
          <span>{snippets.length}/{maxSavedSnippets}</span>
        </div>
        <div className="snippet-save-row">
          <input value={snippetName} maxLength={60} placeholder="Snippet name" aria-label="Snippet name" onChange={(event) => setSnippetName(event.target.value)} />
          <button type="button" disabled={!snippetName.trim() || !props.draft || snippets.length >= maxSavedSnippets} onClick={addSnippet}>
            <Save aria-hidden="true" />
            <span>Save current text</span>
          </button>
        </div>
        {snippets.length === 0 ? <p className="empty-snippets">No saved snippets.</p> : (
          <ul>
            {snippets.map((snippet) => (
              <li key={snippet.id}>
                <button type="button" className="snippet-load" onClick={() => props.onDraftChange(snippet.text)}>{snippet.name}</button>
                <button type="button" onClick={() => setSnippetAction({ kind: "rename", snippet })}>Rename</button>
                <button type="button" disabled={!props.draft} onClick={() => setSnippetAction({ kind: "update", snippet })}>Update</button>
                <button type="button" className="snippet-delete" aria-label={`Delete ${snippet.name}`} onClick={() => setSnippetAction({ kind: "delete", snippet })}><Trash2 aria-hidden="true" /></button>
              </li>
            ))}
          </ul>
        )}
      </section>

      {snippetAction && (
        <SnippetActionDialog action={snippetAction} onCancel={() => setSnippetAction(null)} onConfirm={completeSnippetAction} />
      )}
    </section>
  );
}

function SnippetActionDialog({ action, onCancel, onConfirm }: { action: SnippetAction; onCancel: () => void; onConfirm: (name?: string) => void }) {
  const dialogRef = useRef<HTMLDialogElement>(null);
  const [name, setName] = useState(action.snippet.name);
  const normalizedName = normalizeSnippetName(name);
  const title = action.kind === "rename" ? "Rename snippet" : action.kind === "update" ? "Update snippet" : "Delete snippet";

  useEffect(() => {
    const dialog = dialogRef.current;
    if (!dialog || dialog.open) {
      return;
    }

    if (typeof dialog.showModal === "function") {
      dialog.showModal();
    } else {
      dialog.setAttribute("open", "");
    }
  }, []);

  return (
    <dialog
      className="snippet-action-dialog"
      ref={dialogRef}
      aria-labelledby="snippet-action-title"
      onCancel={(event) => {
        event.preventDefault();
        onCancel();
      }}
    >
      <form method="dialog" onSubmit={(event) => {
        event.preventDefault();
        onConfirm(action.kind === "rename" ? name : undefined);
      }}>
        <header>
          <h2 id="snippet-action-title">{title}</h2>
          <p>{action.kind === "rename"
            ? "Choose a clear name for this saved text."
            : action.kind === "update"
              ? <>Replace <strong>{action.snippet.name}</strong> with the text currently in the editor?</>
              : <>Permanently delete <strong>{action.snippet.name}</strong> from this device?</>}</p>
        </header>

        {action.kind === "rename" && (
          <label>
            <span>Snippet name</span>
            <input autoFocus maxLength={60} value={name} onChange={(event) => setName(event.target.value)} />
          </label>
        )}

        <div className="snippet-dialog-actions">
          <button type="button" onClick={onCancel}>Cancel</button>
          <button
            type="submit"
            className={action.kind === "delete" ? "snippet-dialog-danger" : "snippet-dialog-primary"}
            disabled={action.kind === "rename" && !normalizedName}
          >
            {title}
          </button>
        </div>
      </form>
    </dialog>
  );
}
