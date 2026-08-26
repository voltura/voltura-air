import { ClipboardPaste, Copy, Scissors } from "lucide-react";
import { useEffect, useRef, useState } from "react";
import "./clipboard-read.css";
import {
  canCopyTextToClipboard,
  copyTextToClipboard,
} from "../../../foundation/diagnostics/mobileDiagnostics";
import {
  canWriteDeferredTextToDeviceClipboard,
  writeDeferredTextToDeviceClipboard,
} from "../../../foundation/platform/deviceClipboard";
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
  onCancelGetTextForDevice: () => void;
  onCopyFeedback: (feedback: AppToastMessage) => void;
  onGetText: () => void;
  onGetTextForDevice: () => Promise<ClipboardGetResultMessage> | null;
  onLoadSnippet: (snippet: SavedTextSnippet) => void;
  onTextChange: (text: string) => void;
}

interface TextSelection {
  end: number;
  sourceText: string;
  start: number;
}

export function ClipboardReadMode({
  clientId,
  permission,
  pending,
  result,
  text,
  onCancelGetTextForDevice,
  onCopyFeedback,
  onGetText,
  onGetTextForDevice,
  onLoadSnippet,
  onTextChange,
}: ClipboardReadModeProps) {
  const getButtonRef = useRef<HTMLButtonElement>(null);
  const hiddenDeviceClipboardTextRef = useRef<HTMLTextAreaElement>(null);
  const textAreaRef = useRef<HTMLTextAreaElement>(null);
  const [dismissedErrorResult, setDismissedErrorResult] =
    useState<ClipboardGetResultMessage | null>(null);
  const [areSnippetsVisible, setAreSnippetsVisible] = useState(false);
  const [isCopyAvailable] = useState(canCopyTextToClipboard);
  const [isDeviceCopyAvailable] = useState(canWriteDeferredTextToDeviceClipboard);
  const deviceCopyGenerationRef = useRef(0);
  const mountedRef = useRef(true);
  const [textSelection, setTextSelection] = useState<TextSelection | null>(null);
  const isAllowed = permission === true;
  const hasTextSelection =
    textSelection?.sourceText === text && textSelection.end > textSelection.start;

  const isErrorDialogOpen = result !== null && !result.succeeded && result !== dismissedErrorResult;

  useEffect(() => {
    mountedRef.current = true;
    const hiddenDeviceClipboardText = hiddenDeviceClipboardTextRef.current;
    return () => {
      mountedRef.current = false;
      deviceCopyGenerationRef.current += 1;
      if (hiddenDeviceClipboardText) {
        hiddenDeviceClipboardText.value = "";
      }
      onCancelGetTextForDevice();
    };
  }, [onCancelGetTextForDevice]);

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
      sourceText: text,
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
        tone: "error",
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

  const copyPcClipboardToDevice = () => {
    const pcClipboardResult = onGetTextForDevice();
    if (!pcClipboardResult) {
      onCopyFeedback({
        message: "Could not request PC clipboard text. Reconnect and try again.",
        tone: "error",
      });
      return;
    }

    const generation = ++deviceCopyGenerationRef.current;
    if (hiddenDeviceClipboardTextRef.current) {
      hiddenDeviceClipboardTextRef.current.value = "";
    }
    const clipboardItemText = pcClipboardResult.then((pcResult) => {
      if (deviceCopyGenerationRef.current !== generation) {
        throw new Error("A newer device clipboard request replaced this request.");
      }

      if (!pcResult.succeeded || typeof pcResult.text !== "string") {
        throw new Error("PC clipboard text was unavailable.");
      }

      const hiddenText = hiddenDeviceClipboardTextRef.current;
      if (!hiddenText) {
        throw new Error("The device clipboard text boundary is unavailable.");
      }

      hiddenText.value = pcResult.text;
      return new Blob([hiddenText.value], { type: "text/plain" });
    });
    void clipboardItemText.catch(() => undefined);
    const deviceWrite = writeDeferredTextToDeviceClipboard(clipboardItemText);
    void Promise.all([deviceWrite, pcClipboardResult]).then(([copyResult, pcResult]) => {
      if (!mountedRef.current || deviceCopyGenerationRef.current !== generation) {
        return;
      }

      if (hiddenDeviceClipboardTextRef.current) {
        hiddenDeviceClipboardTextRef.current.value = "";
      }

      if (!pcResult.succeeded || typeof pcResult.text !== "string") {
        onCopyFeedback({
          message: `Could not get PC clipboard text. ${pcResult.message}`,
          tone: "error",
        });
      } else if (copyResult.status === "copied") {
        onCopyFeedback({
          message: "PC clipboard text is now in this device's clipboard.",
          tone: "success",
        });
      } else {
        onCopyFeedback({
          message:
            copyResult.status === "denied"
              ? "This device did not allow clipboard writing. Try again or use the visible text box."
              : "Could not write PC clipboard text to this device's clipboard. Try again or use the visible text box.",
          tone: "error",
        });
      }
    });
  };

  return (
    <section className={`clipboard-read-mode${areSnippetsVisible ? " snippets-visible" : ""}`}>
      <div className="clipboard-read-main">
        <header className="tool-page-header">
          <div>
            <div className="clipboard-read-title-row">
              <h1>Get text from PC</h1>
              <InfoButton
                description={
                  isDeviceCopyAvailable
                    ? "Get PC clipboard text into the visible box, or explicitly get fresh PC clipboard text directly into this device's clipboard."
                    : "Press the button to fetch the PC's current clipboard text. Voltura Air does not write to this device's clipboard."
                }
                size="detailed"
                title="Get text from PC"
              />
            </div>
            <p>Fetch text from the PC clipboard into this page.</p>
          </div>
        </header>

        {!isAllowed && (
          <p className="clipboard-read-guidance error" role="alert">
            Clipboard access is blocked by the host. Enable the permission in the host settings or
            this device's details.
          </p>
        )}

        <div className="clipboard-read-actions">
          <button
            ref={getButtonRef}
            type="button"
            className="clipboard-read-button"
            disabled={!isAllowed || pending}
            onClick={onGetText}
          >
            <ClipboardPaste aria-hidden="true" />
            <span>
              {pending ? "Getting PC clipboard text…" : "Get PC clipboard text into this box"}
            </span>
          </button>
          {isDeviceCopyAvailable && (
            <button
              type="button"
              className="clipboard-read-button"
              disabled={!isAllowed}
              onClick={copyPcClipboardToDevice}
            >
              <Copy aria-hidden="true" />
              <span>Get PC clipboard text into this device's clipboard</span>
            </button>
          )}
          <button
            type="button"
            className="clipboard-read-button"
            aria-pressed={areSnippetsVisible}
            onClick={() => {
              setAreSnippetsVisible((visible) => !visible);
            }}
          >
            <span>{areSnippetsVisible ? "Hide snippets" : "Show snippets"}</span>
          </button>
        </div>

        <div className="clipboard-read-text">
          <div className="clipboard-read-text-header">
            <label htmlFor="clipboard-read-textarea">Text from PC</label>
            <div className="clipboard-read-text-actions">
              <button type="button" disabled={!text} onClick={clearAllText}>
                Clear All
              </button>
              <button type="button" disabled={!text} onClick={selectAllText}>
                Select All
              </button>
              <button type="button" disabled={!hasTextSelection} onClick={cutSelectedText}>
                <Scissors aria-hidden="true" />
                <span>Cut</span>
              </button>
              {isCopyAvailable && (
                <button
                  type="button"
                  disabled={!hasTextSelection}
                  onClick={() => {
                    void copySelectedText();
                  }}
                >
                  <Copy aria-hidden="true" />
                  <span>Copy selected text</span>
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

      <textarea
        ref={hiddenDeviceClipboardTextRef}
        className="visually-hidden"
        aria-hidden="true"
        tabIndex={-1}
        readOnly
        defaultValue=""
      />

      {areSnippetsVisible && (
        <SavedTextSnippets
          key={clientId}
          clientId={clientId}
          draft={text}
          initiallyOpen
          onLoadSnippet={onLoadSnippet}
        />
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
