import { ChevronDown, GripVertical, Save, Trash2 } from "lucide-react";
import { useEffect, useState } from "react";
import { createLocalId } from "../../localId";
import {
  loadTextSnippets,
  maxSavedSnippets,
  maxSnippetLength,
  normalizeSnippetName,
  saveTextSnippets,
  type SavedTextSnippet
} from "../../textSnippets";
import { SnippetActionDialog, type SnippetAction } from "./SnippetActionDialog";
import { useSnippetReorder } from "./useSnippetReorder";

type SavedTextSnippetsProps = {
  clientId: string;
  draft: string;
  initiallyOpen?: boolean;
  onLoadSnippet: (snippet: SavedTextSnippet) => void;
};

function snippetNamesMatch(first: string, second: string): boolean {
  return normalizeSnippetName(first).toLocaleLowerCase() === normalizeSnippetName(second).toLocaleLowerCase();
}

export function SavedTextSnippets({ clientId, draft, initiallyOpen, onLoadSnippet }: SavedTextSnippetsProps) {
  const [snippets, setSnippets] = useState(() => loadTextSnippets(clientId));
  const [snippetName, setSnippetName] = useState("");
  const [snippetAction, setSnippetAction] = useState<SnippetAction | null>(null);
  const normalizedSnippetName = normalizeSnippetName(snippetName);
  const snippetNameTaken = normalizedSnippetName.length > 0 && snippets.some((snippet) => snippetNamesMatch(snippet.name, normalizedSnippetName));
  const {
    draggingSnippetId,
    finishSnippetDrag,
    moveSnippet,
    snippetDragOffsetY,
    snippetReorderFeedback,
    startSnippetLongPress,
    suppressSnippetClick
  } = useSnippetReorder({ clientId, setSnippets, snippets });

  useEffect(() => {
    setSnippets(loadTextSnippets(clientId));
  }, [clientId]);

  const updateSnippets = (next: SavedTextSnippet[]) => {
    setSnippets(next);
    saveTextSnippets(clientId, next);
  };

  const addSnippet = () => {
    const name = normalizeSnippetName(snippetName);
    if (!name || snippetNameTaken || !draft || snippets.length >= maxSavedSnippets) {
      return;
    }

    updateSnippets([...snippets, { id: createLocalId(), name, text: draft.slice(0, maxSnippetLength) }]);
    setSnippetName("");
  };

  const completeSnippetAction = (name?: string) => {
    if (!snippetAction) {
      return;
    }

    const { kind, snippet } = snippetAction;
    if (kind === "rename") {
      const normalizedName = normalizeSnippetName(name ?? "");
      if (!normalizedName || snippets.some((candidate) => candidate.id !== snippet.id && snippetNamesMatch(candidate.name, normalizedName))) {
        return;
      }
      updateSnippets(snippets.map((candidate) => candidate.id === snippet.id ? { ...candidate, name: normalizedName } : candidate));
    } else if (kind === "update" && draft) {
      updateSnippets(snippets.map((candidate) => candidate.id === snippet.id ? { ...candidate, text: draft.slice(0, maxSnippetLength) } : candidate));
    } else if (kind === "delete") {
      updateSnippets(snippets.filter((candidate) => candidate.id !== snippet.id));
    }

    setSnippetAction(null);
  };

  return (
    <>
      <details className="saved-snippets" open={initiallyOpen || undefined}>
        <summary className="saved-snippets-heading">
          <div>
            <h2 id="saved-snippets-title">Saved snippets</h2>
            <p>Stored only on this device until you choose to send one.</p>
          </div>
          <span className="saved-snippets-meta">
            <span>{snippets.length}/{maxSavedSnippets}</span>
            <ChevronDown aria-hidden="true" />
          </span>
        </summary>
        <div className="saved-snippets-content">
          <div className="snippet-save-row">
            <div className="snippet-name-field">
              <input
                value={snippetName}
                maxLength={60}
                placeholder="Snippet name"
                aria-label="Snippet name"
                aria-invalid={snippetNameTaken}
                aria-describedby={snippetNameTaken ? "new-snippet-name-error" : undefined}
                onChange={(event) => setSnippetName(event.target.value)}
              />
              {snippetNameTaken && <span id="new-snippet-name-error" className="snippet-name-error" role="alert">A snippet with this name already exists.</span>}
            </div>
            <button type="button" disabled={!normalizedSnippetName || snippetNameTaken || !draft || snippets.length >= maxSavedSnippets} onClick={addSnippet}>
              <Save aria-hidden="true" />
              <span>Save current text</span>
            </button>
          </div>
          {snippets.length === 0 ? <p className="empty-snippets">No saved snippets.</p> : (
            <>
              <span id="snippet-reorder-instructions" className="visually-hidden">Long-press a snippet card, drag it up or down, and release to save its new position.</span>
              <ul aria-describedby="snippet-reorder-instructions">
                {snippets.map((snippet) => (
                  <li
                    key={snippet.id}
                    className={draggingSnippetId === snippet.id ? "snippet-dragging" : undefined}
                    data-snippet-id={snippet.id}
                    style={draggingSnippetId === snippet.id ? { transform: `translateY(${snippetDragOffsetY}px) scale(1.015)` } : undefined}
                    onClickCapture={suppressSnippetClick}
                    onContextMenu={(event) => event.preventDefault()}
                    onTouchCancel={finishSnippetDrag}
                    onTouchEnd={finishSnippetDrag}
                    onTouchMove={moveSnippet}
                    onTouchStart={(event) => startSnippetLongPress(event, snippet)}
                  >
                    <button
                      type="button"
                      className={`snippet-load${snippet.text === draft ? " draft-match" : ""}`}
                      onClick={() => onLoadSnippet(snippet)}
                    >
                      <span className="snippet-load-label">{snippet.name}</span>
                      <span className="snippet-drag-hint" aria-hidden="true"><GripVertical /></span>
                    </button>
                    <button type="button" onClick={() => setSnippetAction({ kind: "rename", snippet })}>Rename</button>
                    <button type="button" disabled={!draft} onClick={() => setSnippetAction({ kind: "update", snippet })}>Update</button>
                    <button type="button" className="snippet-delete" aria-label={`Delete ${snippet.name}`} onClick={() => setSnippetAction({ kind: "delete", snippet })}><Trash2 aria-hidden="true" /></button>
                  </li>
                ))}
              </ul>
            </>
          )}
          {snippetReorderFeedback && <span className="visually-hidden" role="status">{snippetReorderFeedback}</span>}
        </div>
      </details>

      {snippetAction && (
        <SnippetActionDialog
          action={snippetAction}
          nameTaken={(name) => snippets.some((candidate) => candidate.id !== snippetAction.snippet.id && snippetNamesMatch(candidate.name, name))}
          onCancel={() => setSnippetAction(null)}
          onConfirm={completeSnippetAction}
        />
      )}
    </>
  );
}
