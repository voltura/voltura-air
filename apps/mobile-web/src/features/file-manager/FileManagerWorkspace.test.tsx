import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { publishFileManagerResult } from "../../foundation/connection/fileManagerResultBus";
import type { ClientMessage, FileManagerEntry, FileManagerPanelPage } from "../../foundation/protocol/messages";
import FileManagerWorkspace from "./FileManagerWorkspace";

const entries = (start: number, count: number): FileManagerEntry[] => Array.from({ length: count }, (_, offset) => ({
  id: `entry-${start + offset}`,
  name: `file-${start + offset}.txt`,
  kind: "file",
  extension: "txt",
  size: start + offset,
  modifiedUtc: "2026-08-04T00:00:00Z",
  attributes: []
}));

const page = (panel: "left" | "right", pageEntries: FileManagerEntry[], continuation: string | null): FileManagerPanelPage => ({
  panel,
  revision: `${panel}-revision`,
  displayPath: panel === "left" ? "Downloads" : "Documents",
  parentId: null,
  driveId: "drive-a",
  sortBy: "name",
  descending: false,
  totalCount: panel === "left" ? 101 : 0,
  entries: pageEntries,
  continuation
});

describe("FileManagerWorkspace pagination and selection", () => {
  beforeEach(() => {
    vi.stubGlobal("matchMedia", vi.fn(() => ({ matches: false, addEventListener: vi.fn(), removeEventListener: vi.fn() })));
    vi.stubGlobal("ResizeObserver", class {
      observe = vi.fn();
      disconnect = vi.fn();
    });
  });

  it("virtualizes, loads once near the end, retries inline, and preserves full-directory select all", async () => {
    const sent: ClientMessage[] = [];
    render(<FileManagerWorkspace capability={{ canBrowse: true, canModify: true, maxPageSize: 100 }} connectionEpoch={1} send={(message) => sent.push(message)} state="paired" />);
    const open = sent.find((message) => message.type === "file.session.open");
    expect(open?.type).toBe("file.session.open");

    act(() => { publishFileManagerResult({
      type: "file.session.open.result",
      operationId: open!.operationId,
      succeeded: true,
      message: "Opened.",
      session: {
        sessionId: "session-a",
        drives: [{ id: "drive-a", label: "C:", driveType: "fixed" }],
        shortcuts: [{ id: "downloads-a", label: "Downloads" }],
        left: page("left", entries(0, 100), "continuation-a"),
        right: page("right", [], null)
      }
    }); });

    expect(screen.getAllByRole("checkbox").length).toBeLessThan(100);
    const leftList = document.querySelector(".file-panel:first-child .file-list")!;
    fireEvent.scroll(leftList, { target: { scrollTop: 4300 } });
    await waitFor(() => expect(sent.filter((message) => message.type === "file.page.get")).toHaveLength(1));
    let pageRequests = sent.filter((message) => message.type === "file.page.get");
    expect(pageRequests).toHaveLength(1);
    fireEvent.scroll(leftList, { target: { scrollTop: 4350 } });
    expect(sent.filter((message) => message.type === "file.page.get")).toHaveLength(1);

    const firstPageRequest = pageRequests[0];
    if (!firstPageRequest) {throw new Error("Expected the first page request.");}
    act(() => { publishFileManagerResult({ type: "file.page.get.result", operationId: firstPageRequest.operationId, succeeded: false, code: "share-unavailable", message: "Network share unavailable." }); });
    expect(screen.getByRole("status").textContent).toBe("Network share unavailable.");
    fireEvent.click(await screen.findByRole("button", { name: "Retry loading more" }));
    pageRequests = sent.filter((message) => message.type === "file.page.get");
    expect(pageRequests).toHaveLength(2);
    const retryPageRequest = pageRequests[1];
    if (!retryPageRequest) {throw new Error("Expected the retry page request.");}

    act(() => { publishFileManagerResult({
      type: "file.page.get.result",
      operationId: retryPageRequest.operationId,
      succeeded: true,
      message: "Loaded.",
      page: page("left", entries(100, 1), null)
    }); });
    fireEvent.scroll(leftList, { target: { scrollTop: 4600 } });
    fireEvent.click(screen.getByRole("button", { name: "Select all" }));
    fireEvent.click(await screen.findByRole("checkbox", { name: "Select file-100.txt" }));
    fireEvent.click(screen.getByRole("button", { name: "Copy" }));

    const clipboard = [...sent].reverse().find((message) => message.type === "file.clipboard.set");
    expect(clipboard).toMatchObject({ type: "file.clipboard.set", selectionAll: true, entryIds: [], excludedEntryIds: ["entry-100"] });
  });
});
