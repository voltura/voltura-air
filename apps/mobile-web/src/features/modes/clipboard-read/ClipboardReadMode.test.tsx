import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { StrictMode, useState } from "react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import {
  canCopyTextToClipboard,
  copyTextToClipboard,
} from "../../../foundation/diagnostics/mobileDiagnostics";
import {
  canWriteDeferredTextToDeviceClipboard,
  writeDeferredTextToDeviceClipboard,
} from "../../../foundation/platform/deviceClipboard";
import type { ClipboardGetResultMessage } from "../../../foundation/protocol/messages";
import type { AppToastMessage } from "../../../ui/feedback/AppToast";
import { ClipboardReadMode } from "./ClipboardReadMode";

vi.mock("../../../foundation/diagnostics/mobileDiagnostics", () => ({
  canCopyTextToClipboard: vi.fn(),
  copyTextToClipboard: vi.fn(),
}));

vi.mock("../../../foundation/platform/deviceClipboard", () => ({
  canWriteDeferredTextToDeviceClipboard: vi.fn(),
  writeDeferredTextToDeviceClipboard: vi.fn(),
}));

const successfulDeviceRead = (): Promise<ClipboardGetResultMessage> =>
  Promise.resolve({
    type: "clipboard.get.result",
    operationId: "device-copy",
    succeeded: true,
    message: "Read",
    text: "Fresh PC text",
  });

function ClipboardReadHarness({
  onCancelGetTextForDevice = vi.fn(),
  onCopyFeedback = vi.fn(),
  onGetTextForDevice = successfulDeviceRead,
}: {
  onCancelGetTextForDevice?: () => void;
  onCopyFeedback?: (feedback: AppToastMessage) => void;
  onGetTextForDevice?: () => Promise<ClipboardGetResultMessage> | null;
}) {
  const [text, setText] = useState("Fetched text");
  return (
    <ClipboardReadMode
      clientId="client-a"
      permission
      pending={false}
      result={null}
      text={text}
      onCancelGetTextForDevice={onCancelGetTextForDevice}
      onCopyFeedback={onCopyFeedback}
      onGetText={vi.fn()}
      onGetTextForDevice={onGetTextForDevice}
      onLoadSnippet={vi.fn()}
      onTextChange={setText}
    />
  );
}

