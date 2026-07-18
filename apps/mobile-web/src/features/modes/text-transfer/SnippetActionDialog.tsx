import { useEffect, useRef, useState } from "react";
import { normalizeSnippetName, type SavedTextSnippet } from "../../../foundation/settings/textSnippets";

export interface SnippetAction {
  kind: "rename" | "update" | "delete";
  snippet: SavedTextSnippet;
}

interface SnippetActionDialogProps {
  action: SnippetAction;
  nameTaken: (name: string) => boolean;
  onCancel: () => void;
  onConfirm: (name?: string) => void;
}

export function SnippetActionDialog({ action, nameTaken, onCancel, onConfirm }: SnippetActionDialogProps) {
  const dialogRef = useRef<HTMLDialogElement>(null);
  const nameInputRef = useRef<HTMLInputElement>(null);
  const [name, setName] = useState(action.snippet.name);
  const normalizedName = normalizeSnippetName(name);
  const isNameTaken = action.kind === "rename" && normalizedName.length > 0 && nameTaken(normalizedName);
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
    nameInputRef.current?.focus();
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
            <input
              ref={nameInputRef}
              maxLength={60}
              value={name}
              aria-invalid={isNameTaken}
              aria-describedby={isNameTaken ? "rename-snippet-name-error" : undefined}
              onChange={(event) => { setName(event.target.value); }}
            />
            {isNameTaken && <span id="rename-snippet-name-error" className="snippet-name-error" role="alert">A snippet with this name already exists.</span>}
          </label>
        )}

        <div className="snippet-dialog-actions">
          <button type="button" onClick={onCancel}>Cancel</button>
          <button
            type="submit"
            className={action.kind === "delete" ? "snippet-dialog-danger" : "snippet-dialog-primary"}
            disabled={action.kind === "rename" && (!normalizedName || isNameTaken)}
          >
            {title}
          </button>
        </div>
      </form>
    </dialog>
  );
}
