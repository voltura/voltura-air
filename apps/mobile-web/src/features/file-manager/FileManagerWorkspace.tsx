import { useCallback, useEffect, useRef, useState } from "react";
import type {
  MouseEvent as ReactMouseEvent,
  PointerEvent as ReactPointerEvent,
  ReactNode,
} from "react";
import {
  ArrowDown,
  ArrowUp,
  CheckCheck,
  CircleCheck,
  Clipboard,
  ClipboardPaste,
  Copy,
  Eye,
  File,
  FileArchive,
  FileQuestion,
  Files,
  Folder,
  FolderInput,
  FolderOpen,
  FolderOutput,
  HardDrive,
  Info,
  Pause,
  Play,
  RefreshCw,
  Scissors,
  SkipForward,
  Trash2,
  TriangleAlert,
  Type,
  X,
} from "lucide-react";
import type { ConnectionState } from "../../foundation/connection/connectionTypes";
import type { PcProfile } from "../../foundation/connection/pcProfiles";
import { subscribeFileManagerResults } from "../../foundation/connection/fileManagerResultBus";
import type {
  ClientMessage,
  FileJobSnapshot,
  FileManagerCapability,
  FileManagerEntry,
  FileManagerPanelPage,
  FileManagerProperties,
  FileManagerSession,
  FileSelectionMessageFields,
} from "../../foundation/protocol/messages";
import { ModalDialog } from "../../ui/overlays/ModalDialog";
import { ConfirmationDialog } from "../../ui/overlays/ConfirmationDialog";
import { createLocalId } from "../../foundation/identity/localId";
import { FileTransferMenu } from "./FileTransferMenu";
import "./file-manager.css";

type PanelName = "left" | "right";
interface SelectionState {
  all: boolean;
  ids: Set<string>;
  excluded: Set<string>;
}
interface PanelView {
  page: FileManagerPanelPage;
  entries: FileManagerEntry[];
  loadingMore: boolean;
  pageError: string;
}
interface PendingPanelRequest {
  panel: PanelName;
  kind: "page" | "replace";
  revision: string;
  selectName?: string;
  selectionPagesRemaining?: number;
  selectionGeneration?: number;
}
interface TrackedJob {
  operation: "copy" | "move" | "paste" | "rename" | "delete";
  sourcePanel: PanelName;
  destinationPanel?: PanelName;
}
interface ConfirmedSelection {
  sessionId: string;
  panel: PanelName;
  revision: string;
  fields: FileSelectionMessageFields;
  count: number;
}
interface RenameTarget extends ConfirmedSelection {
  entryId: string;
  originalName: string;
  name: string;
}

interface Props {
  activePc?: PcProfile;
  capability: FileManagerCapability;
  clientId?: string;
  canMirrorView: boolean;
  connectionEpoch: number;
  mirrorViewUnavailableMessage: string;
  onMirrorView: () => void;
  send: (message: ClientMessage) => void;
  state: ConnectionState;
}

const emptySelection = (): SelectionState => ({ all: false, ids: new Set(), excluded: new Set() });
const maximumUploadSelectionContinuationPages = 4;
const formatFileJobLabel = (
  value: FileJobSnapshot["operation"] | FileJobSnapshot["state"],
): string => `${value.charAt(0).toUpperCase()}${value.slice(1).replaceAll("-", " ")}`;