describe("ClipboardReadMode", () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(canCopyTextToClipboard).mockReturnValue(true);
    vi.mocked(copyTextToClipboard).mockResolvedValue("copied");
    vi.mocked(canWriteDeferredTextToDeviceClipboard).mockReturnValue(true);
    vi.mocked(writeDeferredTextToDeviceClipboard).mockImplementation((value) =>
      value.then(
        () => ({ status: "copied" as const }),
        () => ({ status: "failed" as const }),
      ),
    );
  });

  it("fetches only when the user presses the button and preserves manual-copy behavior", () => {
    const onGetText = vi.fn();
    render(
      <ClipboardReadMode
        clientId="client-a"
        permission
        pending={false}
        result={null}
        text=""
        onCancelGetTextForDevice={vi.fn()}
        onCopyFeedback={vi.fn()}
        onGetText={onGetText}
        onGetTextForDevice={successfulDeviceRead}
        onLoadSnippet={vi.fn()}
        onTextChange={vi.fn()}
      />,
    );

    expect(onGetText).not.toHaveBeenCalled();
    expect(screen.getByLabelText("Text from PC")).toHaveProperty("readOnly", true);
    fireEvent.click(screen.getByRole("button", { name: "Get PC clipboard text into this box" }));
    expect(onGetText).toHaveBeenCalledOnce();
    expect(screen.getByRole("button", { name: "Show snippets" })).toHaveProperty("disabled", false);
  });

  it("explains when the host has blocked clipboard access", () => {
    render(
      <ClipboardReadMode
        clientId="client-a"
        permission={false}
        pending={false}
        result={null}
        text="Existing text"
        onCancelGetTextForDevice={vi.fn()}
        onCopyFeedback={vi.fn()}
        onGetText={vi.fn()}
        onGetTextForDevice={successfulDeviceRead}
        onLoadSnippet={vi.fn()}
        onTextChange={vi.fn()}
      />,
    );

    expect(screen.getByRole("alert").textContent).toContain("blocked by the host");
    expect(
      screen.getByRole("button", { name: "Get PC clipboard text into this box" }),
    ).toHaveProperty("disabled", true);
    expect(
      screen.getByRole("button", { name: "Get PC clipboard text into this device's clipboard" }),
    ).toHaveProperty("disabled", true);
    expect(screen.getByLabelText("Text from PC")).toHaveProperty("value", "Existing text");
    expect(screen.getByRole("button", { name: "Show snippets" })).toHaveProperty("disabled", false);
  });

  it("shows the existing snippets control when requested", () => {
    render(
      <ClipboardReadMode
        clientId="client-a"
        permission
        pending={false}
        result={null}
        text="Fetched text"
        onCancelGetTextForDevice={vi.fn()}
        onCopyFeedback={vi.fn()}
        onGetText={vi.fn()}
        onGetTextForDevice={successfulDeviceRead}
        onLoadSnippet={vi.fn()}
        onTextChange={vi.fn()}
      />,
    );

    fireEvent.click(screen.getByRole("button", { name: "Show snippets" }));

    expect(screen.getByText("Saved snippets")).toBeTruthy();
    expect(screen.getByText("Saved snippets").closest("details")).toHaveProperty("open", true);
    expect(screen.getByRole("button", { name: "Hide snippets" })).toBeTruthy();
  });

  it("moves the guidance into the standard information dialog", () => {
    render(
      <ClipboardReadMode
        clientId="client-a"
        permission
        pending={false}
        result={null}
        text=""
        onCancelGetTextForDevice={vi.fn()}
        onCopyFeedback={vi.fn()}
        onGetText={vi.fn()}
        onGetTextForDevice={successfulDeviceRead}
        onLoadSnippet={vi.fn()}
        onTextChange={vi.fn()}
      />,
    );

    expect(screen.queryByText(/Press the button to fetch/)).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "About Get text from PC" }));

    expect(screen.getByRole("dialog").textContent).toContain(
      "Get PC clipboard text into the visible box",
    );
    expect(screen.getByRole("button", { name: "OK" })).toBeTruthy();
  });

  it("clears and selects all fetched text", () => {
    render(<ClipboardReadHarness />);
    const textArea = screen.getByLabelText("Text from PC") as HTMLTextAreaElement;

    fireEvent.click(screen.getByRole("button", { name: "Select All" }));
    expect(document.activeElement).toBe(textArea);
    expect(textArea.selectionStart).toBe(0);
    expect(textArea.selectionEnd).toBe(textArea.value.length);

    fireEvent.click(screen.getByRole("button", { name: "Clear All" }));
    expect(textArea.value).toBe("");
    expect(screen.getByRole("button", { name: "Clear All" })).toHaveProperty("disabled", true);
    expect(screen.getByRole("button", { name: "Select All" })).toHaveProperty("disabled", true);
  });

  it("cuts and copies only the selected text", async () => {
    const onCopyFeedback = vi.fn<(feedback: AppToastMessage) => void>();
    render(<ClipboardReadHarness onCopyFeedback={onCopyFeedback} />);
    const textArea = screen.getByLabelText("Text from PC") as HTMLTextAreaElement;

    textArea.setSelectionRange(0, 7);
    fireEvent.select(textArea);
    fireEvent.click(screen.getByRole("button", { name: "Copy selected text" }));

    await waitFor(() => {
      expect(copyTextToClipboard).toHaveBeenCalledWith("Fetched");
    });
    expect(onCopyFeedback).toHaveBeenCalledWith({
      message: "Selected text copied.",
      tone: "success",
    });

    fireEvent.click(screen.getByRole("button", { name: "Cut" }));
    expect(textArea.value).toBe(" text");
  });

  it("does not show Copy when clipboard writing is unavailable", () => {
    vi.mocked(canCopyTextToClipboard).mockReturnValue(false);
    vi.mocked(canWriteDeferredTextToDeviceClipboard).mockReturnValue(false);
    render(<ClipboardReadHarness />);

    expect(screen.queryByRole("button", { name: "Copy selected text" })).toBeNull();
    expect(screen.getByRole("button", { name: "Cut" })).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "About Get text from PC" }));
    expect(screen.getByRole("dialog").textContent).toContain(
      "Voltura Air does not write to this device's clipboard.",
    );
  });

  it("reports a detected clipboard-path failure and keeps Copy available for retry", async () => {
    vi.mocked(copyTextToClipboard).mockResolvedValue("manual");
    const onCopyFeedback = vi.fn();
    render(<ClipboardReadHarness onCopyFeedback={onCopyFeedback} />);
    const textArea = screen.getByLabelText("Text from PC") as HTMLTextAreaElement;

    textArea.setSelectionRange(0, 7);
    fireEvent.select(textArea);
    fireEvent.click(screen.getByRole("button", { name: "Copy selected text" }));

    await waitFor(() => {
      expect(onCopyFeedback).toHaveBeenCalledWith({
        message: "Could not copy automatically. Try Copy again or use your browser's copy action.",
        tone: "error",
      });
    });
    expect(screen.getByRole("button", { name: "Copy selected text" })).toBeTruthy();
  });

  it("starts a fresh PC read and deferred device write from the same activation", async () => {
    let resolvePcRead: ((result: ClipboardGetResultMessage) => void) | undefined;
    const pcRead = new Promise<ClipboardGetResultMessage>((resolve) => {
      resolvePcRead = resolve;
    });
    const onGetTextForDevice = vi.fn(() => pcRead);
    const onCopyFeedback = vi.fn<(feedback: AppToastMessage) => void>();
    let clipboardItemText: Promise<Blob> | undefined;
    vi.mocked(writeDeferredTextToDeviceClipboard).mockImplementation((value) => {
      clipboardItemText = value;
      return value.then(
        () => ({ status: "copied" as const }),
        () => ({ status: "failed" as const }),
      );
    });
    render(
      <ClipboardReadHarness
        onCopyFeedback={onCopyFeedback}
        onGetTextForDevice={onGetTextForDevice}
      />,
    );

    const copyButton = screen.getByRole("button", {
      name: "Get PC clipboard text into this device's clipboard",
    });
    fireEvent.click(copyButton);
    expect(onGetTextForDevice).toHaveBeenCalledOnce();
    expect(writeDeferredTextToDeviceClipboard).toHaveBeenCalledOnce();
    expect(copyButton).toHaveProperty("disabled", false);

    resolvePcRead?.({
      type: "clipboard.get.result",
      operationId: "device-copy",
      succeeded: true,
      message: "Read",
      text: "Newest PC text",
    });
    await expect(clipboardItemText).resolves.toBeInstanceOf(Blob);
    expect(await (await clipboardItemText!).text()).toBe("Newest PC text");
    await waitFor(() =>
      expect(onCopyFeedback).toHaveBeenCalledWith({
        message: "PC clipboard text is now in this device's clipboard.",
        tone: "success",
      }),
    );
    expect(screen.getByLabelText("Text from PC")).toHaveProperty("value", "Fetched text");
    expect(document.querySelector<HTMLTextAreaElement>("textarea[aria-hidden='true']")!.value).toBe(
      "",
    );
    expect(copyButton).toHaveProperty("disabled", false);
  });

  it("allows every activation and only reports the newest device copy", async () => {
    let resolveFirst: ((result: ClipboardGetResultMessage) => void) | undefined;
    let resolveSecond: ((result: ClipboardGetResultMessage) => void) | undefined;
    const reads = [
      new Promise<ClipboardGetResultMessage>((resolve) => {
        resolveFirst = resolve;
      }),
      new Promise<ClipboardGetResultMessage>((resolve) => {
        resolveSecond = resolve;
      }),
    ];
    const onGetTextForDevice = vi.fn(() => reads.shift() ?? null);
    const onCopyFeedback = vi.fn<(feedback: AppToastMessage) => void>();
    render(
      <ClipboardReadHarness
        onCopyFeedback={onCopyFeedback}
        onGetTextForDevice={onGetTextForDevice}
      />,
    );
    const copyButton = screen.getByRole("button", {
      name: "Get PC clipboard text into this device's clipboard",
    });

    fireEvent.click(copyButton);
    fireEvent.click(copyButton);
    expect(onGetTextForDevice).toHaveBeenCalledTimes(2);
    expect(writeDeferredTextToDeviceClipboard).toHaveBeenCalledTimes(2);
    expect(copyButton).toHaveProperty("disabled", false);

    resolveFirst?.({
      type: "clipboard.get.result",
      operationId: "old",
      succeeded: true,
      message: "Old",
      text: "Old text",
    });
    resolveSecond?.({
      type: "clipboard.get.result",
      operationId: "new",
      succeeded: true,
      message: "New",
      text: "New text",
    });
    await waitFor(() => expect(onCopyFeedback).toHaveBeenCalledTimes(1));
    expect(onCopyFeedback).toHaveBeenCalledWith({
      message: "PC clipboard text is now in this device's clipboard.",
      tone: "success",
    });
    expect(copyButton).toHaveProperty("disabled", false);
  });

  it("distinguishes a PC read failure from a device clipboard denial", async () => {
    const onCopyFeedback = vi.fn<(feedback: AppToastMessage) => void>();
    const failedPcRead = () =>
      Promise.resolve<ClipboardGetResultMessage>({
        type: "clipboard.get.result",
        operationId: "failed-read",
        succeeded: false,
        code: "VAIR-CLIPBOARD-NO-TEXT",
        message: "The PC clipboard has no text.",
      });
    const view = render(
      <ClipboardReadHarness onCopyFeedback={onCopyFeedback} onGetTextForDevice={failedPcRead} />,
    );

    fireEvent.click(
      screen.getByRole("button", { name: "Get PC clipboard text into this device's clipboard" }),
    );
    await waitFor(() =>
      expect(onCopyFeedback.mock.lastCall?.[0].message).toContain(
        "Could not get PC clipboard text",
      ),
    );

    vi.mocked(writeDeferredTextToDeviceClipboard).mockResolvedValue({ status: "denied" });
    view.rerender(
      <ClipboardReadHarness
        onCopyFeedback={onCopyFeedback}
        onGetTextForDevice={successfulDeviceRead}
      />,
    );
    fireEvent.click(
      screen.getByRole("button", { name: "Get PC clipboard text into this device's clipboard" }),
    );
    await waitFor(() =>
      expect(onCopyFeedback.mock.lastCall?.[0].message).toContain(
        "did not allow clipboard writing",
      ),
    );
  });

  it("omits direct device copy when deferred clipboard writing is unsupported", () => {
    vi.mocked(canWriteDeferredTextToDeviceClipboard).mockReturnValue(false);
    render(<ClipboardReadHarness />);

    expect(
      screen.queryByRole("button", { name: "Get PC clipboard text into this device's clipboard" }),
    ).toBeNull();
    expect(screen.getByRole("button", { name: "Copy selected text" })).toBeTruthy();
  });

  it("ignores a device clipboard write result after unmount", async () => {
    let resolveWrite: ((value: { status: "copied" }) => void) | undefined;
    vi.mocked(writeDeferredTextToDeviceClipboard).mockReturnValue(
      new Promise((resolve) => {
        resolveWrite = resolve;
      }),
    );
    const onCancelGetTextForDevice = vi.fn();
    const onCopyFeedback = vi.fn<(feedback: AppToastMessage) => void>();
    const view = render(
      <ClipboardReadHarness
        onCancelGetTextForDevice={onCancelGetTextForDevice}
        onCopyFeedback={onCopyFeedback}
      />,
    );

    fireEvent.click(
      screen.getByRole("button", { name: "Get PC clipboard text into this device's clipboard" }),
    );
    view.unmount();
    resolveWrite?.({ status: "copied" });
    await Promise.resolve();

    expect(onCancelGetTextForDevice).toHaveBeenCalledOnce();
    expect(onCopyFeedback).not.toHaveBeenCalled();
  });

  it("reports device-copy completion after the StrictMode effect cycle", async () => {
    const onCopyFeedback = vi.fn<(feedback: AppToastMessage) => void>();
    render(
      <StrictMode>
        <ClipboardReadHarness onCopyFeedback={onCopyFeedback} />
      </StrictMode>,
    );

    fireEvent.click(
      screen.getByRole("button", { name: "Get PC clipboard text into this device's clipboard" }),
    );

    await waitFor(() =>
      expect(onCopyFeedback).toHaveBeenCalledWith({
        message: "PC clipboard text is now in this device's clipboard.",
        tone: "success",
      }),
    );
  });
});
