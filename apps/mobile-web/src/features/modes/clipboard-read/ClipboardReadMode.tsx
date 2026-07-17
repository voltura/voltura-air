import { ClipboardPaste } from "lucide-react";
import { useRef, useState } from "react";
import type { ClipboardGetResultMessage } from "../../../protocol";
import type { SavedTextSnippet } from "../../../textSnippets";
import { InfoDialog } from "../../../ui/overlays/InfoDialog";
import { SavedTextSnippets } from "../text-transfer/SavedTextSnippets";

interface ClipboardReadModeProps {
  clientId: string;
  permission: boolean | undefined;
  pending: boolean;
  result: ClipboardGetResultMessage | null;
  text: string;
  onGetText: () => void;
  onLoadSnippet: (snippet: SavedTextSnippet) => void;
}

export function ClipboardReadMode({ clientId, permission, pending, result, text, onGetText, onLoadSnippet }: ClipboardReadModeProps) {
  const getButtonRef = useRef<HTMLButtonElement>(null);
  const [dismissedErrorResult, setDismissedErrorResult] = useState<ClipboardGetResultMessage | null>(null);
  const [areSnippetsVisible, setAreSnippetsVisible] = useState(false);
  const isAllowed = permission === true;

  const isErrorDialogOpen = result !== null && !result.succeeded && result !== dismissedErrorResult;

  const closeErrorDialog = () => {
    setDismissedErrorResult(result);
    window.setTimeout(() => getButtonRef.current?.focus(), 0);
  };

  return (
    <section className={`clipboard-read-mode${areSnippetsVisible ? " snippets-visible" : ""}`}>
      <div className="clipboard-read-main">
        <header className="tool-page-header">
          <div>
            <h1>Get text from PC</h1>
            <p>Fetch text from the PC clipboard into this page.</p>
          </div>
        </header>

        <p className={`clipboard-read-guidance${isAllowed ? "" : " error"}`} role={isAllowed ? undefined : "alert"}>
          {isAllowed
            ? "Press the button to fetch the PC's current clipboard text. Voltura Air does not write to this device's clipboard."
            : "Clipboard access is blocked by the host. Enable the permission in the host settings or this device's details."}
        </p>

        <div className="clipboard-read-actions">
          <button ref={getButtonRef} type="button" className="clipboard-read-button" disabled={!isAllowed || pending} onClick={onGetText}>
            <ClipboardPaste aria-hidden="true" />
            <span>{pending ? "Getting text from PC…" : "Get text from PC"}</span>
          </button>
          <button
            type="button"
            className="clipboard-read-button"
            aria-pressed={areSnippetsVisible}
            onClick={() => { setAreSnippetsVisible((visible) => !visible); }}
          >
            <span>{areSnippetsVisible ? "Hide snippets" : "Show snippets"}</span>
          </button>
        </div>

        <label className="clipboard-read-text" htmlFor="clipboard-read-textarea">
          <span>Text from PC</span>
          <textarea
            id="clipboard-read-textarea"
            aria-label="Text from PC"
            readOnly
            value={text}
            placeholder="Fetched text appears here. Select it to copy manually."
          />
        </label>
      </div>

      {areSnippetsVisible && (
        <SavedTextSnippets key={clientId} clientId={clientId} draft={text} initiallyOpen onLoadSnippet={onLoadSnippet} />
      )}

      {result && !result.succeeded && (
        <InfoDialog
          title="Could not get text from PC"
          description={result.message}
          isOpen={isErrorDialogOpen}
          onClose={closeErrorDialog}
        />
      )}
    </section>
  );
}