export default function FileManagerWorkspace({
  activePc,
  capability,
  canMirrorView,
  clientId,
  connectionEpoch,
  mirrorViewUnavailableMessage,
  onMirrorView,
  send,
  state,
}: Props) {
  const [session, setSession] = useState<FileManagerSession | null>(null);
  const [panels, setPanels] = useState<Record<PanelName, PanelView> | null>(null);
  const [activePanel, setActivePanel] = useState<PanelName>("left");
  const [selections, setSelections] = useState<Record<PanelName, SelectionState>>({
    left: emptySelection(),
    right: emptySelection(),
  });
  const [status, setStatus] = useState("Opening Files…");
  const [statusTone, setStatusTone] = useState<"neutral" | "success" | "error">("neutral");
  const [properties, setProperties] = useState<FileManagerProperties | null>(null);
  const [jobs, setJobs] = useState<FileJobSnapshot[]>([]);
  const [operationsOpen, setOperationsOpen] = useState(false);
  const [shortcutsOpen, setShortcutsOpen] = useState(false);
  const [clipboardOpen, setClipboardOpen] = useState(false);
  const [transferPresented, setTransferPresented] = useState(false);
  const [deleteTarget, setDeleteTarget] = useState<ConfirmedSelection | null>(null);
  const [renameTarget, setRenameTarget] = useState<RenameTarget | null>(null);
  const [dualPanel, setDualPanel] = useState(false);
  const stageRef = useRef<HTMLDivElement>(null);
  const sessionRequestRef = useRef("");
  const sessionRef = useRef<FileManagerSession | null>(null);
  const panelsRef = useRef<Record<PanelName, PanelView> | null>(null);
  const pendingPanelRequests = useRef(new Map<string, PendingPanelRequest>());
  const latestReplacementRequest = useRef<Record<PanelName, string>>({ left: "", right: "" });
  const selectionLookupGeneration = useRef<Record<PanelName, number>>({ left: 0, right: 0 });
  const pendingJobRequests = useRef(new Map<string, TrackedJob>());
  const pendingMirrorOpenRef = useRef("");
  const pendingPropertiesRef = useRef("");
  const trackedJobs = useRef(new Map<string, TrackedJob>());
  const terminalJobEffects = useRef(new Set<string>());
  const jobsRef = useRef<FileJobSnapshot[]>([]);

  useEffect(() => {
    sessionRef.current = session;
  }, [session]);
  useEffect(() => {
    panelsRef.current = panels;
  }, [panels]);
  const refreshPanel = useCallback(
    (panel: PanelName, selectName?: string) => {
      const currentSession = sessionRef.current;
      const currentPanels = panelsRef.current;
      if (!currentSession || !currentPanels) {
        return;
      }
      const operationId = createLocalId();
      latestReplacementRequest.current[panel] = operationId;
      const selectionGeneration = ++selectionLookupGeneration.current[panel];
      pendingPanelRequests.current.set(operationId, {
        panel,
        kind: "replace",
        revision: currentPanels[panel].page.revision,
        ...(selectName
          ? {
              selectName,
              selectionPagesRemaining: maximumUploadSelectionContinuationPages,
              selectionGeneration,
            }
          : {}),
      });
      send({ type: "file.refresh", operationId, sessionId: currentSession.sessionId, panel });
    },
    [send],
  );
  const handleTransferNotice = useCallback(
    (message: string, tone: "success" | "error" | "neutral") => {
      setStatus(message);
      setStatusTone(tone);
    },
    [],
  );
  const handleUploadCompleted = useCallback(
    (panel: PanelName, fileName: string) => {
      setActivePanel(panel);
      refreshPanel(panel, fileName);
    },
    [refreshPanel],
  );
  useEffect(() => {
    const stage = stageRef.current;
    if (!stage) {
      return;
    }
    const updateLayout = () => {
      const nextDualPanel = stage.clientWidth >= 640;
      setDualPanel(nextDualPanel);
      if (!nextDualPanel) {
        setClipboardOpen(false);
      }
    };
    const observer = new ResizeObserver(updateLayout);
    updateLayout();
    observer.observe(stage);
    return () => observer.disconnect();
  }, [session]);
  useEffect(() => {
    if (state !== "paired" || !capability.canBrowse) {
      return;
    }
    const operationId = createLocalId();
    const panelRequests = pendingPanelRequests.current;
    const jobRequests = pendingJobRequests.current;
    const currentTrackedJobs = trackedJobs.current;
    const currentTerminalJobEffects = terminalJobEffects.current;
    sessionRequestRef.current = operationId;
    send({ type: "file.session.open", operationId });
    send({ type: "file.jobs.get", operationId: createLocalId() });
    return () => {
      sessionRef.current = null;
      panelsRef.current = null;
      panelRequests.clear();
      pendingPropertiesRef.current = "";
      pendingMirrorOpenRef.current = "";
      jobRequests.clear();
      currentTrackedJobs.clear();
      currentTerminalJobEffects.clear();
      jobsRef.current = [];
      queueMicrotask(() => {
        setSession(null);
        setPanels(null);
        setSelections({ left: emptySelection(), right: emptySelection() });
        setProperties(null);
        setDeleteTarget(null);
        setRenameTarget(null);
        setJobs([]);
      });
    };
  }, [capability.canBrowse, connectionEpoch, send, state]);

  useEffect(() => {
    const applyTerminalJobEffects = (job: FileJobSnapshot) => {
      const tracked = trackedJobs.current.get(job.jobId);
      if (!tracked || !isTerminalJob(job.state) || terminalJobEffects.current.has(job.jobId)) {
        return;
      }
      terminalJobEffects.current.add(job.jobId);
      trackedJobs.current.delete(job.jobId);
      const completed = job.state === "completed";
      setStatus(
        job.message ??
          (completed ? `${job.operation} completed.` : `${job.operation} did not complete.`),
      );
      setStatusTone(completed ? "success" : job.state === "canceled" ? "neutral" : "error");
      if (completed && tracked.operation === "copy") {
        setSelections((current) => ({ ...current, [tracked.sourcePanel]: emptySelection() }));
      }
      const refreshPanels = new Set<PanelName>();
      if (
        tracked.operation === "move" ||
        tracked.operation === "delete" ||
        tracked.operation === "rename" ||
        tracked.operation === "paste"
      ) {
        refreshPanels.add(tracked.sourcePanel);
      }
      if (tracked.operation === "copy" || tracked.operation === "move") {
        refreshPanels.add(tracked.destinationPanel!);
      }
      refreshPanels.forEach(refreshPanel);
    };
    return subscribeFileManagerResults((message) => {
      if (message.type === "file.session.open.result") {
        if (message.operationId !== sessionRequestRef.current) {
          return;
        }
        if (!message.succeeded || !message.session) {
          setStatus(message.message);
          setStatusTone("error");
          return;
        }
        if (!isSemanticallyValidSession(message.session)) {
          setStatus("The PC returned an invalid file session.");
          setStatusTone("error");
          return;
        }
        const openedPanels = {
          left: {
            page: message.session.left,
            entries: message.session.left.entries,
            loadingMore: false,
            pageError: "",
          },
          right: {
            page: message.session.right,
            entries: message.session.right.entries,
            loadingMore: false,
            pageError: "",
          },
        };
        setSession(message.session);
        panelsRef.current = openedPanels;
        setPanels(openedPanels);
        setStatus("Files ready.");
        setStatusTone("neutral");
        return;
      }
      if (
        message.type === "file.page.get.result" ||
        message.type === "file.navigate.result" ||
        message.type === "file.refresh.result" ||
        message.type === "file.sort.result"
      ) {
        const pending = pendingPanelRequests.current.get(message.operationId);
        if (!pending) {
          return;
        }
        pendingPanelRequests.current.delete(message.operationId);
        if (
          pending.selectName &&
          pending.selectionGeneration !== selectionLookupGeneration.current[pending.panel]
        ) {
          return;
        }
        if (
          pending.kind === "replace" &&
          latestReplacementRequest.current[pending.panel] !== message.operationId
        ) {
          return;
        }
        if (!message.succeeded || !message.page) {
          setStatus(message.message);
          setStatusTone("error");
          setPanels((current) =>
            current
              ? {
                  ...current,
                  [pending.panel]: {
                    ...current[pending.panel],
                    loadingMore: false,
                    pageError:
                      pending.kind === "page" ? message.message : current[pending.panel].pageError,
                  },
                }
              : current,
          );
          if (message.code === "stale-panel") {
            const currentSession = sessionRef.current;
            if (currentSession) {
              const refreshId = createLocalId();
              latestReplacementRequest.current[pending.panel] = refreshId;
              pendingPanelRequests.current.set(refreshId, {
                panel: pending.panel,
                kind: "replace",
                revision: "",
                ...(pending.selectName
                  ? {
                      selectName: pending.selectName,
                      selectionPagesRemaining:
                        pending.selectionPagesRemaining ?? maximumUploadSelectionContinuationPages,
                      selectionGeneration: pending.selectionGeneration,
                    }
                  : {}),
              });
              send({
                type: "file.refresh",
                operationId: refreshId,
                sessionId: currentSession.sessionId,
                panel: pending.panel,
              });
            }
          }
          return;
        }
        const page = message.page;
        const currentPanel = panelsRef.current?.[pending.panel];
        const currentRevision = currentPanel?.page.revision;
        const invalidAppend =
          pending.kind === "page" &&
          (currentRevision === undefined ||
            pending.revision !== currentRevision ||
            page.revision !== currentRevision ||
            hasOverlappingIds(currentPanel?.entries ?? [], page.entries));
        if (page.panel !== pending.panel || !hasUniqueIds(page.entries) || invalidAppend) {
          setStatus("The PC returned an invalid file page.");
          setStatusTone("error");
          setPanels((current) =>
            current
              ? {
                  ...current,
                  [pending.panel]: {
                    ...current[pending.panel],
                    loadingMore: false,
                    pageError: "Invalid file page.",
                  },
                }
              : current,
          );
          return;
        }
        setPanels((current) => {
          if (!current) {
            return current;
          }
          const existing = current[page.panel];
          const append =
            pending.kind === "page" &&
            pending.revision === existing.page.revision &&
            existing.page.revision === page.revision;
          if (pending.kind === "page" && !append) {
            return current;
          }
          const next = {
            ...current,
            [page.panel]: {
              page,
              entries: append ? [...existing.entries, ...page.entries] : page.entries,
              loadingMore: false,
              pageError: "",
            },
          };
          panelsRef.current = next;
          return next;
        });
        const selected = pending.selectName
          ? page.entries.find((entry) => entry.name === pending.selectName)
          : undefined;
        if (selected || pending.kind === "replace") {
          setSelections((current) => ({
            ...current,
            [page.panel]: selected
              ? { all: false, ids: new Set([selected.id]), excluded: new Set() }
              : emptySelection(),
          }));
        }
        const currentSession = sessionRef.current;
        const selectionPagesRemaining = pending.selectionPagesRemaining ?? 0;
        if (
          currentSession &&
          pending.selectName &&
          !selected &&
          page.continuation &&
          selectionPagesRemaining > 0
        ) {
          const nextPageId = createLocalId();
          pendingPanelRequests.current.set(nextPageId, {
            panel: page.panel,
            kind: "page",
            revision: page.revision,
            selectName: pending.selectName,
            selectionPagesRemaining: selectionPagesRemaining - 1,
            selectionGeneration:
              pending.selectionGeneration ?? selectionLookupGeneration.current[page.panel],
          });
          send({
            type: "file.page.get",
            operationId: nextPageId,
            sessionId: currentSession.sessionId,
            panel: page.panel,
            revision: page.revision,
            continuation: page.continuation,
          });
        }
        const selectionNotLocated =
          pending.selectName && !selected && (!page.continuation || selectionPagesRemaining === 0);
        setStatus(
          selectionNotLocated
            ? "File copied to the PC. It is outside the loaded rows."
            : message.message,
        );
        setStatusTone(selectionNotLocated ? "success" : "neutral");
        return;
      }
      if (message.type === "file.properties.get.result") {
        if (message.operationId !== pendingPropertiesRef.current) {
          return;
        }
        pendingPropertiesRef.current = "";
        if (message.succeeded && message.properties) {
          setProperties(message.properties);
        }
        setStatus(message.message);
        setStatusTone(message.succeeded ? "success" : "error");
        return;
      }
      if (message.type === "file.jobs.status") {
        jobsRef.current = message.jobs;
        setJobs(message.jobs);
        message.jobs.forEach((job) => {
          applyTerminalJobEffects(job);
        });
        return;
      }
      if (message.type === "file.job.create.result" && message.job) {
        const tracked = pendingJobRequests.current.get(message.operationId);
        pendingJobRequests.current.delete(message.operationId);
        if (tracked) {
          trackedJobs.current.set(message.job.jobId, tracked);
        }
        const newest =
          jobsRef.current.find((job) => job.jobId === message.job!.jobId) ?? message.job;
        jobsRef.current = [newest, ...jobsRef.current.filter((job) => job.jobId !== newest.jobId)];
        setJobs(jobsRef.current);
        applyTerminalJobEffects(newest);
        setStatus(message.message);
        setStatusTone("success");
        return;
      }
      if (message.type === "file.job.create.result") {
        const tracked = pendingJobRequests.current.get(message.operationId);
        pendingJobRequests.current.delete(message.operationId);
        if (message.code === "stale-panel" && tracked) {
          const currentSession = sessionRef.current;
          if (currentSession) {
            const refreshId = createLocalId();
            latestReplacementRequest.current[tracked.sourcePanel] = refreshId;
            pendingPanelRequests.current.set(refreshId, {
              panel: tracked.sourcePanel,
              kind: "replace",
              revision: "",
            });
            send({
              type: "file.refresh",
              operationId: refreshId,
              sessionId: currentSession.sessionId,
              panel: tracked.sourcePanel,
            });
          }
        }
      }
      if (
        message.type === "file.open.result" &&
        message.operationId === pendingMirrorOpenRef.current
      ) {
        pendingMirrorOpenRef.current = "";
        setStatus(message.message);
        setStatusTone(message.succeeded ? "success" : "error");
        if (message.succeeded) {
          onMirrorView();
        }
        return;
      }
      if (
        message.type.startsWith("file.transfer.") ||
        !("message" in message) ||
        !("succeeded" in message)
      ) {
        return;
      }
      setStatus(message.message);
      setStatusTone(message.succeeded ? "success" : "error");
    });
  }, [onMirrorView, refreshPanel, send]);

  const sendPanel = (panel: PanelName, targetId: string) => {
    if (!session || !panels) {
      return;
    }
    setActivePanel(panel);
    selectionLookupGeneration.current[panel]++;
    const operationId = createLocalId();
    latestReplacementRequest.current[panel] = operationId;
    pendingPanelRequests.current.set(operationId, {
      panel,
      kind: "replace",
      revision: panels[panel].page.revision,
    });
    send({
      type: "file.navigate",
      operationId,
      sessionId: session.sessionId,
      panel,
      revision: panels[panel].page.revision,
      targetId,
    });
  };

  const loadMore = (panel: PanelName) => {
    if (!session || !panels) {
      return;
    }
    const view = panels[panel];
    if (!view.page.continuation || view.loadingMore) {
      return;
    }
    setPanels((current) =>
      current
        ? { ...current, [panel]: { ...current[panel], loadingMore: true, pageError: "" } }
        : current,
    );
    const operationId = createLocalId();
    pendingPanelRequests.current.set(operationId, {
      panel,
      kind: "page",
      revision: view.page.revision,
    });
    send({
      type: "file.page.get",
      operationId,
      sessionId: session.sessionId,
      panel,
      revision: view.page.revision,
      continuation: view.page.continuation,
    });
  };

  const toggleEntry = (panel: PanelName, entryId: string) => {
    setActivePanel(panel);
    selectionLookupGeneration.current[panel]++;
    setSelections((current) => {
      const selection = current[panel];
      if (selection.all) {
        const excluded = new Set(selection.excluded);
        if (excluded.has(entryId)) {
          excluded.delete(entryId);
        } else {
          excluded.add(entryId);
        }
        return { ...current, [panel]: { ...selection, excluded } };
      }
      const ids = new Set(selection.ids);
      if (ids.has(entryId)) {
        ids.delete(entryId);
      } else {
        ids.add(entryId);
      }
      return { ...current, [panel]: { ...selection, ids } };
    });
  };
  const selectEntry = (panel: PanelName, entryId: string) => {
    setActivePanel(panel);
    selectionLookupGeneration.current[panel]++;
    setSelections((current) => ({
      ...current,
      [panel]: { all: false, ids: new Set([entryId]), excluded: new Set() },
    }));
  };

  const selectionFields = (panel: PanelName): FileSelectionMessageFields => {
    const selection = selections[panel];
    return {
      selectionAll: selection.all,
      entryIds: [...selection.ids],
      excludedEntryIds: [...selection.excluded],
    };
  };
  const selectedCount = (panel: PanelName) => {
    if (!panels) {
      return 0;
    }
    const selection = selections[panel];
    return selection.all
      ? Math.max(0, panels[panel].page.totalCount - selection.excluded.size)
      : selection.ids.size;
  };
  const singleSelected = (panel: PanelName) => {
    if (!panels || selections[panel].all || selections[panel].ids.size !== 1) {
      return null;
    }
    const id = [...selections[panel].ids][0];
    return panels[panel].entries.find((entry) => entry.id === id) ?? null;
  };

  const setClipboard = (effect: "copy" | "move") => {
    if (!session || !panels || selectedCount(activePanel) === 0) {
      return;
    }
    send({
      type: "file.clipboard.set",
      operationId: createLocalId(),
      sessionId: session.sessionId,
      panel: activePanel,
      revision: panels[activePanel].page.revision,
      effect,
      ...selectionFields(activePanel),
    });
  };
  const createJob = (
    operation: "copy" | "move" | "paste" | "rename" | "delete",
    newName?: string,
    confirmed?: ConfirmedSelection,
  ) => {
    if (!session || !panels) {
      return;
    }
    if (confirmed && confirmed.sessionId !== session.sessionId) {
      setStatus("The file session changed. Select the item again.");
      setStatusTone("error");
      return;
    }
    const sourcePanel = confirmed?.panel ?? activePanel;
    const sourceRevision = confirmed?.revision ?? panels[sourcePanel].page.revision;
    const destination =
      operation === "copy" || operation === "move"
        ? (() => {
            const destinationPanel =
              sourcePanel === "left" ? ("right" as const) : ("left" as const);
            return {
              destinationPanel,
              destinationRevision: panels[destinationPanel].page.revision,
            };
          })()
        : {};
    const rename = newName === undefined ? {} : { newName };
    const operationId = createLocalId();
    pendingJobRequests.current.set(operationId, {
      operation,
      sourcePanel,
      ...(operation === "copy" || operation === "move"
        ? { destinationPanel: sourcePanel === "left" ? ("right" as const) : ("left" as const) }
        : {}),
    });
    send({
      type: "file.job.create",
      operationId,
      sessionId: session.sessionId,
      panel: sourcePanel,
      revision: sourceRevision,
      operation,
      ...destination,
      ...rename,
      ...(operation === "paste"
        ? { selectionAll: true, entryIds: [], excludedEntryIds: [] }
        : (confirmed?.fields ?? selectionFields(sourcePanel))),
    });
  };
  const openSelected = () => {
    const entry = singleSelected(activePanel);
    if (!entry || !session || !panels) {
      return;
    }
    send({
      type: "file.open",
      operationId: createLocalId(),
      sessionId: session.sessionId,
      panel: activePanel,
      revision: panels[activePanel].page.revision,
      entryId: entry.id,
    });
  };
  const viewSelected = () => {
    const entry = singleSelected(activePanel);
    if (!entry || !session || !panels) {
      return;
    }
    if (!canMirrorView) {
      setStatus(mirrorViewUnavailableMessage);
      setStatusTone("error");
      return;
    }
    const operationId = createLocalId();
    pendingMirrorOpenRef.current = operationId;
    setStatus("Opening the file on the PC before starting the mirror…");
    setStatusTone("neutral");
    send({
      type: "file.open",
      operationId,
      sessionId: session.sessionId,
      panel: activePanel,
      revision: panels[activePanel].page.revision,
      entryId: entry.id,
    });
  };
  const getProperties = (panel = activePanel, entryId = singleSelected(panel)?.id) => {
    if (!entryId || !session || !panels) {
      return;
    }
    setActivePanel(panel);
    const operationId = createLocalId();
    pendingPropertiesRef.current = operationId;
    send({
      type: "file.properties.get",
      operationId,
      sessionId: session.sessionId,
      panel,
      revision: panels[panel].page.revision,
      entryId,
    });
  };

  if (!capability.canBrowse) {
    return (
      <section className="file-manager-unavailable">
        <Files aria-hidden="true" />
        <h2>Files needs permission</h2>
        <p>Enable Browse and open files for this device on the PC.</p>
      </section>
    );
  }
  if (!session || !panels) {
    return <div className="workspace-loading">{status}</div>;
  }

  const selected = selectedCount(activePanel);
  const single = singleSelected(activePanel);
  const activeJobs = jobs.filter(
    (job) => !["completed", "failed", "canceled", "interrupted"].includes(job.state),
  );
  const leadingJob = activeJobs[0];
  const terminalJobCount = jobs.filter((job) => isTerminalJob(job.state)).length;
  const dismissTransientMenus = (target: EventTarget | null) => {
    if (
      (target as HTMLElement | null)?.closest(
        ".file-toolbar-menu, .file-shortcuts-menu, [data-file-menu-trigger]",
      )
    ) {
      return;
    }
    setClipboardOpen(false);
    setShortcutsOpen(false);
  };
  return (
    <section
      className={`file-manager-workspace ${leadingJob ? "job-bar-presented" : ""}`}
      aria-label="Files on PC"
    >
      <div ref={stageRef} className="file-manager-stage">
        <div
          className="file-manager-content"
          onPointerDownCapture={(event) => dismissTransientMenus(event.target)}
          onClickCapture={(event) => dismissTransientMenus(event.target)}
        >
          <FileToolbar
            canModify={capability.canModify}
            dualPanel={dualPanel}
            selected={selected}
            single={single}
            transfer={
              capability.canTransfer === true && activePc && clientId ? (
                <FileTransferMenu
                  activePc={activePc}
                  canModify={capability.canModify}
                  clientId={clientId}
                  enabled={state === "paired" && capability.canTransfer === true}
                  onPresentationChange={setTransferPresented}
                  onTransferNotice={handleTransferNotice}
                  onUploadCompleted={handleUploadCompleted}
                  send={send}
                  target={{
                    sessionId: session.sessionId,
                    panel: activePanel,
                    revision: panels[activePanel].page.revision,
                    entry: single,
                  }}
                />
              ) : null
            }
            onSelectAll={() => {
              selectionLookupGeneration.current[activePanel]++;
              setSelections((current) => ({
                ...current,
                [activePanel]: { all: true, ids: new Set(), excluded: new Set() },
              }));
            }}
            onUnselectAll={() => {
              selectionLookupGeneration.current[activePanel]++;
              setSelections((current) => ({ ...current, [activePanel]: emptySelection() }));
            }}
            onCut={() => setClipboard("move")}
            onCopyClipboard={() => setClipboard("copy")}
            onPaste={() => createJob("paste")}
            onProperties={() => getProperties()}
            onDelete={() =>
              setDeleteTarget({
                sessionId: session.sessionId,
                panel: activePanel,
                revision: panels[activePanel].page.revision,
                fields: selectionFields(activePanel),
                count: selected,
              })
            }
            onRename={() => {
              if (single) {
                setRenameTarget({
                  sessionId: session.sessionId,
                  panel: activePanel,
                  revision: panels[activePanel].page.revision,
                  fields: { selectionAll: false, entryIds: [single.id], excludedEntryIds: [] },
                  count: 1,
                  entryId: single.id,
                  originalName: single.name,
                  name: single.name,
                });
              }
            }}
            onView={viewSelected}
            onOpen={openSelected}
            onCopy={() => createJob("copy")}
            onMove={() => createJob("move")}
            onClipboard={() => {
              setClipboardOpen((open) => !open);
              setShortcutsOpen(false);
            }}
            onShortcuts={() => {
              setShortcutsOpen((open) => !open);
              setClipboardOpen(false);
            }}
          />
          {clipboardOpen && (
            <div
              className="file-toolbar-menu file-clipboard-menu"
              role="menu"
              aria-label="Windows file clipboard"
            >
              <button
                role="menuitem"
                onClick={() => {
                  setClipboard("move");
                  setClipboardOpen(false);
                }}
                disabled={!capability.canModify || selected === 0}
              >
                <Scissors />
                Cut to clipboard
              </button>
              <button
                role="menuitem"
                onClick={() => {
                  setClipboard("copy");
                  setClipboardOpen(false);
                }}
                disabled={selected === 0}
              >
                <Clipboard />
                Copy to clipboard
              </button>
            </div>
          )}
          {shortcutsOpen && (
            <div className="file-shortcuts-menu" role="menu" aria-label="File shortcuts">
              {session.shortcuts.map((shortcut) => (
                <button
                  key={shortcut.id}
                  role="menuitem"
                  onClick={() => {
                    sendPanel(activePanel, shortcut.id);
                    setShortcutsOpen(false);
                  }}
                >
                  <FolderOpen />
                  {shortcut.label}
                </button>
              ))}
            </div>
          )}
          <div className="file-manager-panels">
            {(["left", "right"] as const).map((panel) => (
              <FilePanel
                key={panel}
                active={activePanel === panel}
                drives={session.drives}
                panel={panels[panel]}
                selection={selections[panel]}
                onActivate={() => setActivePanel(panel)}
                onDrive={(id) => sendPanel(panel, id)}
                onLoadMore={() => loadMore(panel)}
                onNavigate={(id) => sendPanel(panel, id)}
                onProperties={(entryId) => getProperties(panel, entryId)}
                onRefresh={() => refreshPanel(panel)}
                onSort={(sortBy) => {
                  selectionLookupGeneration.current[panel]++;
                  const operationId = createLocalId();
                  latestReplacementRequest.current[panel] = operationId;
                  pendingPanelRequests.current.set(operationId, {
                    panel,
                    kind: "replace",
                    revision: panels[panel].page.revision,
                  });
                  send({
                    type: "file.sort",
                    operationId,
                    sessionId: session.sessionId,
                    panel,
                    sortBy,
                    descending:
                      panels[panel].page.sortBy === sortBy && !panels[panel].page.descending,
                  });
                }}
                onSelect={(id) => selectEntry(panel, id)}
                onToggle={(id) => toggleEntry(panel, id)}
              />
            ))}
          </div>
        </div>
      </div>
      <div
        className={`file-status ${statusTone}`}
        role={statusTone === "error" ? "alert" : "status"}
      >
        {statusTone === "error" ? (
          <TriangleAlert />
        ) : statusTone === "success" ? (
          <CircleCheck />
        ) : (
          <Info />
        )}
        <span>{status}</span>
      </div>
      {leadingJob && (
        <button className="file-job-minimized" onClick={() => setOperationsOpen(true)}>
          <span>
            {formatFileJobLabel(leadingJob.operation)} ·{" "}
            {leadingJob.currentName ?? formatFileJobLabel(leadingJob.state)}
          </span>
          <progress
            max={Math.max(1, leadingJob.bytesTotal || leadingJob.itemsTotal)}
            value={leadingJob.bytesTotal ? leadingJob.bytesCompleted : leadingJob.itemsCompleted}
          />
        </button>
      )}
      {!transferPresented && !leadingJob && terminalJobCount > 0 && (
        <button
          className="file-job-minimized file-job-history"
          onClick={() => setOperationsOpen(true)}
        >
          <span>File operations · {terminalJobCount} in history</span>
        </button>
      )}
      {operationsOpen && (
        <OperationCenter jobs={jobs} onClose={() => setOperationsOpen(false)} send={send} />
      )}
      {properties && <PropertiesDialog value={properties} onClose={() => setProperties(null)} />}
      <ConfirmationDialog
        confirmLabel="Move to Recycle Bin"
        description={`Move ${deleteTarget?.count ?? 0} selected item${deleteTarget?.count === 1 ? "" : "s"} to the Windows Recycle Bin? The PC rejects the operation if every item cannot be recycled.`}
        isOpen={deleteTarget !== null}
        onCancel={() => setDeleteTarget(null)}
        onConfirm={() => {
          const target = deleteTarget;
          setDeleteTarget(null);
          if (target) {
            createJob("delete", undefined, target);
          }
        }}
        title="Delete selected items?"
      />
      <ModalDialog
        dismissLabel="Cancel"
        isOpen={renameTarget !== null}
        onClose={() => setRenameTarget(null)}
        onSubmit={(event) => {
          event.preventDefault();
          const nextName = renameTarget?.name.trim();
          if (!nextName || !renameTarget) {
            return false;
          }
          if (nextName === renameTarget.originalName) {
            return false;
          }
          createJob("rename", nextName, renameTarget);
          setRenameTarget(null);
          return true;
        }}
        submitLabel="Rename"
        title="Rename item"
      >
        <label className="file-rename-field">
          New name
          <input
            className="text-input"
            value={renameTarget?.name ?? ""}
            maxLength={255}
            onChange={(event) =>
              setRenameTarget((current) =>
                current ? { ...current, name: event.target.value } : current,
              )
            }
          />
        </label>
      </ModalDialog>
    </section>
  );
}

