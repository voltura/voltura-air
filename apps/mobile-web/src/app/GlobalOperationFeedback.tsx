import type { AppTab } from "./appModeTabs";
import type { AppLaunchResultMessage, ClipboardGetResultMessage, TextSendResultMessage } from "../foundation/protocol/messages";
import { AppToast } from "../ui/feedback/AppToast";

interface GlobalOperationFeedbackProps {
  appLaunchResult: AppLaunchResultMessage | null;
  clipboardReadResult: ClipboardGetResultMessage | null;
  pendingAppLaunchId: string | null;
  pendingClipboardRead: boolean;
  pendingTextTransfer: boolean;
  tab: AppTab;
  textTransferResult: TextSendResultMessage | null;
}

export function GlobalOperationFeedback({
  appLaunchResult,
  clipboardReadResult,
  pendingAppLaunchId,
  pendingClipboardRead,
  pendingTextTransfer,
  tab,
  textTransferResult
}: GlobalOperationFeedbackProps) {
  return (
    <>
      {pendingAppLaunchId !== null && <AppToast tone="pending">Waiting for the PC to respond…</AppToast>}
      {pendingAppLaunchId === null && appLaunchResult && (
        <AppToast tone={appLaunchResult.succeeded ? "success" : "error"}>{appLaunchResult.message}</AppToast>
      )}
      {tab !== "text-transfer" && pendingTextTransfer && <AppToast tone="pending">Waiting for the PC to send text…</AppToast>}
      {tab !== "text-transfer" && !pendingTextTransfer && textTransferResult && (
        <AppToast tone={textTransferResult.succeeded ? "success" : "error"}>{textTransferResult.message}</AppToast>
      )}
      {pendingClipboardRead && <AppToast tone="pending">Getting text from PC…</AppToast>}
      {!pendingClipboardRead && clipboardReadResult?.succeeded && <AppToast tone="success">{clipboardReadResult.message}</AppToast>}
    </>
  );
}
