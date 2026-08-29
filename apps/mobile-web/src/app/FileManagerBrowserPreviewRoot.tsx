import { useCallback, useEffect, useRef } from "react";
import FileManagerWorkspace from "../features/file-manager";
import { publishFileManagerResult } from "../foundation/connection/fileManagerResultBus";
import type {
  ClientMessage,
  FileManagerEntry,
  FileManagerPanelPage,
  FileManagerSession,
} from "../foundation/protocol/messages";
import { AppHeader } from "./AppHeader";
import { getModeDefinition, getModeTabs } from "./appModeTabs";

const modified = "2026-08-08T14:32:18Z";
const folder = (id: string, name: string, attributes: string[] = []): FileManagerEntry => ({
  id,
  name,
  kind: "folder",
  extension: "",
  size: null,
  modifiedUtc: modified,
  attributes,
});
const file = (
  id: string,
  name: string,
  extension: string,
  size: number,
  attributes: string[] = ["archive"],
): FileManagerEntry => ({
  id,
  name,
  kind: "file",
  extension,
  size,
  modifiedUtc: modified,
  attributes,
});
const page = (
  panel: "left" | "right",
  displayPath: string,
  driveId: string,
  entries: FileManagerEntry[],
): FileManagerPanelPage => ({
  panel,
  revision: `${panel}-preview-revision`,
  displayPath,
  parentId: `${panel}-parent`,
  driveId,
  sortBy: "name",
  descending: false,
  totalCount: entries.length,
  entries,
  continuation: null,
});

const previewSession: FileManagerSession = {
  sessionId: "files-preview-session",
  drives: [
    {
      id: "drive-system",
      label: "C:\\ System",
      driveType: "fixed",
      freeBytes: 421_000_000_000,
      totalBytes: 1_000_000_000_000,
    },
    {
      id: "drive-media",
      label: "D:\\ Media",
      driveType: "fixed",
      freeBytes: 812_000_000_000,
      totalBytes: 2_000_000_000_000,
    },
  ],
  shortcuts: [
    { id: "shortcut-desktop", label: "Desktop" },
    { id: "shortcut-documents", label: "Documents" },
    { id: "shortcut-downloads", label: "Downloads" },
  ],
  left: page("left", "C:\\Work\\Projects", "drive-system", [
    folder("left-design", "Design"),
    folder("left-documents", "Project documents"),
    folder("left-release", "Release package"),
    file("left-plan", "Launch plan and checklist.docx", "docx", 486_400),
    file("left-notes", "Meeting notes.txt", "txt", 18_432),
    file("left-overview", "Product overview.pdf", "pdf", 4_823_040),
    file("left-slides", "Quarterly presentation.pptx", "pptx", 12_845_056),
  ]),
  right: page("right", "D:\\Media\\Shared", "drive-media", [
    folder("right-photos", "Event photos"),
    folder("right-video", "Product videos"),
    folder("right-wallpapers", "Wallpapers"),
    file("right-brochure", "Product brochure.pdf", "pdf", 3_912_704),
    file("right-demo", "Remote control demo.mp4", "mp4", 284_164_096),
    file("right-logo", "Voltura Air logo.png", "png", 742_400),
    file("right-readme", "Read me first.txt", "txt", 8_192, ["read-only"]),
  ]),
};

export function FileManagerBrowserPreviewRoot() {
  const compactModeButtonRef = useRef<HTMLButtonElement>(null);
  const send = useCallback((message: ClientMessage) => {
    if (message.type === "file.session.open") {
      window.setTimeout(
        () =>
          publishFileManagerResult({
            type: "file.session.open.result",
            operationId: message.operationId,
            succeeded: true,
            message: "Files ready.",
            session: previewSession,
          }),
        0,
      );
    } else if (message.type === "file.jobs.get") {
      window.setTimeout(
        () =>
          publishFileManagerResult({
            type: "file.jobs.status",
            operationId: message.operationId,
            jobs: [],
          }),
        0,
      );
    }
  }, []);

  useEffect(() => {
    document.documentElement.dataset.displayMode = "browser";
    const previewTheme = new URL(window.location.href).searchParams.get("theme");
    if (previewTheme === "light" || previewTheme === "dark") {
      document.documentElement.dataset.theme = previewTheme;
    }
  }, []);
  const modeTabs = getModeTabs("files", true, true);
  return (
    <div className="app-frame control-depth">
      <main className="app-shell mode-tabs-collapsed files-active control-depth">
        <AppHeader
          activeMode={getModeDefinition("files")}
          canShowModeNavigation
          compactModeButtonRef={compactModeButtonRef}
          connectionPcName="STUDIO-PC"
          developerMode={false}
          isModeSelectorOpen={false}
          message="Connected to STUDIO-PC"
          modeTabs={modeTabs}
          onCloseModeSelector={() => undefined}
          onOpenSettings={() => undefined}
          onSelectMode={() => undefined}
          onSelectRemoteMode={() => undefined}
          onToggleModeSelector={() => undefined}
          remoteMode="standard"
          refreshInstalledApp={() => undefined}
          state="paired"
          tab="files"
        />
        <FileManagerWorkspace
          activePc={{
            customName: false,
            id: "preview-pc",
            name: "STUDIO-PC",
            url: "https://preview.invalid",
            hostIdentityPublicKey: "A".repeat(87),
            transportMode: "secure-direct",
          }}
          capability={{
            canBrowse: true,
            canModify: true,
            canTransfer: true,
            hidesProtectedSystemItems: true,
            maxPageSize: 100,
          }}
          canMirrorView
          clientId="preview-client"
          connectionEpoch={1}
          mirrorViewUnavailableMessage="PC Screen unavailable."
          onMirrorView={() => undefined}
          send={send}
          state="paired"
        />
      </main>
    </div>
  );
}
