import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, it, vi, expect } from "vitest";
import { copyTextToClipboard } from "../../../foundation/diagnostics/mobileDiagnostics";
import type { ClipboardGetResultMessage } from "../../../foundation/protocol/messages";
import { ClipboardReadMode } from "./ClipboardReadMode";
vi.mock("../../../foundation/diagnostics/mobileDiagnostics", () => ({
  canCopyTextToClipboard: () => true,
  copyTextToClipboard: vi.fn(),
}));
vi.mock("../../../foundation/platform/deviceClipboard", () => ({
  canWriteDeferredTextToDeviceClipboard: () => true,
  writeDeferredTextToDeviceClipboard: (value: Promise<Blob>) =>
    value.then(
      () => ({ status: "copied" }),
      () => ({ status: "failed" }),
    ),
}));
const successfulDeviceRead = (): Promise<ClipboardGetResultMessage> =>
  Promise.resolve({
    type: "clipboard.get.result",
    operationId: "device-copy",
    succeeded: true,
    message: "Read",
    text: "Fresh PC text",
  });
beforeEach(() => vi.clearAllMocks());
it.each(["unmount", "selection", "text"])(
  "does not restore stale selected-copy UI after %s",
  async (reason) => {
    let complete!: (value: "copied") => void;
    vi.mocked(copyTextToClipboard).mockReturnValue(
      new Promise((resolve) => {
        complete = resolve;
      }),
    );
    const feedback = vi.fn();
    const props = {
      clientId: "client",
      permission: true,
      pending: false,
      result: null,
      text: "Original text",
      onCancelGetTextForDevice: vi.fn(),
      onCopyFeedback: feedback,
      onGetText: vi.fn(),
      onGetTextForDevice: successfulDeviceRead,
      onLoadSnippet: vi.fn(),
      onTextChange: vi.fn(),
    };
    const view = render(<ClipboardReadMode {...props} />);
    const input = screen.getByLabelText<HTMLTextAreaElement>("Text from PC");
    input.setSelectionRange(0, 8);
    fireEvent.select(input);
    fireEvent.click(screen.getByRole("button", { name: "Copy selected text" }));
    if (reason === "unmount") {
      view.unmount();
    } else if (reason === "text") {
      view.rerender(<ClipboardReadMode {...props} text="New" />);
    } else {
      input.setSelectionRange(9, 13);
      fireEvent.select(input);
    }
    await act(async () => {
      await Promise.resolve(complete("copied"));
    });
    expect(feedback).not.toHaveBeenCalled();
    if (reason === "selection") {
      expect(input.selectionStart).toBe(9);
    }
  },
);

it("keeps a pending direct device copy through a callback identity change", async () => {
  let complete!: (value: ClipboardGetResultMessage) => void;
  const pending = new Promise<ClipboardGetResultMessage>((resolve) => {
    complete = resolve;
  });
  const cancel = vi.fn();
  const replacementCancel = vi.fn();
  const feedback = vi.fn();
  const props = {
    clientId: "client",
    permission: true,
    pending: false,
    result: null,
    text: "",
    onCancelGetTextForDevice: cancel,
    onCopyFeedback: feedback,
    onGetText: vi.fn(),
    onGetTextForDevice: () => pending,
    onLoadSnippet: vi.fn(),
    onTextChange: vi.fn(),
  };
  const view = render(<ClipboardReadMode {...props} />);
  fireEvent.click(
    screen.getByRole("button", { name: "Get PC clipboard text into this device's clipboard" }),
  );
  view.rerender(<ClipboardReadMode {...props} onCancelGetTextForDevice={replacementCancel} />);
  expect(cancel).not.toHaveBeenCalled();
  await act(async () => {
    await Promise.resolve(
      complete({
        type: "clipboard.get.result",
        operationId: "pending",
        succeeded: true,
        message: "Ready",
        text: "Text",
      }),
    );
  });
  await waitFor(() => expect(feedback).toHaveBeenCalled());
  view.unmount();
  expect(replacementCancel).toHaveBeenCalledOnce();
});