function FileToolbar(props: {
  canModify: boolean;
  dualPanel: boolean;
  selected: number;
  single: FileManagerEntry | null;
  transfer: ReactNode;
  onSelectAll: () => void;
  onUnselectAll: () => void;
  onCut: () => void;
  onCopyClipboard: () => void;
  onPaste: () => void;
  onProperties: () => void;
  onDelete: () => void;
  onRename: () => void;
  onView: () => void;
  onOpen: () => void;
  onCopy: () => void;
  onMove: () => void;
  onClipboard: () => void;
  onShortcuts: () => void;
}) {
  const action = (
    label: string,
    Icon: typeof Copy,
    run: () => void,
    disabled = false,
    menuTrigger = false,
  ) => (
    <button
      type="button"
      aria-label={label}
      title={label}
      data-file-menu-trigger={menuTrigger || undefined}
      disabled={disabled}
      onClick={run}
    >
      <Icon />
      <span>{label}</span>
    </button>
  );
  return (
    <div
      className={`file-toolbar ${props.dualPanel ? "dual-panel" : "single-panel"}`}
      aria-label="File actions"
    >
      {action("Select all", CheckCheck, props.onSelectAll)}
      {action("Unselect all", X, props.onUnselectAll, props.selected === 0)}
      {props.dualPanel ? (
        <>
          {action("Copy", FolderOutput, props.onCopy, !props.canModify || props.selected === 0)}
          {action("Move", FolderInput, props.onMove, !props.canModify || props.selected === 0)}
        </>
      ) : (
        <>
          {action("Cut", Scissors, props.onCut, !props.canModify || props.selected === 0)}
          {action("Copy", Clipboard, props.onCopyClipboard, props.selected === 0)}
        </>
      )}
      {action("Paste", ClipboardPaste, props.onPaste, !props.canModify)}
      {action("Properties", Info, props.onProperties, !props.single)}
      {action("Delete", Trash2, props.onDelete, !props.canModify || props.selected === 0)}
      {action("Rename", Type, props.onRename, !props.canModify || !props.single)}
      {action("View", Eye, props.onView, props.single?.kind !== "file")}
      {action("Open", FolderOpen, props.onOpen, !props.single)}
      {props.dualPanel && action("Clipboard", Clipboard, props.onClipboard, false, true)}
      {props.transfer}
      {action("Locations", HardDrive, props.onShortcuts, false, true)}
    </div>
  );
}

