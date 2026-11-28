import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { publishAiAssistantResult } from "../../foundation/connection/aiAssistantResultBus";
import type { ClientMessage } from "../../foundation/protocol/messages";
import AiAssistantWorkspace from "./AiAssistantWorkspace";

vi.mock("../../foundation/connection/pairingCredentials", () => ({
  signClientPayload: () => "signature",
}));
const capability = {
  enabled: true,
  available: true,
  permissionGranted: true,
  canUse: true,
  requiresRepair: false,
  active: false,
  ownedByClient: false,
  working: false,
};
const activePc = {
  customName: false,
  hostIdentityPublicKey: "key",
  id: "pc",
  name: "PC",
  url: "https://pc.test",
};
beforeEach(() => {
  Element.prototype.scrollIntoView = vi.fn();
});
afterEach(() => {
  vi.useRealTimers();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});
function setup(send = vi.fn<(message: ClientMessage) => boolean | void>()) {
  const props = {
    activePc,
    capability,
    clientId: "client",
    onBack: vi.fn(),
    send,
    state: "paired" as const,
  };
  const view = render(<AiAssistantWorkspace {...props} />);
  return { ...view, props, send };
}
async function open(send: ReturnType<typeof setup>["send"]) {
  await waitFor(() =>
    expect(send.mock.calls.some(([m]) => m.type === "ai.assistant.open")).toBe(true),
  );
  const message = send.mock.calls.find(([m]) => m.type === "ai.assistant.open")?.[0];
  if (message?.type !== "ai.assistant.open") {
    throw new Error("Missing open");
  }
  return message;
}
function accept(operationId: string) {
  act(() =>
    publishAiAssistantResult({
      type: "ai.assistant.open.result",
      operationId,
      succeeded: true,
      message: "Ready",
    }),
  );
}
function question() {
  return screen.getByRole("textbox", { name: "Ask the Voltura Air Assistant" });
}

it("opens and closes the Assistant on a controller without secure-context UUID support", async () => {
  vi.stubGlobal("crypto", { getRandomValues: crypto.getRandomValues.bind(crypto) });
  const { send, unmount } = setup();
  const request = await open(send);
  expect(request.operationId).toMatch(/^[a-z0-9-]+$/u);
  accept(request.operationId);

  unmount();

  const close = send.mock.lastCall?.[0];
  if (close?.type !== "ai.assistant.close") {
    throw new Error("Expected an Assistant close request.");
  }
  expect(close.operationId).toMatch(/^[a-z0-9-]+$/u);
});

it.each(["ask", "reset"])("unblocks %s immediately when the request cannot be sent", async (kind) => {
  const { send } = setup();
  accept((await open(send)).operationId);
  send.mockReturnValueOnce(false);
  const draft = "Keep this draft";
  fireEvent.change(question(), { target: { value: draft } });

  if (kind === "ask") {
    fireEvent.keyDown(question(), { key: "Enter" });
  } else {
    fireEvent.click(screen.getByRole("button", { name: "New conversation" }));
  }

  expect((question() as HTMLTextAreaElement).disabled).toBe(false);
  expect((question() as HTMLTextAreaElement).value).toBe(draft);
  expect(screen.getByText("Could not send the Assistant request. Reconnect and try again.")).toBeTruthy();
});

it("reports an unsent open immediately without attempting to close a session that never opened", async () => {
  const send = vi.fn<(message: ClientMessage) => boolean | void>().mockReturnValueOnce(false);
  const { unmount } = setup(send);
  await open(send);

  expect(screen.getByText("Could not open the Assistant. Reconnect and reopen the tool.")).toBeTruthy();
  unmount();
  expect(send.mock.calls.some(([message]) => message.type === "ai.assistant.close")).toBe(false);
});

