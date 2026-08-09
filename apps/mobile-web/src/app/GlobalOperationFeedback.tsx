import type { AppTab } from "./appModeTabs";
import type { AppLaunchResultMessage, ClipboardGetResultMessage, PowerPointRefreshResultMessage, PresentationCommandResultMessage, PresentationSessionResultMessage, TextSendResultMessage } from "../foundation/protocol/messages";
import { AppToast } from "../ui/feedback/AppToast";
import type { AppToastMessage } from "../ui/feedback/AppToast";

interface GlobalOperationFeedbackProps {
  appLaunchResult: AppLaunchResultMessage | null;
  clipboardReadResult: ClipboardGetResultMessage | null;
  pendingAppLaunchId: string | null;
  pendingClipboardRead: boolean;
  pendingTextTransfer: boolean;
  powerPointRefreshResult: PowerPointRefreshResultMessage | null;
  presentationResult: PresentationCommandResultMessage | null;
  presentationSessionResult: PresentationSessionResultMessage | null;
  tab: AppTab;
  textTransferResult: TextSendResultMessage | null;
  transientFeedback: AppToastMessage | null;
  onDismissTransient?: () => void;
}

export function GlobalOperationFeedback({
  appLaunchResult,
  clipboardReadResult,
  pendingAppLaunchId,
  pendingClipboardRead,
  pendingTextTransfer,
  powerPointRefreshResult,
  presentationResult,
  presentationSessionResult,
  tab,
  textTransferResult,
  transientFeedback,
  onDismissTransient
}: GlobalOperationFeedbackProps) {
  let feedback = transientFeedback;

  if (!feedback && pendingClipboardRead) {
    feedback = { message: "Getting text from PC…", tone: "pending" };
  } else if (!feedback && clipboardReadResult?.succeeded) {
    feedback = { message: clipboardReadResult.message, tone: "success" };
  } else if (!feedback && tab !== "text-transfer" && pendingTextTransfer) {
    feedback = { message: "Waiting for the PC to send text…", tone: "pending" };
  } else if (!feedback && tab !== "text-transfer" && textTransferResult) {
    feedback = { message: textTransferResult.message, tone: textTransferResult.succeeded ? "success" : "error" };
  } else if (!feedback && pendingAppLaunchId !== null) {
    feedback = { message: "Waiting for the PC to respond…", tone: "pending" };
  } else if (!feedback && appLaunchResult) {
    feedback = { message: appLaunchResult.message, tone: appLaunchResult.succeeded ? "success" : "error" };
  } else if (!feedback && presentationResult?.succeeded === false) {
    feedback = {
      message: `${presentationActionLabel(presentationResult.action)} failed. ${presentationResult.message}`,
      tone: "error"
    };
  } else if (!feedback && presentationSessionResult?.succeeded === false) {
    feedback = {
      message: `${presentationSessionActionLabel(presentationSessionResult)} failed. ${presentationSessionResult.message}`,
      tone: "error"
    };
  } else if (!feedback && powerPointRefreshResult?.succeeded === false) {
    feedback = {
      message: `Refresh PowerPoint failed. ${powerPointRefreshResult.message}`,
      tone: "error"
    };
  }

  return feedback ? <AppToast tone={feedback.tone} {...(transientFeedback && onDismissTransient ? { onDismiss: onDismissTransient } : {})}>{feedback.message}</AppToast> : null;
}

function presentationSessionActionLabel(result: PresentationSessionResultMessage): string {
  switch (result.action) {
    case "start": return "Start tracking";
    case "break": return "Change break";
    case "save": return "Save session";
    case "discard": return "Discard session";
  }
}

function presentationActionLabel(action: PresentationCommandResultMessage["action"]): string {
  switch (action) {
    case "activate": return "Bring PowerPoint forward";
    case "start": return "Start from beginning";
    case "start-current": return "Start from current";
    case "next": return "Next";
    case "previous": return "Previous";
    case "first": return "First";
    case "last": return "Last";
    case "goto": return "Go to slide";
    case "black": return "Black screen";
    case "white": return "White screen";
    case "pause": return "Pause auto-play";
    case "pointer": return "Laser pointer";
    case "end": return "End slideshow";
  }
}