function FilePanel({
  active,
  drives,
  panel,
  selection,
  onActivate,
  onDrive,
  onLoadMore,
  onNavigate,
  onProperties,
  onRefresh,
  onSelect,
  onSort,
  onToggle,
}: {
  active: boolean;
  drives: FileManagerSession["drives"];
  panel: PanelView;
  selection: SelectionState;
  onActivate: () => void;
  onDrive: (id: string) => void;
  onLoadMore: () => void;
  onNavigate: (id: string) => void;
  onProperties: (entryId: string) => void;
  onRefresh: () => void;
  onSelect: (id: string) => void;
  onSort: (sortBy: "name" | "size" | "type" | "modified") => void;
  onToggle: (id: string) => void;
}) {
  const [scrollTop, setScrollTop] = useState(0);
  const [height, setHeight] = useState(400);
  const viewportRef = useRef<HTMLDivElement>(null);
  const longPressRef = useRef<{
    timer: ReturnType<typeof setTimeout>;
    pointerId: number;
    x: number;
    y: number;
    fired: boolean;
  } | null>(null);
  const suppressClickRef = useRef(false);
  const rowHeight = 54;
  useEffect(() => {
    const viewport = viewportRef.current;
    if (!viewport) {
      return;
    }
    const observer = new ResizeObserver(() => setHeight(viewport.clientHeight));
    observer.observe(viewport);
    return () => observer.disconnect();
  }, []);
  useEffect(
    () => () => {
      if (longPressRef.current) {
        clearTimeout(longPressRef.current.timer);
      }
    },
    [],
  );
  useEffect(() => {
    if (selection.all || selection.ids.size !== 1) {
      return;
    }
    const selectedId = [...selection.ids][0];
    const index = panel.entries.findIndex((entry) => entry.id === selectedId);
    const viewport = viewportRef.current;
    if (index < 0 || !viewport) {
      return;
    }
    const top = (index + (panel.page.parentId ? 1 : 0)) * rowHeight;
    const bottom = top + rowHeight;
    if (top < viewport.scrollTop) {
      viewport.scrollTop = top;
      setScrollTop(top);
    } else if (bottom > viewport.scrollTop + viewport.clientHeight) {
      const nextTop = Math.max(0, bottom - viewport.clientHeight);
      viewport.scrollTop = nextTop;
      setScrollTop(nextTop);
    }
  }, [panel.entries, panel.page.parentId, selection]);
  const start = Math.max(0, Math.floor(scrollTop / rowHeight) - 5);
  const visible = Math.ceil(height / rowHeight) + 10;
  const parentOffset = panel.page.parentId ? 1 : 0;
  const entryStart = Math.max(0, start - parentOffset);
  const rows = panel.entries.slice(entryStart, entryStart + visible);
  const visibleAttributes = [...new Set(panel.entries.flatMap((entry) => entry.attributes))];
  useEffect(() => {
    if (
      entryStart + visible >= panel.entries.length - 10 &&
      panel.page.continuation &&
      !panel.loadingMore &&
      !panel.pageError
    ) {
      onLoadMore();
    }
  }, [
    entryStart,
    onLoadMore,
    panel.entries.length,
    panel.loadingMore,
    panel.page.continuation,
    panel.pageError,
    visible,
  ]);
  const cancelLongPress = () => {
    if (!longPressRef.current) {
      return;
    }
    clearTimeout(longPressRef.current.timer);
    longPressRef.current = null;
  };
  const beginLongPress = (event: ReactPointerEvent<HTMLElement>) => {
    if (event.pointerType === "mouse" && event.button !== 0) {
      return;
    }
    const target = (event.target as HTMLElement).closest<HTMLElement>("[data-properties-entry]");
    const entryId = target?.dataset.propertiesEntry;
    if (!entryId) {
      return;
    }
    cancelLongPress();
    const pending = {
      pointerId: event.pointerId,
      x: event.clientX,
      y: event.clientY,
      fired: false,
      timer: setTimeout(() => {
        pending.fired = true;
        suppressClickRef.current = true;
        onProperties(entryId);
      }, 550),
    };
    longPressRef.current = pending;
  };
  const moveLongPress = (event: ReactPointerEvent<HTMLElement>) => {
    const pending = longPressRef.current;
    if (pending?.pointerId !== event.pointerId) {
      return;
    }
    if (Math.hypot(event.clientX - pending.x, event.clientY - pending.y) > 10) {
      cancelLongPress();
    }
  };
  const endLongPress = (event: ReactPointerEvent<HTMLElement>) => {
    if (longPressRef.current?.pointerId === event.pointerId) {
      cancelLongPress();
    }
  };
  const cancelLongPressGesture = (event: ReactPointerEvent<HTMLElement>) => {
    if (longPressRef.current?.pointerId === event.pointerId) {
      cancelLongPress();
      suppressClickRef.current = false;
    }
  };
  const suppressLongPressClick = (event: ReactMouseEvent<HTMLElement>) => {
    if (!suppressClickRef.current) {
      return;
    }
    suppressClickRef.current = false;
    event.preventDefault();
    event.stopPropagation();
  };
  const heading = (label: string, sortBy: "name" | "size" | "type" | "modified") => (
    <button type="button" onClick={() => onSort(sortBy)}>
      {label}
      {panel.page.sortBy === sortBy ? (panel.page.descending ? " ↓" : " ↑") : ""}
    </button>
  );
  return (
    <section
      className={`file-panel ${active ? "active" : ""}`}
      onPointerDown={onActivate}
      onPointerDownCapture={beginLongPress}
      onPointerMoveCapture={moveLongPress}
      onPointerUpCapture={endLongPress}
      onPointerCancelCapture={cancelLongPressGesture}
      onClickCapture={suppressLongPressClick}
      onContextMenuCapture={(event) => {
        if ((event.target as HTMLElement).closest("[data-properties-entry]")) {
          event.preventDefault();
        }
      }}
      aria-label={`${panel.page.panel} file panel`}
    >
      <div className="file-panel-location">
        <select
          aria-label={`${panel.page.panel} drive`}
          value={panel.page.driveId ?? ""}
          onChange={(event) => onDrive(event.target.value)}
        >
          <option value="" disabled>
            Drive
          </option>
          {drives.map((drive) => (
            <option key={drive.id} value={drive.id}>
              {drive.label}
            </option>
          ))}
        </select>
        <div
          data-properties-entry="current"
          title={`${panel.page.displayPath} — long press for properties`}
        >
          {panel.page.displayPath}
        </div>
        <button
          type="button"
          className="icon-action file-panel-refresh"
          aria-label={`Refresh ${panel.page.panel} panel`}
          title="Refresh"
          onClick={onRefresh}
        >
          <RefreshCw />
        </button>
      </div>
      <div className="file-columns">
        {heading("Name", "name")}
        {heading("Size", "size")}
        {heading("Type", "type")}
        {heading("Modified", "modified")}
      </div>
      <div
        ref={viewportRef}
        className="file-list"
        onScroll={(event) => setScrollTop(event.currentTarget.scrollTop)}
      >
        <div
          className="file-list-space"
          style={{ height: (panel.entries.length + (panel.page.parentId ? 1 : 0)) * rowHeight }}
        >
          {panel.page.parentId && start === 0 && (
            <div className="file-row parent" style={{ transform: "translateY(0)" }}>
              <button
                type="button"
                title="Up one folder"
                onClick={() => onNavigate(panel.page.parentId!)}
              >
                <FolderOpen />
                <span>..</span>
              </button>
              <span />
              <span />
              <span />
            </div>
          )}
          {rows.map((entry, index) => {
            const absoluteIndex = entryStart + index + parentOffset;
            const checked = selection.all
              ? !selection.excluded.has(entry.id)
              : selection.ids.has(entry.id);
            const Icon =
              entry.kind === "folder"
                ? Folder
                : /^(zip|rar|7z|iso)$/i.exec(entry.extension)
                  ? FileArchive
                  : entry.extension
                    ? File
                    : FileQuestion;
            const modified = formatModified(entry.modifiedUtc);
            const attributeLabel = entry.attributes.map(fileAttributeLabel).join(", ");
            return (
              <div
                key={entry.id}
                data-properties-entry={entry.id}
                className={`file-row ${checked ? "selected" : ""}`}
                style={{ transform: `translateY(${absoluteIndex * rowHeight}px)` }}
              >
                <input
                  type="checkbox"
                  aria-label={`Select ${entry.name}`}
                  checked={checked}
                  onChange={() => onToggle(entry.id)}
                />
                <button
                  type="button"
                  title={entry.name}
                  onClick={() =>
                    entry.kind === "folder" ? onNavigate(entry.id) : onSelect(entry.id)
                  }
                >
                  <Icon />
                  <span>{entry.name}</span>
                  {entry.attributes.length > 0 && (
                    <small
                      className="file-attributes"
                      aria-label={attributeLabel}
                      title={attributeLabel}
                    >
                      {entry.attributes.map(fileAttributeCode).join("")}
                    </small>
                  )}
                </button>
                <span>{entry.kind === "folder" ? "DIR" : formatBytes(entry.size ?? 0)}</span>
                <span>{entry.kind === "folder" ? "Folder" : entry.extension || "File"}</span>
                <time dateTime={entry.modifiedUtc}>
                  <span>{modified.date}</span>
                  <span>{modified.time}</span>
                </time>
              </div>
            );
          })}
        </div>
        {(panel.loadingMore || panel.pageError) && (
          <div className="file-page-state">
            {panel.loadingMore ? (
              "Loading more…"
            ) : (
              <button onClick={onLoadMore}>Retry loading more</button>
            )}
          </div>
        )}
      </div>
      <footer>
        <span>
          {selection.all ? panel.page.totalCount - selection.excluded.size : selection.ids.size}{" "}
          selected · {panel.page.totalCount} items
        </span>
        {visibleAttributes.length > 0 && (
          <span className="file-attribute-legend">
            {visibleAttributes
              .map(
                (attribute) => `${fileAttributeCode(attribute)} ${fileAttributeLabel(attribute)}`,
              )
              .join(" · ")}
          </span>
        )}
      </footer>
    </section>
  );
}

