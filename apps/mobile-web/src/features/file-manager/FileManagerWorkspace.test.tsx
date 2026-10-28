import { act, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { publishFileManagerResult } from "../../foundation/connection/fileManagerResultBus";
import type {
  ClientMessage,
  FileJobSnapshot,
  FileManagerEntry,
  FileManagerPanelPage,
} from "../../foundation/protocol/messages";
import FileManagerWorkspace from "./FileManagerWorkspace";

const entries = (start: number, count: number): FileManagerEntry[] =>
  Array.from({ length: count }, (_, offset) => ({
    id: `entry-${start + offset}`,
    name: `file-${start + offset}.txt`,
    kind: "file",
    extension: "txt",
    size: start + offset,
    modifiedUtc: "2026-08-04T00:00:00Z",
    attributes: [],
  }));

const page = (
  panel: "left" | "right",
  pageEntries: FileManagerEntry[],
  continuation: string | null,
): FileManagerPanelPage => ({
  panel,
  revision: `${panel}-revision`,
  displayPath: panel === "left" ? "Downloads" : "Documents",
  parentId: null,
  driveId: "drive-a",
  sortBy: "name",
  descending: false,
  totalCount: panel === "left" ? 101 : 0,
  entries: pageEntries,
  continuation,
});

describe("FileManagerWorkspace pagination and selection", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    vi.stubGlobal(
      "matchMedia",
      vi.fn(() => ({ matches: false, addEventListener: vi.fn(), removeEventListener: vi.fn() })),
    );
    vi.stubGlobal(
      "ResizeObserver",
      class {
        observe = vi.fn();
        disconnect = vi.fn();
      },
    );
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("cleans up read deadlines when Files unmounts", () => {
    vi.useFakeTimers();
    const sent: ClientMessage[] = [];
    const view = renderWorkspace(sent);
    expect(vi.getTimerCount()).toBeGreaterThan(0);
    view.unmount();
    vi.runAllTicks();
    expect(vi.getTimerCount()).toBe(0);
  });
  it("does not let an older mirror-open timeout replace newer opening feedback", () => {
    vi.useFakeTimers();
    const sent: ClientMessage[] = [];
    renderWorkspace(sent);
    openSession(sent, entries(0, 1), []);
    fireEvent.click(screen.getByRole("button", { name: "file-0.txt" }));
    fireEvent.click(screen.getByRole("button", { name: "View" }));
    act(() => {
      vi.advanceTimersByTime(1000);
    });
    fireEvent.click(screen.getByRole("button", { name: "View" }));
    act(() => {
      vi.advanceTimersByTime(14_000);
    });
    expect(screen.getByText("Opening the file on the PC before starting the mirror…")).toBeTruthy();
  });

  it("bounds a Files session open with no response and ignores its late reply", () => {
    vi.useFakeTimers();
    const sent: ClientMessage[] = [];
    renderWorkspace(sent);
    act(() => {
      vi.advanceTimersByTime(15_000);
    });
    expect(
      screen.getByText(
        "The PC did not respond to the Files request. Check the connection and the PC before retrying.",
      ),
    ).toBeTruthy();
    openSession(sent, entries(0, 1), []);
    expect(screen.queryByRole("button", { name: "file-0.txt" })).toBeNull();
  });
  it("does not replay a duplicate session result over a current file view", () => {
    const sent: ClientMessage[] = [];
    renderWorkspace(sent);
    openSession(sent, entries(0, 1), []);
    openSession(sent, entries(20, 1), []);
    expect(screen.getByRole("button", { name: "file-0.txt" })).toBeTruthy();
    expect(screen.queryByRole("button", { name: "file-20.txt" })).toBeNull();
  });
  it("ignores a Files session response that arrives after disconnect", async () => {
    const sent: ClientMessage[] = [];
    const sender = (message: ClientMessage) => {
      sent.push(message);
    };
    const props = {
      capability: {
        canBrowse: true,
        canModify: true,
        hidesProtectedSystemItems: true,
        maxPageSize: 100,
      },
      canMirrorView: true,
      connectionEpoch: 1,
      mirrorViewUnavailableMessage: "Unavailable",
      onMirrorView: vi.fn(),
      send: sender,
      state: "paired" as const,
    };
    const view = render(<FileManagerWorkspace {...props} />);
    view.rerender(<FileManagerWorkspace {...props} state="connecting" />);
    await act(async () => {
      await Promise.resolve();
    });
    openSession(sent, entries(0, 1), []);
    expect(screen.queryByRole("button", { name: "file-0.txt" })).toBeNull();
  });
  it.each(["refresh", "properties", "view"])("bounds an unanswered Files %s request", (kind) => {
    vi.useFakeTimers();
    const sent: ClientMessage[] = [];
    const mirror = vi.fn();
    renderWorkspace(sent, mirror);
    openSession(sent, entries(0, 1), []);
    if (kind === "refresh") {
      fireEvent.click(screen.getByRole("button", { name: "Refresh left panel" }));
    } else {
      fireEvent.click(screen.getByRole("button", { name: "file-0.txt" }));
      fireEvent.click(
        screen.getByRole("button", { name: kind === "view" ? "View" : "Properties" }),
      );
    }
    act(() => {
      vi.advanceTimersByTime(15_000);
    });
    expect(
      screen.getByText(
        "The PC did not respond to the Files request. Check the connection and the PC before retrying.",
      ),
    ).toBeTruthy();
    if (kind === "view") {
      const open = [...sent].reverse().find((message) => message.type === "file.open");
      if (open?.type !== "file.open") {
        throw new Error("Expected file open");
      }
      act(() =>
        publishFileManagerResult({
          type: "file.open.result",
          operationId: open.operationId,
          succeeded: true,
          message: "Late open",
        }),
      );
      expect(mirror).not.toHaveBeenCalled();
    }
  });
  it("expires a missing continuation page so pagination can be retried", () => {
    vi.useFakeTimers();
    const sent: ClientMessage[] = [];
    renderWorkspace(sent);
    const request = sent.find((message) => message.type === "file.session.open");
    if (request?.type !== "file.session.open") {
      throw new Error("Expected session");
    }
    act(() =>
      publishFileManagerResult({
        type: "file.session.open.result",
        operationId: request.operationId,
        succeeded: true,
        message: "Opened",
        session: {
          sessionId: "session-a",
          drives: [{ id: "drive-a", label: "C:", driveType: "fixed" }],
          shortcuts: [],
          left: page("left", entries(0, 100), "more"),
          right: page("right", [], null),
        },
      }),
    );
    fireEvent.scroll(document.querySelector(".file-panel:first-child .file-list")!, {
      target: { scrollTop: 4300 },
    });
    expect(sent.some((message) => message.type === "file.page.get")).toBe(true);
    act(() => {
      vi.advanceTimersByTime(15_000);
    });
    expect(screen.queryByText("Loading more…")).toBeNull();
    expect(screen.getByRole("button", { name: "Retry loading more" })).toBeTruthy();
  });

  it("does not apply an old continuation timeout to a newly refreshed folder", () => {
    vi.useFakeTimers();
    const sent: ClientMessage[] = [];
    renderWorkspace(sent);
    const request = sent.find((message) => message.type === "file.session.open")!;
    act(() =>
      publishFileManagerResult({
        type: "file.session.open.result",
        operationId: request.operationId!,
        succeeded: true,
        message: "Opened",
        session: {
          sessionId: "session-a",
          drives: [],
          shortcuts: [],
          left: page("left", entries(0, 100), "more"),
          right: page("right", [], null),
        },
      }),
    );
    fireEvent.scroll(document.querySelector(".file-panel:first-child .file-list")!, {
      target: { scrollTop: 4300 },
    });
    expect(sent.some((message) => message.type === "file.page.get")).toBe(true);
    fireEvent.click(screen.getByRole("button", { name: "Refresh left panel" }));
    const refresh = sent.find((message) => message.type === "file.refresh")!;
    act(() =>
      publishFileManagerResult({
        type: "file.refresh.result",
        operationId: refresh.operationId!,
        succeeded: true,
        message: "Refreshed",
        page: { ...page("left", entries(200, 1), null), revision: "new" },
      }),
    );
    act(() => {
      vi.advanceTimersByTime(15_000);
    });
    fireEvent.scroll(document.querySelector(".file-panel:first-child .file-list")!, {
      target: { scrollTop: 0 },
    });
    expect(screen.getByRole("button", { name: "file-200.txt" })).toBeTruthy();
    expect(screen.queryByText("The PC did not respond. Retry this folder.")).toBeNull();
  });

  it("uses row taps for one-file selection while checkboxes retain multi-select", () => {
    const sent: ClientMessage[] = [];
    renderWorkspace(sent);
    openSession(sent, entries(0, 2), []);

    fireEvent.click(screen.getByRole("checkbox", { name: "Select file-0.txt" }));
    fireEvent.click(screen.getByRole("button", { name: "file-1.txt" }));

    expect(
      (screen.getByRole("checkbox", { name: "Select file-0.txt" }) as HTMLInputElement).checked,
    ).toBe(false);
    expect(
      (screen.getByRole("checkbox", { name: "Select file-1.txt" }) as HTMLInputElement).checked,
    ).toBe(true);
  });

  it("virtualizes, loads once near the end, retries inline, and preserves full-directory select all", async () => {
    const sent: ClientMessage[] = [];
    render(
      <FileManagerWorkspace
        capability={{
          canBrowse: true,
          canModify: true,
          hidesProtectedSystemItems: true,
          maxPageSize: 100,
        }}
        canMirrorView
        connectionEpoch={1}
        mirrorViewUnavailableMessage="PC Screen unavailable."
        onMirrorView={() => undefined}
        send={(message) => sent.push(message)}
        state="paired"
      />,
    );
    const open = sent.find((message) => message.type === "file.session.open");
    expect(open?.type).toBe("file.session.open");

    act(() => {
      publishFileManagerResult({
        type: "file.session.open.result",
        operationId: open!.operationId,
        succeeded: true,
        message: "Opened.",
        session: {
          sessionId: "session-a",
          drives: [{ id: "drive-a", label: "C:", driveType: "fixed" }],
          shortcuts: [{ id: "downloads-a", label: "Downloads" }],
          left: page("left", entries(0, 100), "continuation-a"),
          right: page("right", [], null),
        },
      });
    });

    expect(screen.getAllByRole("checkbox").length).toBeLessThan(100);
    const leftList = document.querySelector(".file-panel:first-child .file-list")!;
    fireEvent.scroll(leftList, { target: { scrollTop: 4300 } });
    await waitFor(() =>
      expect(sent.filter((message) => message.type === "file.page.get")).toHaveLength(1),
    );
    let pageRequests = sent.filter((message) => message.type === "file.page.get");
    expect(pageRequests).toHaveLength(1);
    fireEvent.scroll(leftList, { target: { scrollTop: 4350 } });
    expect(sent.filter((message) => message.type === "file.page.get")).toHaveLength(1);

    const firstPageRequest = pageRequests[0];
    if (!firstPageRequest) {
      throw new Error("Expected the first page request.");
    }
    act(() => {
      publishFileManagerResult({
        type: "file.page.get.result",
        operationId: firstPageRequest.operationId,
        succeeded: false,
        code: "share-unavailable",
        message: "Network share unavailable.",
      });
    });
    expect(screen.getByRole("alert").textContent).toContain("Network share unavailable.");
    fireEvent.click(await screen.findByRole("button", { name: "Retry loading more" }));
    pageRequests = sent.filter((message) => message.type === "file.page.get");
    expect(pageRequests).toHaveLength(2);
    const retryPageRequest = pageRequests[1];
    if (!retryPageRequest) {
      throw new Error("Expected the retry page request.");
    }

    act(() => {
      publishFileManagerResult({
        type: "file.page.get.result",
        operationId: retryPageRequest.operationId,
        succeeded: true,
        message: "Loaded.",
        page: page("left", entries(100, 1), null),
      });
    });
    fireEvent.scroll(leftList, { target: { scrollTop: 5200 } });
    fireEvent.click(screen.getByRole("button", { name: "Select all" }));
    fireEvent.click(await screen.findByRole("checkbox", { name: "Select file-100.txt" }));
    fireEvent.click(screen.getByRole("button", { name: "Copy" }));

    const clipboard = [...sent].reverse().find((message) => message.type === "file.clipboard.set");
    expect(clipboard).toMatchObject({
      type: "file.clipboard.set",
      selectionAll: true,
      entryIds: [],
      excludedEntryIds: ["entry-100"],
    });
  });

  it.each([
    ["overlaps the existing entries", page("left", entries(99, 1), null)],
    [
      "changes the panel revision",
      { ...page("left", entries(100, 1), null), revision: "changed-revision" },
    ],
  ])("rejects a successful continuation page that %s", async (_caseName, returnedPage) => {
    const sent: ClientMessage[] = [];
    renderWorkspace(sent);
    const open = sent.find((message) => message.type === "file.session.open");
    if (open?.type !== "file.session.open") {
      throw new Error("Expected Files to open a session.");
    }
    act(() =>
      publishFileManagerResult({
        type: "file.session.open.result",
        operationId: open.operationId,
        succeeded: true,
        message: "Opened.",
        session: {
          sessionId: "session-a",
          drives: [{ id: "drive-a", label: "C:", driveType: "fixed" }],
          shortcuts: [],
          left: page("left", entries(0, 100), "continuation-a"),
          right: page("right", [], null),
        },
      }),
    );

    fireEvent.scroll(document.querySelector(".file-panel:first-child .file-list")!, {
      target: { scrollTop: 4300 },
    });
    await waitFor(() =>
      expect(sent.some((message) => message.type === "file.page.get")).toBe(true),
    );
    const request = [...sent].reverse().find((message) => message.type === "file.page.get");
    if (request?.type !== "file.page.get") {
      throw new Error("Expected a continuation request.");
    }
    act(() =>
      publishFileManagerResult({
        type: "file.page.get.result",
        operationId: request.operationId,
        succeeded: true,
        message: "Loaded.",
        page: returnedPage,
      }),
    );

    expect(screen.getByRole("alert").textContent).toContain("invalid file page");
    expect(screen.getByRole("button", { name: "Retry loading more" })).toBeTruthy();
  });

  it("uses Copy for the other panel in a wide layout and dismisses transient menus on other interactions", () => {
    vi.spyOn(HTMLElement.prototype, "clientWidth", "get").mockReturnValue(800);
    vi.stubGlobal(
      "ResizeObserver",
      class {
        constructor(private readonly callback: ResizeObserverCallback) {}
        observe = vi.fn(() => this.callback([], this as unknown as ResizeObserver));
        disconnect = vi.fn();
      },
    );
    const sent: ClientMessage[] = [];
    renderWorkspace(sent);
    openSession(sent, [entries(0, 1)[0]!], [entries(10, 1)[0]!]);

    fireEvent.click(screen.getByRole("checkbox", { name: "Select file-0.txt" }));
    fireEvent.click(screen.getByRole("button", { name: "Copy" }));
    expect([...sent].reverse().find((message) => message.type === "file.job.create")).toMatchObject(
      {
        type: "file.job.create",
        operation: "copy",
        panel: "left",
        destinationPanel: "right",
        destinationRevision: "right-revision",
        entryIds: ["entry-0"],
      },
    );
    expect(sent.some((message) => message.type === "file.clipboard.set")).toBe(false);

    fireEvent.click(screen.getByRole("button", { name: "Clipboard" }));
    expect(screen.getByRole("menu", { name: "Windows file clipboard" })).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "file-0.txt" }));
    expect(screen.queryByRole("menu", { name: "Windows file clipboard" })).toBeNull();

    fireEvent.click(screen.getByRole("button", { name: "Locations" }));
    expect(screen.getByRole("menu", { name: "File shortcuts" })).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Refresh left panel" }));
    expect(screen.queryByRole("menu", { name: "File shortcuts" })).toBeNull();
    expect([...sent].reverse().find((message) => message.type === "file.refresh")).toMatchObject({
      type: "file.refresh",
      panel: "left",
    });
  });

  it("enters PC Screen only after View opens the selected file successfully", () => {
    const sent: ClientMessage[] = [];
    const onMirrorView = vi.fn();
    renderWorkspace(sent, onMirrorView);
    openSession(sent, [entries(0, 1)[0]!], []);
    fireEvent.click(screen.getByRole("checkbox", { name: "Select file-0.txt" }));

    fireEvent.click(screen.getByRole("button", { name: "View" }));
    const failedOpen = [...sent].reverse().find((message) => message.type === "file.open");
    if (failedOpen?.type !== "file.open") {
      throw new Error("Expected View to open the file.");
    }
    expect(onMirrorView).not.toHaveBeenCalled();
    act(() =>
      publishFileManagerResult({
        type: "file.open.result",
        operationId: failedOpen.operationId,
        succeeded: false,
        code: "shell-failed",
        message: "Windows could not open this file.",
      }),
    );
    expect(onMirrorView).not.toHaveBeenCalled();
    expect(screen.getByRole("alert").textContent).toContain("Windows could not open this file.");

    fireEvent.click(screen.getByRole("button", { name: "View" }));
    const opened = [...sent].reverse().find((message) => message.type === "file.open");
    if (opened?.type !== "file.open") {
      throw new Error("Expected View to retry opening the file.");
    }
    act(() =>
      publishFileManagerResult({
        type: "file.open.result",
        operationId: opened.operationId,
        succeeded: true,
        message: "Opened.",
      }),
    );
    expect(onMirrorView).toHaveBeenCalledOnce();
  });

  it("keeps a failed operation selected and clears a completed operation selection", () => {
    vi.spyOn(HTMLElement.prototype, "clientWidth", "get").mockReturnValue(800);
    vi.stubGlobal(
      "ResizeObserver",
      class {
        constructor(private readonly callback: ResizeObserverCallback) {}
        observe = vi.fn(() => this.callback([], this as unknown as ResizeObserver));
        disconnect = vi.fn();
      },
    );
    const sent: ClientMessage[] = [];
    renderWorkspace(sent);
    openSession(sent, [entries(0, 1)[0]!], []);
    const checkbox = screen.getByRole("checkbox", { name: "Select file-0.txt" });
    fireEvent.click(checkbox);
    fireEvent.click(screen.getByRole("button", { name: "Copy" }));
    const failedRequest = [...sent].reverse().find((message) => message.type === "file.job.create");
    if (failedRequest?.type !== "file.job.create") {
      throw new Error("Expected a copy job.");
    }
    act(() =>
      publishFileManagerResult({
        type: "file.job.create.result",
        operationId: failedRequest.operationId,
        succeeded: true,
        message: "Queued.",
        job: job("job-failed", "copy", "running"),
      }),
    );
    act(() =>
      publishFileManagerResult({
        type: "file.jobs.status",
        jobs: [job("job-failed", "copy", "failed")],
      }),
    );
    expect((checkbox as HTMLInputElement).checked).toBe(true);
    expect(
      sent.some((message) => message.type === "file.refresh" && message.panel === "right"),
    ).toBe(true);
    expect(
      sent.some((message) => message.type === "file.refresh" && message.panel === "left"),
    ).toBe(false);

    fireEvent.click(screen.getByRole("button", { name: "Copy" }));
    const completedRequest = [...sent]
      .reverse()
      .find((message) => message.type === "file.job.create");
    if (completedRequest?.type !== "file.job.create") {
      throw new Error("Expected a second copy job.");
    }
    act(() =>
      publishFileManagerResult({
        type: "file.job.create.result",
        operationId: completedRequest.operationId,
        succeeded: true,
        message: "Queued.",
        job: job("job-complete", "copy", "running"),
      }),
    );
    act(() =>
      publishFileManagerResult({
        type: "file.jobs.status",
        jobs: [job("job-failed", "copy", "failed"), job("job-complete", "copy", "completed")],
      }),
    );
    expect((checkbox as HTMLInputElement).checked).toBe(false);
    expect(
      sent.some((message) => message.type === "file.refresh" && message.panel === "right"),
    ).toBe(true);
  });

  it("keeps terminal operation history reachable and dismissible", () => {
    const sent: ClientMessage[] = [];
    renderWorkspace(sent);
    openSession(sent, [], []);
    act(() =>
      publishFileManagerResult({
        type: "file.jobs.status",
        jobs: [job("job-failed", "copy", "failed")],
      }),
    );

    fireEvent.click(screen.getByRole("button", { name: "File operations · 1 in history" }));
    expect(screen.getByRole("heading", { name: "File operations" })).toBeTruthy();
    fireEvent.click(screen.getByRole("button", { name: "Remove history" }));
    expect(
      [...sent].reverse().find((message) => message.type === "file.job.control"),
    ).toMatchObject({
      type: "file.job.control",
      jobId: "job-failed",
      action: "dismiss",
    });
  });

  it("places capitalized conflict actions below the conflict message", () => {
    const sent: ClientMessage[] = [];
    renderWorkspace(sent);
    openSession(sent, [], []);
    act(() =>
      publishFileManagerResult({
        type: "file.jobs.status",
        jobs: [job("job-upload", "upload", "needs-attention")],
      }),
    );

    fireEvent.click(screen.getByRole("button", { name: /^Upload · Needs attention/u }));
    const message = screen.getByText("image.jpg already exists.");
    const actions = message.nextElementSibling;
    expect(actions?.className).toBe("file-conflict-actions");
    expect(actions?.textContent).toBe("ReplaceKeep bothCancel");
  });
});

