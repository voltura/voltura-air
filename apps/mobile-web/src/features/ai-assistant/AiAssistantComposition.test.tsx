import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { publishAiAssistantResult } from "../../foundation/connection/aiAssistantResultBus";
import type { ClientMessage } from "../../foundation/protocol/messages";
import AiAssistantWorkspace from "./AiAssistantWorkspace";

vi.mock("../../foundation/connection/pairingCredentials", () => ({
  signClientPayload: () => "test-signature",
}));
afterEach(() => vi.restoreAllMocks());

it("does not submit Enter while an IME is committing composed text", async () => {
  Element.prototype.scrollIntoView = vi.fn();
  const send = vi.fn<(message: ClientMessage) => void>();
  render(
    <AiAssistantWorkspace
      activePc={{
        customName: false,
        hostIdentityPublicKey: "host-key",
        id: "pc-1",
        name: "PC",
        url: "https://pc.test",
      }}
      capability={{
        enabled: true,
        available: true,
        permissionGranted: true,
        canUse: true,
        requiresRepair: false,
        active: false,
        ownedByClient: false,
        working: false,
      }}
      clientId="client-1"
      onBack={vi.fn()}
      send={send}
      state="paired"
    />,
  );
  await waitFor(() =>
    expect(send).toHaveBeenCalledWith(expect.objectContaining({ type: "ai.assistant.open" })),
  );
  const open = send.mock.calls.find(([message]) => message.type === "ai.assistant.open")?.[0];
  if (open?.type !== "ai.assistant.open") {
    throw new Error("Expected automatic Assistant open.");
  }
  act(() =>
    publishAiAssistantResult({
      type: "ai.assistant.open.result",
      operationId: open.operationId,
      succeeded: true,
      message: "ready",
    }),
  );
  const question = screen.getByRole("textbox", { name: "Ask the Voltura Air Assistant" });
  fireEvent.change(question, { target: { value: "漢字" } });
  fireEvent.keyDown(question, { key: "Enter", isComposing: true });
  expect(send.mock.calls.some(([message]) => message.type === "ai.assistant.ask")).toBe(false);
  fireEvent.keyDown(question, { key: "Enter", isComposing: false });
  expect(send.mock.calls.some(([message]) => message.type === "ai.assistant.ask")).toBe(true);
});
