import ScreenViewWorkspace from "./ScreenViewWorkspace";
import { defaultTrackpadSettings } from "../../foundation/input/gestures";
import type { ClientMessage } from "../../foundation/protocol/messages";
import { publishScreenViewResult } from "../../foundation/connection/screenViewResultBus";

export default function ScreenViewBrowserPreviewRoot() {
  const send = (message: ClientMessage) => {
    if (message.type === "screen.view.sources.get") {
      queueMicrotask(() => publishScreenViewResult({
        type: "screen.view.sources.result",
        operationId: message.operationId,
        succeeded: true,
        message: "Displays are available.",
        sources: [
          { id: "display-1", label: "Main display", width: 1920, height: 1080, isPrimary: true },
          { id: "display-2", label: "Portrait display", width: 1080, height: 1920, isPrimary: false }
        ]
      }));
    }
  };

  return <main className="app-shell control-depth">
    <ScreenViewWorkspace
      activePc={{ customName: false, id: "preview", name: "Studio PC", url: "http://127.0.0.1:51395", hostIdentityFingerprint: "AAAAAAAAAAAAAAAAAAAAAA", hostIdentityPublicKey: `B${"A".repeat(86)}` }}
      capability={{ enabled: true, permissionGranted: true, canView: true, requiresRepair: false, encrypted: true, maxWidth: 1920, maxHeight: 1080, maxFramesPerSecond: 30 }}
      clientId="preview-client"
      onBack={() => { /* Preview only. */ }}
      onOpenKeyboard={() => { /* Preview only. */ }}
      send={send}
      state="paired"
      trackpadSettings={defaultTrackpadSettings}
    />
  </main>;
}
