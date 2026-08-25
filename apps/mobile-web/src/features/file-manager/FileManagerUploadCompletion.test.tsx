import { act, fireEvent, render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { publishFileManagerResult } from "../../foundation/connection/fileManagerResultBus";
import type { ClientMessage, FileManagerPanelPage } from "../../foundation/protocol/messages";

vi.mock("./FileTransferMenu", () => ({
  FileTransferMenu: ({ onUploadCompleted }: { onUploadCompleted?: (panel: "left" | "right", fileName: string) => void }) =>
    <button type="button" onClick={() => onUploadCompleted?.("left", "uploaded.txt")}>Complete test upload</button>
}));

import FileManagerWorkspace from "./FileManagerWorkspace";

const page = (names: string[], continuation: string | null = null, totalCount = names.length): FileManagerPanelPage => ({
  panel: "left",
  revision: "left-revision",
  displayPath: "Downloads",
  parentId: null,
  driveId: "drive-a",
  sortBy: "name",
  descending: false,
  totalCount,
  entries: names.map((name, index) => ({ id: `entry-${name}`, name, kind: "file", extension: "txt", size: index, modifiedUtc: "2026-08-25T00:00:00Z", attributes: [] })),
  continuation
});

describe("FileManagerWorkspace upload completion", () => {
  beforeEach(() => {
    vi.stubGlobal("matchMedia", vi.fn(() => ({ matches: false, addEventListener: vi.fn(), removeEventListener: vi.fn() })));
    vi.stubGlobal("ResizeObserver", class { observe = vi.fn(); disconnect = vi.fn(); });
  });

  it("refreshes the destination and selects the host-confirmed uploaded file", () => {
    const sent: ClientMessage[] = [];
    render(<FileManagerWorkspace
      activePc={{ customName: false, id: "pc", name: "PC", url: "https://pc.invalid", hostIdentityPublicKey: "A".repeat(87), transportMode: "secure-direct" }}
      capability={{ canBrowse: true, canModify: true, canTransfer: true, hidesProtectedSystemItems: true, maxPageSize: 100 }}
      clientId="client" canMirrorView connectionEpoch={1} mirrorViewUnavailableMessage="PC Screen unavailable."
      onMirrorView={() => undefined} send={(message) => sent.push(message)} state="paired"
    />);
    const open = sent.find((message) => message.type === "file.session.open");
    if (open?.type !== "file.session.open") {throw new Error("Expected Files to open a session.");}
    act(() => publishFileManagerResult({
      type: "file.session.open.result", operationId: open.operationId, succeeded: true, message: "Opened.",
      session: {
        sessionId: "session-a", drives: [{ id: "drive-a", label: "C:", driveType: "fixed" }], shortcuts: [],
        left: page(["existing.txt"]), right: { ...page([]), panel: "right", revision: "right-revision", displayPath: "Documents" }
      }
    }));

    fireEvent.click(screen.getByRole("button", { name: "Complete test upload" }));
    const refresh = [...sent].reverse().find((message) => message.type === "file.refresh");
    if (refresh?.type !== "file.refresh") {throw new Error("Expected the uploaded folder to refresh.");}
    act(() => publishFileManagerResult({ type: "file.refresh.result", operationId: refresh.operationId, succeeded: true, message: "Folder loaded.", page: page(["existing.txt", "uploaded.txt"]) }));

    expect((screen.getByRole("checkbox", { name: "Select uploaded.txt" }) as HTMLInputElement).checked).toBe(true);
    expect((screen.getByRole("checkbox", { name: "Select existing.txt" }) as HTMLInputElement).checked).toBe(false);
  });

  it("continues paging until the uploaded file can be selected", () => {
    const sent: ClientMessage[] = [];
    render(<FileManagerWorkspace
      activePc={{ customName: false, id: "pc", name: "PC", url: "https://pc.invalid", hostIdentityPublicKey: "A".repeat(87), transportMode: "secure-direct" }}
      capability={{ canBrowse: true, canModify: true, canTransfer: true, hidesProtectedSystemItems: true, maxPageSize: 100 }}
      clientId="client" canMirrorView connectionEpoch={1} mirrorViewUnavailableMessage="PC Screen unavailable."
      onMirrorView={() => undefined} send={(message) => sent.push(message)} state="paired"
    />);
    const open = sent.find((message) => message.type === "file.session.open");
    if (open?.type !== "file.session.open") {throw new Error("Expected Files to open a session.");}
    act(() => publishFileManagerResult({
      type: "file.session.open.result", operationId: open.operationId, succeeded: true, message: "Opened.",
      session: {
        sessionId: "session-a", drives: [{ id: "drive-a", label: "C:", driveType: "fixed" }], shortcuts: [],
        left: page(["existing.txt"]), right: { ...page([]), panel: "right", revision: "right-revision", displayPath: "Documents" }
      }
    }));

    fireEvent.click(screen.getByRole("button", { name: "Complete test upload" }));
    const refresh = [...sent].reverse().find((message) => message.type === "file.refresh");
    if (refresh?.type !== "file.refresh") {throw new Error("Expected the uploaded folder to refresh.");}
    const firstPageNames = Array.from({ length: 100 }, (_, index) => `file-${String(index).padStart(3, "0")}.txt`);
    act(() => publishFileManagerResult({ type: "file.refresh.result", operationId: refresh.operationId, succeeded: true, message: "Folder loaded.", page: page(firstPageNames, "next-page", 101) }));
    const nextPage = [...sent].reverse().find((message) => message.type === "file.page.get");
    if (nextPage?.type !== "file.page.get") {throw new Error("Expected Files to locate the uploaded row in the next page.");}
    act(() => publishFileManagerResult({ type: "file.page.get.result", operationId: nextPage.operationId, succeeded: true, message: "Folder loaded.", page: page(["uploaded.txt"], null, 101) }));

    expect((screen.getByRole("checkbox", { name: "Select uploaded.txt" }) as HTMLInputElement).checked).toBe(true);
  });
});