const conflictResolutionLabels = {
  cancel: "Cancel",
  "keep-both": "Keep both",
  replace: "Replace",
  skip: "Skip",
} as const;

function OperationCenter({
  jobs,
  onClose,
  send,
}: {
  jobs: FileJobSnapshot[];
  onClose: () => void;
  send: (message: ClientMessage) => void;
}) {
  const [applyAll, setApplyAll] = useState<Record<string, boolean>>({});
  const [cancelMoveJobId, setCancelMoveJobId] = useState<string | null>(null);
  const terminalJobs = jobs.filter((job) => isTerminalJob(job.state));
  const control = (jobId: string, action: "pause" | "resume" | "cancel" | "dismiss") =>
    send({ type: "file.job.control", operationId: createLocalId(), jobId, action });
  return (
    <div className="file-operation-scrim">
      <section
        className="file-operation-center"
        role="dialog"
        aria-modal="true"
        aria-label="File operations"
      >
        <header>
          <h2>File operations</h2>
          <div>
            {terminalJobs.length > 0 && (
              <button onClick={() => terminalJobs.forEach((job) => control(job.jobId, "dismiss"))}>
                <Trash2 />
                Clear history
              </button>
            )}
            <button aria-label="Close file operations" onClick={onClose}>
              <X />
            </button>
          </div>
        </header>
        {jobs.length === 0 ? (
          <p>No file operations.</p>
        ) : (
          jobs.map((job) => (
            <article key={job.jobId}>
              <strong>
                {formatFileJobLabel(job.operation)} · {formatFileJobLabel(job.state)}
              </strong>
              <span>{job.currentName ?? job.message}</span>
              <progress
                max={Math.max(1, job.bytesTotal || job.itemsTotal)}
                value={job.bytesTotal ? job.bytesCompleted : job.itemsCompleted}
              />
              <small>
                {job.itemsCompleted} / {job.itemsTotal} items · {formatBytes(job.bytesCompleted)} /{" "}
                {formatBytes(job.bytesTotal)}
                {!isTerminalJob(job.state) && job.bytesPerSecond
                  ? ` · ${formatBytes(job.bytesPerSecond)}/s`
                  : ""}
                {!isTerminalJob(job.state) && job.etaSeconds
                  ? ` · ${formatDuration(job.etaSeconds)} remaining`
                  : ""}
              </small>
              <div className="file-job-actions">
                {job.state === "queued" && (
                  <>
                    <button
                      onClick={() =>
                        send({
                          type: "file.job.reorder",
                          operationId: createLocalId(),
                          jobId: job.jobId,
                          direction: "up",
                        })
                      }
                    >
                      <ArrowUp />
                      Earlier
                    </button>
                    <button
                      onClick={() =>
                        send({
                          type: "file.job.reorder",
                          operationId: createLocalId(),
                          jobId: job.jobId,
                          direction: "down",
                        })
                      }
                    >
                      <ArrowDown />
                      Later
                    </button>
                  </>
                )}
                {job.canPause && (
                  <button onClick={() => control(job.jobId, "pause")}>
                    <Pause />
                    Pause
                  </button>
                )}
                {job.canResume && (
                  <button onClick={() => control(job.jobId, "resume")}>
                    <Play />
                    Resume
                  </button>
                )}
                {job.canCancel && (
                  <button
                    className="danger-button"
                    onClick={() => {
                      if (job.operation === "move") {
                        setCancelMoveJobId(job.jobId);
                      } else {
                        control(job.jobId, "cancel");
                      }
                    }}
                  >
                    <X />
                    Cancel
                  </button>
                )}
                {isTerminalJob(job.state) && (
                  <button onClick={() => control(job.jobId, "dismiss")}>
                    <X />
                    Remove history
                  </button>
                )}
              </div>
              {job.state === "needs-attention" && (
                <div className="file-conflict">
                  <p>{job.conflictName} already exists.</p>
                  {job.operation !== "upload" && (
                    <label>
                      <input
                        type="checkbox"
                        checked={applyAll[job.jobId] === true}
                        onChange={(event) =>
                          setApplyAll((current) => ({
                            ...current,
                            [job.jobId]: event.target.checked,
                          }))
                        }
                      />
                      Apply to all remaining conflicts
                    </label>
                  )}
                  <div className="file-conflict-actions">
                    {(job.operation === "upload"
                      ? (["replace", "keep-both", "cancel"] as const)
                      : (["replace", "skip", "cancel"] as const)
                    ).map((resolution) => (
                      <button
                        key={resolution}
                        onClick={() =>
                          send({
                            type: "file.job.conflict.resolve",
                            operationId: createLocalId(),
                            jobId: job.jobId,
                            resolution,
                            applyToAll:
                              job.operation === "upload" ? false : applyAll[job.jobId] === true,
                          })
                        }
                      >
                        {resolution === "skip" || resolution === "keep-both" ? (
                          <SkipForward />
                        ) : resolution === "replace" ? (
                          <Copy />
                        ) : (
                          <X />
                        )}
                        {conflictResolutionLabels[resolution]}
                      </button>
                    ))}
                  </div>
                </div>
              )}
            </article>
          ))
        )}
        <ConfirmationDialog
          confirmLabel="Cancel move"
          description="Cancel the move? The current incomplete destination file is removed. Already committed destination items remain, and their source items have already been removed."
          isOpen={cancelMoveJobId !== null}
          onCancel={() => setCancelMoveJobId(null)}
          onConfirm={() => {
            const jobId = cancelMoveJobId;
            setCancelMoveJobId(null);
            if (jobId) {
              control(jobId, "cancel");
            }
          }}
          title="Cancel this move?"
        />
      </section>
    </div>
  );
}

