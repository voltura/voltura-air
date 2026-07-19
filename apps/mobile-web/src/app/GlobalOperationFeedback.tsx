import type { AppTab } from "./appModeTabs";
import type { AppLaunchResultMessage, ClipboardGetResultMessage, TextSendResultMessage } from "../foundation/protocol/messages";
import { AppToast } from "../ui/feedback/AppToast";
import type { AppToastMessage } from "../ui/feedback/AppToast";

interface GlobalOperationFeedbackProps {
  appLaunchResult: AppLaunchResultMessage | null;
  clipboardReadResult: ClipboardGetResultMessage | null;
  pendingAppLaunchId: string | null;
  pendingClipboardRead: boolean;
  pendingTextTransfer: boolean;
  tab: AppTab;
  textTransferResult: TextSendResultMessage | null;
  transientFeedback: AppToastMessage | null;
}

export function GlobalOperationFeedback({
  appLaunchResult,
  clipboardReadResult,
  pendingAppLaunchId,
  pendingClipboardRead,
  pendingTextTransfer,
  tab,
  textTransferResult,
  transientFeedback
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
  }

  return feedback ? <AppToast tone={feedback.tone}>{feedback.message}</AppToast> : null;
}