function renderWorkspace(sent: ClientMessage[], onMirrorView = vi.fn()) {
  return render(
    <FileManagerWorkspace
      capability={{
        canBrowse: true,
        canModify: true,
        hidesProtectedSystemItems: true,
        maxPageSize: 100,
      }}
      canMirrorView
      connectionEpoch={1}
      mirrorViewUnavailableMessage="PC Screen unavailable."
      onMirrorView={onMirrorView}
      send={(message) => sent.push(message)}
      state="paired"
    />,
  );
}

function openSession(
  sent: ClientMessage[],
  leftEntries: FileManagerEntry[],
  rightEntries: FileManagerEntry[],
) {
  const open = [...sent].reverse().find((message) => message.type === "file.session.open");
  if (open?.type !== "file.session.open") {
    throw new Error("Expected Files to open a session.");
  }
  const panelPage = (
    panel: "left" | "right",
    panelEntries: FileManagerEntry[],
  ): FileManagerPanelPage => ({
    panel,
    revision: `${panel}-revision`,
    displayPath: panel === "left" ? "Downloads" : "Documents",
    parentId: `${panel}-parent`,
    driveId: "drive-a",
    sortBy: "name",
    descending: false,
    totalCount: panelEntries.length,
    entries: panelEntries,
    continuation: null,
  });
  act(() =>
    publishFileManagerResult({
      type: "file.session.open.result",
      operationId: open.operationId,
      succeeded: true,
      message: "Opened.",
      session: {
        sessionId: "session-a",
        drives: [{ id: "drive-a", label: "C:", driveType: "fixed" }],
        shortcuts: [{ id: "downloads-a", label: "Downloads" }],
        left: panelPage("left", leftEntries),
        right: panelPage("right", rightEntries),
      },
    }),
  );
}

function job(
  jobId: string,
  operation: FileJobSnapshot["operation"],
  state: FileJobSnapshot["state"],
): FileJobSnapshot {
  return {
    jobId,
    operation,
    state,
    queuePosition: 0,
    itemsCompleted: state === "completed" ? 1 : 0,
    itemsTotal: 1,
    bytesCompleted: state === "completed" ? 12 : 0,
    bytesTotal: 12,
    message: state === "failed" ? "Copy failed." : "Working.",
    conflictName: state === "needs-attention" ? "image.jpg" : null,
    canPause: state === "running",
    canResume: false,
    canCancel: state === "running" || state === "needs-attention",
  };
}