function PropertiesDialog({
  value,
  onClose,
}: {
  value: FileManagerProperties;
  onClose: () => void;
}) {
  return (
    <ModalDialog isOpen title="Properties" dismissLabel="Close" onClose={onClose}>
      <dl className="file-properties">
        <dt>Name</dt>
        <dd>{value.name}</dd>
        <dt>Path</dt>
        <dd>{value.fullPath}</dd>
        <dt>Type</dt>
        <dd>
          {value.kind}
          {value.extension ? ` · ${value.extension}` : ""}
        </dd>
        <dt>Size</dt>
        <dd>
          {value.size === null || value.size === undefined
            ? "Not calculated"
            : formatBytes(value.size)}
        </dd>
        <dt>Created</dt>
        <dd>{new Date(value.createdUtc).toLocaleString()}</dd>
        <dt>Modified</dt>
        <dd>{new Date(value.modifiedUtc).toLocaleString()}</dd>
        <dt>Accessed</dt>
        <dd>{new Date(value.accessedUtc).toLocaleString()}</dd>
        <dt>Attributes</dt>
        <dd>{value.attributes.join(", ") || "None"}</dd>
      </dl>
    </ModalDialog>
  );
}

function formatBytes(value: number): string {
  if (value < 1024) {
    return `${value} B`;
  }
  const units = ["KB", "MB", "GB", "TB"];
  let amount = value / 1024;
  let index = 0;
  while (amount >= 1024 && index < units.length - 1) {
    amount /= 1024;
    index++;
  }
  return `${amount.toLocaleString(undefined, { maximumFractionDigits: 1 })} ${units[index]}`;
}