it("keeps a newer suggested draft when the prior question is accepted", async () => {
  const { send } = setup();
  accept((await open(send)).operationId);
  fireEvent.change(question(), { target: { value: "First question" } });
  fireEvent.keyDown(question(), { key: "Enter" });
  const request = send.mock.calls.find(([message]) => message.type === "ai.assistant.ask")?.[0];
  if (request?.type !== "ai.assistant.ask") {
    throw new Error("Expected Assistant question");
  }

  fireEvent.click(screen.getByRole("button", { name: "Top features" }));
  act(() => publishAiAssistantResult({
    type: "ai.assistant.ask.result",
    operationId: request.operationId,
    succeeded: true,
    message: "Accepted.",
  }));

  expect((question() as HTMLTextAreaElement).value).toBe("What are the top features of Voltura Air?");
});
it.each(["open", "ask", "reset"])(
  "expires a lost %s acknowledgement and ignores its late result",
  async (kind) => {
    if (kind === "open") {
      vi.useFakeTimers();
    }
    const { send } = setup();
    if (kind === "open") {
      await act(() => vi.advanceTimersByTimeAsync(0));
    }
    const opening =
      kind === "open"
        ? send.mock.calls.find(([m]) => m.type === "ai.assistant.open")?.[0]
        : await open(send);
    if (opening?.type !== "ai.assistant.open") {
      throw new Error("Expected opening request");
    }
    if (kind !== "open") {
      accept(opening.operationId);
    }
    if (kind !== "open") {
      vi.useFakeTimers();
    }
    if (kind === "ask") {
      fireEvent.change(question(), { target: { value: "Question" } });
      fireEvent.keyDown(question(), { key: "Enter" });
    } else if (kind === "reset") {
      fireEvent.click(screen.getByRole("button", { name: "New conversation" }));
    }
    const request = send.mock.calls.at(-1)?.[0];
    if (!request || !("operationId" in request)) {
      throw new Error("Missing operation");
    }
    act(() => {
      vi.advanceTimersByTime(30_000);
    });
    expect(
      screen.getByText(
        "The PC did not confirm the Assistant request. Reopen the tool to reconnect.",
      ),
    ).toBeTruthy();
    act(() =>
      publishAiAssistantResult({
        type:
          kind === "open"
            ? "ai.assistant.open.result"
            : kind === "ask"
              ? "ai.assistant.ask.result"
              : "ai.assistant.reset.result",
        operationId: request.operationId,
        succeeded: true,
        message: "Late success",
      }),
    );
    expect(screen.queryByText("Late success")).toBeNull();
    if (kind === "open") {
      expect((question() as HTMLTextAreaElement).disabled).toBe(true);
    }
    if (kind === "ask") {
      expect((question() as HTMLTextAreaElement).value).toBe("Question");
    }
  },
);
it.each(["disconnect", "revoked"])("blocks asking and resetting after %s", async (reason) => {
  const { send, rerender, props } = setup();
  accept((await open(send)).operationId);
  fireEvent.change(question(), { target: { value: "Keep this draft" } });
  rerender(
    <AiAssistantWorkspace
      {...props}
      state={reason === "disconnect" ? "connecting" : "paired"}
      capability={reason === "revoked" ? { ...capability, canUse: false } : capability}
    />,
  );
  fireEvent.keyDown(question(), { key: "Enter" });
  fireEvent.click(screen.getByRole("button", { name: "New conversation" }));
  expect(
    send.mock.calls.some(([m]) => m.type === "ai.assistant.ask" || m.type === "ai.assistant.reset"),
  ).toBe(false);
  expect((question() as HTMLTextAreaElement).disabled).toBe(true);
});
it("does not reopen when an old open success arrives after closure", async () => {
  const { send } = setup();
  const request = await open(send);
  act(() => publishAiAssistantResult({ type: "ai.assistant.closed", reason: "closed" }));
  accept(request.operationId);
  expect((question() as HTMLTextAreaElement).disabled).toBe(true);
});
it("keeps an in-flight open when a parent supplies a new send callback", async () => {
  const { send, props, rerender } = setup();
  const request = await open(send);
  const replacement = vi.fn<(message: ClientMessage) => void>();
  rerender(<AiAssistantWorkspace {...props} send={replacement} />);
  await act(async () => new Promise((resolve) => setTimeout(resolve, 5)));
  expect(replacement.mock.calls.some(([m]) => m.type === "ai.assistant.open")).toBe(false);
  accept(request.operationId);
  expect((question() as HTMLTextAreaElement).disabled).toBe(false);
});
it("opens a fresh session after reconnect instead of retaining a stale opened flag", async () => {
  const { send, props, rerender } = setup();
  accept((await open(send)).operationId);
  rerender(<AiAssistantWorkspace {...props} state="connecting" />);
  rerender(<AiAssistantWorkspace {...props} />);
  await waitFor(() =>
    expect(send.mock.calls.filter(([m]) => m.type === "ai.assistant.open")).toHaveLength(2),
  );
});
