import { useRef, useState } from "react";
import { normalizeSnippetName, type SavedTextSnippet } from "../../../foundation/settings/textSnippets";
import { ModalDialog } from "../../../ui/overlays/ModalDialog";

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
  const nameInputRef = useRef<HTMLInputElement>(null);
  const [name, setName] = useState(action.snippet.name);
  const normalizedName = normalizeSnippetName(name);
  const isNameTaken = action.kind === "rename" && normalizedName.length > 0 && nameTaken(normalizedName);
  const title = action.kind === "rename" ? "Rename snippet" : action.kind === "update" ? "Update snippet" : "Delete snippet";

  return (
    <ModalDialog
      actions={(
        <>
          <button type="button" onClick={onCancel}>Cancel</button>
          <button
            type="submit"
            className={action.kind === "delete" ? "snippet-dialog-danger" : "snippet-dialog-primary"}
            disabled={action.kind === "rename" && (!normalizedName || isNameTaken)}
          >
            {title}
          </button>
        </>
      )}
      actionsClassName="snippet-dialog-actions"
      className="snippet-action-dialog"
      dismissLabel="Cancel"
      focusDismissAction={action.kind !== "rename"}
      formClassName="snippet-action-dialog-form"
      initialFocusRef={action.kind === "rename" ? nameInputRef : undefined}
      isOpen
      onClose={onCancel}
      onSubmit={(event) => {
        event.preventDefault();
        onConfirm(action.kind === "rename" ? name : undefined);
        return false;
      }}
      title={title}
    >
      <p>{action.kind === "rename"
        ? "Choose a clear name for this saved text."
        : action.kind === "update"
          ? <>Replace <strong>{action.snippet.name}</strong> with the text currently in the editor?</>
          : <>Permanently delete <strong>{action.snippet.name}</strong> from this device?</>}</p>

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
    </ModalDialog>
  );
}