function formatDuration(seconds: number): string {
  if (seconds < 60) {
    return `${seconds}s`;
  }
  const minutes = Math.floor(seconds / 60);
  return `${minutes}m ${seconds % 60}s`;
}

function isTerminalJob(state: string): boolean {
  return (
    state === "completed" || state === "failed" || state === "canceled" || state === "interrupted"
  );
}

function hasUniqueIds(items: readonly { id: string }[]): boolean {
  return new Set(items.map((item) => item.id)).size === items.length;
}

function hasOverlappingIds(
  existing: readonly { id: string }[],
  incoming: readonly { id: string }[],
): boolean {
  const existingIds = new Set(existing.map((item) => item.id));
  return incoming.some((item) => existingIds.has(item.id));
}

function isSemanticallyValidSession(session: FileManagerSession): boolean {
  return (
    session.left.panel === "left" &&
    session.right.panel === "right" &&
    hasUniqueIds(session.drives) &&
    hasUniqueIds(session.shortcuts) &&
    hasUniqueIds(session.left.entries) &&
    hasUniqueIds(session.right.entries) &&
    session.left.totalCount >= session.left.entries.length &&
    session.right.totalCount >= session.right.entries.length
  );
}

function formatModified(value: string): { date: string; time: string } {
  const date = new Date(value);
  const twoDigits = (part: number) => String(part).padStart(2, "0");
  return {
    date: `${twoDigits(date.getDate())}/${twoDigits(date.getMonth() + 1)}/${date.getFullYear()}`,
    time: `${twoDigits(date.getHours())}:${twoDigits(date.getMinutes())}:${twoDigits(date.getSeconds())}`,
  };
}

function fileAttributeCode(attribute: string): string {
  return (
    (
      { hidden: "H", system: "S", "read-only": "R", archive: "A", "reparse-point": "L" } as Record<
        string,
        string
      >
    )[attribute] ?? "?"
  );
}

function fileAttributeLabel(attribute: string): string {
  return (
    (
      {
        hidden: "Hidden",
        system: "System",
        "read-only": "Read-only",
        archive: "Archive",
        "reparse-point": "Link",
      } as Record<string, string>
    )[attribute] ?? attribute
  );
}
