import { ClipboardPaste, Copy, Scissors } from "lucide-react";
import { useRef, useState } from "react";
import "./clipboard-read.css";
import { canCopyTextToClipboard, copyTextToClipboard } from "../../../foundation/diagnostics/mobileDiagnostics";
import type { ClipboardGetResultMessage } from "../../../foundation/protocol/messages";
import type { SavedTextSnippet } from "../../../foundation/settings/textSnippets";
import type { AppToastMessage } from "../../../ui/feedback/AppToast";
import { InfoButton } from "../../../ui/overlays/InfoButton";
import { InfoDialog } from "../../../ui/overlays/InfoDialog";
import { SavedTextSnippets } from "../text-transfer/SavedTextSnippets";

interface ClipboardReadModeProps {
  clientId: string;
  permission: boolean | undefined;
  pending: boolean;
  result: ClipboardGetResultMessage | null;
  text: string;
  onCopyFeedback: (feedback: AppToastMessage) => void;
  onGetText: () => void;
  onLoadSnippet: (snippet: SavedTextSnippet) => void;
  onTextChange: (text: string) => void;
}

interface TextSelection {
  end: number;
  sourceText: string;
  start: number;
}

export function ClipboardReadMode({ clientId, permission, pending, result, text, onCopyFeedback, onGetText, onLoadSnippet, onTextChange }: ClipboardReadModeProps) {
  const getButtonRef = useRef<HTMLButtonElement>(null);
  const textAreaRef = useRef<HTMLTextAreaElement>(null);
  const [dismissedErrorResult, setDismissedErrorResult] = useState<ClipboardGetResultMessage | null>(null);
  const [areSnippetsVisible, setAreSnippetsVisible] = useState(false);
  const [isCopyAvailable] = useState(canCopyTextToClipboard);
  const [textSelection, setTextSelection] = useState<TextSelection | null>(null);
  const isAllowed = permission === true;
  const hasTextSelection = textSelection !== null
    && textSelection.sourceText === text
    && textSelection.end > textSelection.start;

  const isErrorDialogOpen = result !== null && !result.succeeded && result !== dismissedErrorResult;

  const closeErrorDialog = () => {
    setDismissedErrorResult(result);
    window.setTimeout(() => getButtonRef.current?.focus(), 0);
  };

  const selectAllText = () => {
    const textArea = textAreaRef.current;
    if (!textArea) {
      return;
    }

    textArea.focus({ preventScroll: true });
    textArea.select();
    setTextSelection({ start: 0, end: text.length, sourceText: text });
  };

  const recordTextSelection = () => {
    const textArea = textAreaRef.current;
    if (!textArea || textArea.selectionStart === textArea.selectionEnd) {
      setTextSelection(null);
      return;
    }

    setTextSelection({
      start: textArea.selectionStart,
      end: textArea.selectionEnd,
      sourceText: text
    });
  };

  const clearAllText = () => {
    setTextSelection(null);
    onTextChange("");
  };

  const cutSelectedText = () => {
    if (textSelection?.sourceText !== text || textSelection.start === textSelection.end) {
      return;
    }

    const selectionStart = textSelection.start;
    onTextChange(text.slice(0, selectionStart) + text.slice(textSelection.end));
    setTextSelection(null);
    window.requestAnimationFrame(() => {
      textAreaRef.current?.focus({ preventScroll: true });
      textAreaRef.current?.setSelectionRange(selectionStart, selectionStart);
    });
  };

  const copySelectedText = async () => {
    if (textSelection?.sourceText !== text || textSelection.start === textSelection.end) {
      return;
    }

    const selectionStart = textSelection.start;
    const selectionEnd = textSelection.end;
    const result = await copyTextToClipboard(text.slice(selectionStart, selectionEnd));
    if (result === "manual") {
      onCopyFeedback({
        message: "Could not copy automatically. Try Copy again or use your browser's copy action.",
        tone: "error"
      });
      return;
    }

    onCopyFeedback({ message: "Selected text copied.", tone: "success" });

    const textArea = textAreaRef.current;
    if (!textArea) {
      return;
    }

    textArea.focus({ preventScroll: true });
    textArea.setSelectionRange(selectionStart, selectionEnd);
  };

  return (
    <section className={`clipboard-read-mode${areSnippetsVisible ? " snippets-visible" : ""}`}>
      <div className="clipboard-read-main">
        <header className="tool-page-header">
          <div>
            <div className="clipboard-read-title-row">
              <h1>Get text from PC</h1>
              <InfoButton
                description={isCopyAvailable
                  ? "Press the button to fetch the PC's current clipboard text. Voltura Air writes to this device's clipboard only when you choose Copy."
                  : "Press the button to fetch the PC's current clipboard text. Voltura Air does not write to this device's clipboard."}
                size="detailed"
                title="Get text from PC"
              />
            </div>
            <p>Fetch text from the PC clipboard into this page.</p>
          </div>
        </header>

        {!isAllowed && (
          <p className="clipboard-read-guidance error" role="alert">
            Clipboard access is blocked by the host. Enable the permission in the host settings or this device's details.
          </p>
        )}

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

        <div className="clipboard-read-text">
          <div className="clipboard-read-text-header">
            <label htmlFor="clipboard-read-textarea">Text from PC</label>
            <div className="clipboard-read-text-actions">
              <button type="button" disabled={!text} onClick={clearAllText}>Clear All</button>
              <button type="button" disabled={!text} onClick={selectAllText}>Select All</button>
              <button type="button" disabled={!hasTextSelection} onClick={cutSelectedText}>
                <Scissors aria-hidden="true" />
                <span>Cut</span>
              </button>
              {isCopyAvailable && (
                <button type="button" disabled={!hasTextSelection} onClick={() => { void copySelectedText(); }}>
                  <Copy aria-hidden="true" />
                  <span>Copy</span>
                </button>
              )}
            </div>
          </div>
          <textarea
            ref={textAreaRef}
            id="clipboard-read-textarea"
            aria-label="Text from PC"
            readOnly
            value={text}
            placeholder="Fetched text appears here. Select text to cut or copy it."
            onSelect={recordTextSelection}
          />
        </div>
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
