import type { ServerMessage } from "../protocol/messages";

type ServerMessageType = ServerMessage["type"];
type MessageOfType<T extends ServerMessageType> = Extract<ServerMessage, { type: T }>;

export interface ServerFrameContract<T extends ServerMessageType> {
  required: readonly string[];
  frames: readonly MessageOfType<T>[];
}

const filePage = {
  panel: "left" as const,
  revision: "revision-a",
  displayPath: "Downloads",
  parentId: "parent-a",
  driveId: "drive-a",
  sortBy: "name" as const,
  descending: false,
  totalCount: 0,
  entries: [],
  continuation: null
};
const fileSession = { sessionId: "session-a", drives: [], shortcuts: [], left: filePage, right: { ...filePage, panel: "right" as const } };
const fileProperties = { entryId: "entry-a", name: "file.txt", fullPath: "Downloads\\file.txt", kind: "file" as const, extension: "txt", size: 12, createdUtc: "2026-08-04T00:00:00Z", modifiedUtc: "2026-08-04T00:00:00Z", accessedUtc: "2026-08-04T00:00:00Z", attributes: [] };
const fileJob = { jobId: "job-a", operation: "copy" as const, state: "queued" as const, queuePosition: 1, itemsCompleted: 0, itemsTotal: 1, bytesCompleted: 0, bytesTotal: 12, canPause: false, canResume: false, canCancel: true };

export const serverFrameCatalog = {
  "pair.accepted": {
    required: ["clientId", "pcName", "paired"],
    frames: [{
      type: "pair.accepted", clientId: "client-a", pcName: "Office PC", paired: true,
      capabilities: {
        awake: { canControl: true, active: false, mode: "off" },
        gestureDebug: false, inputAck: true, clipboardRead: true,
        presentation: { canControl: true, canSaveReports: true, laserPointerActive: false },
        power: { lock: true, lockAvailability: "notExplicitlyDisabled", blackoutDisplay: true, displayOff: true, screenSaver: true, screenSaverAvailable: true, signOut: true, restart: true, shutdown: true },
        remoteLaunch: true, urlOpen: { canOpen: true }, sleep: true, textTransfer: true, volume: true
      },
      host: {
        appLaunchActions: [{ id: "custom.notes", label: "Notes", kind: "custom" }],
        defaultRemoteMode: "youtube", developerMode: true, developerSessionId: "session-a",
        hostVersion: "0.6.4", webClientBuildId: "build-a", pcName: "Office PC", pointerSpeed: 55,
        customPointerEnabled: true, inputBlockedByElevation: false, selectedAdapterName: "Ethernet",
        selectedIp: "192.168.1.50", selectedPort: 51395,
        textTransferTarget: { mode: "focused", displayName: "Focused app", available: true },
        webSocketUrl: "ws://192.168.1.50:51395/ws"
      }
    }]
  },
  "pair.disconnect.accepted": { required: [], frames: [{ type: "pair.disconnect.accepted" }] },
  "pair.challenge": { required: ["clientId", "challenge"], frames: [{ type: "pair.challenge", clientId: "client-a", challenge: "challenge-a" }] },
  "pair.bootstrap.challenge": {
    required: ["clientId", "clientNonce", "serverNonce", "hostIdentity", "proof"],
    frames: [{
      type: "pair.bootstrap.challenge",
      clientId: "client-a",
      clientNonce: "A".repeat(43),
      serverNonce: "B".repeat(43),
      hostIdentity: { publicKey: "C".repeat(87), fingerprint: "D".repeat(22) },
      proof: "E".repeat(43)
    }]
  },
  "pair.rejected": { required: ["reason"], frames: [{ type: "pair.rejected", reason: "invalid-token" }] },
  "status": {
    required: ["connected"],
    frames: [
      { type: "status", connected: true, message: "Connected", pcName: "Office PC" },
      { type: "status", connected: true, host: { appLaunchActions: [] } },
      {
        type: "status",
        connected: true,
        capabilities: {
          screenView: {
            enabled: true, permissionGranted: true, canView: true, requiresRepair: false,
            encrypted: true, maxWidth: 1920, maxHeight: 1080, maxFramesPerSecond: 30,
            receiverQualityFeedback: true
          }
        }
      }
    ]
  },
  "health.pong": { required: [], frames: [{ type: "health.pong" }] },
  "input.ack": { required: [], frames: [{ type: "input.ack", seq: 4 }] },
  "input.error": {
    required: ["message"],
    frames: [
      { type: "input.error", seq: 4, code: "VAIR-INPUT", message: "Input failed" },
      { type: "input.error", message: "Input failed" }
    ]
  },
  "presentation.command.result": {
    required: ["operationId", "target", "action", "succeeded", "message", "laserPointerActive"],
    frames: [
      { type: "presentation.command.result", operationId: "op-presentation", target: "powerpoint", action: "next", succeeded: true, message: "Done", laserPointerActive: false },
      { type: "presentation.command.result", operationId: "op-presentation", target: "powerpoint", action: "next", succeeded: false, code: "permission-denied", message: "Blocked", laserPointerActive: true }
    ]
  },
  "presentation.powerpoint.refresh.result": {
    required: ["operationId", "succeeded", "message", "state", "presentations"],
    frames: [
      {
        type: "presentation.powerpoint.refresh.result",
        operationId: "op-refresh",
        succeeded: true,
        message: "Refreshed",
        state: "ready",
        presentations: [{
          runtimePresentationId: "presentation-1",
          name: "Quarterly update.pptx",
          state: "presenting",
          slideCount: 24,
          currentSlideIndex: 7,
          currentShowPosition: 7,
          slideShowState: "running"
        }]
      },
      {
        type: "presentation.powerpoint.refresh.result",
        operationId: "op-refresh-failed",
        succeeded: false,
        code: "powerpoint-busy",
        message: "Busy",
        state: "busy",
        presentations: []
      }
    ]
  },
  "presentation.powerpoint.launch.result": {
    required: ["operationId", "presentationId", "succeeded", "message"],
    frames: [
      {
        type: "presentation.powerpoint.launch.result",
        operationId: "op-launch",
        presentationId: "report-1",
        succeeded: true,
        message: "Presentation opened and started.",
        runtimePresentationId: "presentation-1",
        presentation: {
          runtimePresentationId: "presentation-1",
          name: "Quarterly update.pptx",
          state: "presenting",
          slideCount: 24,
          currentSlideIndex: 1,
          currentShowPosition: 1,
          slideShowState: "running"
        }
      },
      {
        type: "presentation.powerpoint.launch.result",
        operationId: "op-launch-failed",
        presentationId: "report-missing",
        succeeded: false,
        code: "powerpoint-source-missing",
        message: "The file is unavailable."
      }
    ]
  },
  "presentation.session.result": {
    required: ["operationId", "action", "succeeded", "message"],
    frames: [
      {
        type: "presentation.session.result",
        operationId: "op-session",
        action: "save",
        succeeded: true,
        message: "Presentation saved"
      },
      {
        type: "presentation.session.result",
        operationId: "op-session-failed",
        action: "save",
        succeeded: false,
        code: "session-not-owner",
        message: "Not owner"
      }
    ]
  },
  "presentation.report.save.result": {
    required: ["operationId", "reportId", "succeeded", "message"],
    frames: [
      { type: "presentation.report.save.result", operationId: "op-report", reportId: "report-1", succeeded: true, message: "Saved" },
      { type: "presentation.report.save.result", operationId: "op-report", reportId: "report-1", succeeded: false, code: "invalid-report", message: "Invalid" }
    ]
  },
  "system.power.result": {
    required: ["operationId", "action", "succeeded", "message"],
    frames: [
      { type: "system.power.result", operationId: "op-power", action: "lock", succeeded: true, message: "Locked" },
      { type: "system.power.result", operationId: "op-power", action: "lock", succeeded: false, code: "VAIR-POWER-DENIED", message: "Blocked" }
    ]
  },
  "awake.result": {
    required: ["operationId", "enabled", "succeeded", "message"],
    frames: [
      { type: "awake.result", operationId: "op-awake", enabled: true, succeeded: true, message: "Awake" },
      { type: "awake.result", operationId: "op-awake", enabled: true, succeeded: false, code: "VAIR-AWAKE-DENIED", message: "Blocked" }
    ]
  },
  "app.launch.result": {
    required: ["operationId", "actionId", "succeeded", "message"],
    frames: [
      { type: "app.launch.result", operationId: "op-app", actionId: "custom.notes", succeeded: true, code: "started", message: "Opened" },
      { type: "app.launch.result", operationId: "op-app", actionId: "custom.notes", succeeded: false, code: "not-found", message: "Missing" }
    ]
  },
  "url.open.result": {
    required: ["operationId", "succeeded", "message"],
    frames: [
      { type: "url.open.result", operationId: "op-url", succeeded: true, code: "accepted", message: "Opened", normalizedUrl: "https://example.com/" },
      { type: "url.open.result", operationId: "op-url", succeeded: false, code: "invalid-url", message: "Invalid" }
    ]
  },
  "text.send.result": {
    required: ["operationId", "succeeded", "message"],
    frames: [
      { type: "text.send.result", operationId: "op-text", succeeded: true, message: "Sent", deliveryKind: "typed" },
      { type: "text.send.result", operationId: "op-text", succeeded: false, code: "VAIR-TEXT-DELIVERY-FAILED", message: "Failed", deliveryKind: "typed" }
    ]
  },
  "clipboard.get.result": {
    required: ["operationId", "succeeded", "message"],
    frames: [
      { type: "clipboard.get.result", operationId: "op-clipboard", succeeded: true, message: "Read", text: "Example PC clipboard text" },
      { type: "clipboard.get.result", operationId: "op-clipboard", succeeded: true, message: "Read", text: "" },
      { type: "clipboard.get.result", operationId: "op-clipboard", succeeded: false, code: "VAIR-CLIPBOARD-UNAVAILABLE", message: "Unavailable" }
    ]
  },
  "custom.screen.get.result": {
    required: ["operationId", "succeeded"],
    frames: [
      {
        type: "custom.screen.get.result",
        operationId: "op-screen-get",
        succeeded: true,
        screen: {
          id: "screen.one",
          name: "Media",
          revision: "rev.one",
          orientationLayoutsEnabled: false,
          showNavigationHeader: true,
          sections: [{
            id: "section.more",
            name: "More",
            showHeader: true,
            widthColumns: 12,
            heightMode: "fill",
            fillWeight: 1,
            rowLimit: 0,
            buttonAlignment: "start",
            kind: "buttons",
            collapsible: true,
            initiallyExpanded: false,
            trackpadLeftClick: true,
            trackpadRightClick: true,
            trackpadButtonSide: "right",
            trackpadFullscreenControl: false,
            trackpadGyroControl: false,
            trackpadEnabled: true,
            volumeEnabled: true,
            buttons: []
          }]
        }
      },
      {
        type: "custom.screen.get.result",
        operationId: "op-screen-get-failed",
        succeeded: false,
        code: "not-assigned",
        message: "Unavailable"
      }
    ]
  },
  "custom.screen.invoke.result": {
    required: ["operationId", "screenId", "buttonId", "succeeded", "message"],
    frames: [
      {
        type: "custom.screen.invoke.result",
        operationId: "op-screen-invoke",
        screenId: "screen.one",
        buttonId: "button.one",
        succeeded: true,
        message: "Action completed."
      },
      {
        type: "custom.screen.invoke.result",
        operationId: "op-screen-invoke-failed",
        screenId: "screen.one",
        buttonId: "button.one",
        succeeded: false,
        code: "permission-denied",
        message: "Blocked"
      }
    ]
  },
  "screen.view.sources.result": {
    required: ["operationId", "succeeded", "message", "sources"],
    frames: [
      { type: "screen.view.sources.result", operationId: "op-screen-sources", succeeded: true, message: "Displays are available.", sources: [{ id: "display-1", label: "Display 1", width: 1920, height: 1080, isPrimary: true }] },
      { type: "screen.view.sources.result", operationId: "op-screen-sources-failed", succeeded: false, code: "permission-denied", message: "Screen viewing is disabled.", sources: [] }
    ]
  },
  "screen.view.start.result": {
    required: ["operationId", "displayId", "succeeded", "message"],
    frames: [
      { type: "screen.view.start.result", operationId: "op-screen-start", displayId: "display-1", succeeded: true, code: "accepted", message: "Ready.", offerSdp: "v=0\r\n", hostSignature: "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" },
      { type: "screen.view.start.result", operationId: "op-screen-start-busy", displayId: "display-1", succeeded: false, code: "busy", message: "Another device is viewing." }
    ]
  },
  "screen.view.answer.result": {
    required: ["operationId", "succeeded", "message"],
    frames: [
      { type: "screen.view.answer.result", operationId: "op-screen-start", succeeded: true, code: "accepted", message: "Opening." },
      { type: "screen.view.answer.result", operationId: "op-screen-failed", succeeded: false, code: "invalid-answer", message: "Rejected." }
    ]
  },
  "screen.view.source.result": {
    required: ["operationId", "displayId", "succeeded", "message"],
    frames: [
      { type: "screen.view.source.result", operationId: "op-screen-source", displayId: "display-2", succeeded: true, code: "accepted", message: "The mirrored display was changed." },
      { type: "screen.view.source.result", operationId: "op-screen-source-failed", displayId: "display-2", succeeded: false, code: "display-unavailable", message: "The selected display is unavailable." }
    ]
  },
  "screen.view.stop.result": {
    required: ["operationId", "succeeded", "message"],
    frames: [
      { type: "screen.view.stop.result", operationId: "op-screen-stop", succeeded: true, code: "stopped", message: "Screen viewing stopped." },
      { type: "screen.view.stop.result", operationId: "op-screen-stop-failed", succeeded: false, code: "not-owner", message: "This device is not viewing." }
    ]
  },
  "screen.view.ended": {
    required: ["reason", "message"],
    frames: [
      { type: "screen.view.ended", operationId: "screen-operation", reason: "host-stopped", message: "The PC stopped screen viewing." },
      { type: "screen.view.ended", operationId: "screen-operation", reason: "permission-revoked", message: "The PC stopped screen viewing and disallowed this device." }
    ]
  },
  "phone.webcam.start.result": {
    required: ["operationId", "succeeded", "message"],
    frames: [
      { type: "phone.webcam.start.result", operationId: "op-webcam-start", succeeded: true, code: "accepted", message: "Ready.", offerSdp: "v=0\r\n", hostSignature: "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", maximumBitrate: 12000000 },
      { type: "phone.webcam.start.result", operationId: "op-webcam-relay", succeeded: true, code: "accepted", message: "Ready.", offerSdp: "v=0\r\n", hostSignature: "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", iceServers: [{ urls: ["turns:turn.voltura.se:5349?transport=tcp"], username: "1740000000:client", credential: "credential" }], turnExpiresAt: "2026-08-13T22:30:00Z", relayUsageBytes: 1234, relayUsageCheckedAt: "2026-08-13T22:15:00Z", relayQuality: "Standard", maximumBitrate: 4000000 },
      { type: "phone.webcam.start.result", operationId: "op-webcam-busy", succeeded: false, code: "busy", message: "Another phone is active." }
    ]
  },
  "phone.webcam.answer.result": {
    required: ["operationId", "succeeded", "message"],
    frames: [
      { type: "phone.webcam.answer.result", operationId: "op-webcam-start", succeeded: true, code: "accepted", message: "Connecting." },
      { type: "phone.webcam.answer.result", operationId: "op-webcam-answer-failed", succeeded: false, code: "invalid-answer", message: "Rejected." }
    ]
  },
  "phone.webcam.stop.result": {
    required: ["operationId", "succeeded", "message"],
    frames: [
      { type: "phone.webcam.stop.result", operationId: "op-webcam-stop", succeeded: true, code: "stopped", message: "Stopped." },
      { type: "phone.webcam.stop.result", operationId: "op-webcam-stop-failed", succeeded: false, code: "not-owner", message: "Not active." }
    ]
  },
  "phone.webcam.ended": {
    required: ["operationId", "reason", "message"],
    frames: [
      { type: "phone.webcam.ended", operationId: "op-webcam-ended-transport", reason: "transport-lost", message: "The Phone webcam session ended." },
      { type: "phone.webcam.ended", operationId: "op-webcam-ended-decoder", reason: "decoder-failed", message: "The PC video decoder stopped." },
      { type: "phone.webcam.ended", operationId: "op-webcam-ended-offer", reason: "offer-expired", message: "The Phone webcam offer expired." }
    ]
  },
  "file.session.open.result": { required: ["operationId", "succeeded", "message"], frames: [
    { type: "file.session.open.result", operationId: "op-file-session-ok", succeeded: true, message: "Opened.", session: fileSession },
    { type: "file.session.open.result", operationId: "op-file-session", succeeded: false, message: "Unavailable." }
  ] },
  "file.page.get.result": { required: ["operationId", "succeeded", "message"], frames: [
    { type: "file.page.get.result", operationId: "op-file-page-ok", succeeded: true, message: "Loaded.", page: filePage },
    { type: "file.page.get.result", operationId: "op-file-page", succeeded: false, message: "Unavailable." }
  ] },
  "file.navigate.result": { required: ["operationId", "succeeded", "message"], frames: [
    { type: "file.navigate.result", operationId: "op-file-nav-ok", succeeded: true, message: "Opened.", page: filePage },
    { type: "file.navigate.result", operationId: "op-file-nav", succeeded: false, message: "Unavailable." }
  ] },
  "file.refresh.result": { required: ["operationId", "succeeded", "message"], frames: [
    { type: "file.refresh.result", operationId: "op-file-refresh-ok", succeeded: true, message: "Refreshed.", page: filePage },
    { type: "file.refresh.result", operationId: "op-file-refresh", succeeded: false, message: "Unavailable." }
  ] },
  "file.properties.get.result": { required: ["operationId", "succeeded", "message"], frames: [
    { type: "file.properties.get.result", operationId: "op-file-properties-ok", succeeded: true, message: "Loaded.", properties: fileProperties },
    { type: "file.properties.get.result", operationId: "op-file-properties", succeeded: false, message: "Unavailable." }
  ] },
  "file.clipboard.set.result": { required: ["operationId", "succeeded", "message"], frames: [
    { type: "file.clipboard.set.result", operationId: "op-file-clipboard-ok", succeeded: true, message: "Copied." },
    { type: "file.clipboard.set.result", operationId: "op-file-clipboard", succeeded: false, message: "Unavailable." }
  ] },
  "file.open.result": { required: ["operationId", "succeeded", "message"], frames: [
    { type: "file.open.result", operationId: "op-file-open-ok", succeeded: true, message: "Opened." },
    { type: "file.open.result", operationId: "op-file-open", succeeded: false, message: "Unavailable." }
  ] },
  "file.job.create.result": { required: ["operationId", "succeeded", "message"], frames: [
    { type: "file.job.create.result", operationId: "op-file-job-ok", succeeded: true, message: "Queued.", job: fileJob },
    { type: "file.job.create.result", operationId: "op-file-job", succeeded: false, message: "Unavailable." }
  ] },
  "file.job.control.result": { required: ["operationId", "succeeded", "message"], frames: [
    { type: "file.job.control.result", operationId: "op-file-control-ok", succeeded: true, message: "Paused." },
    { type: "file.job.control.result", operationId: "op-file-control", succeeded: false, message: "Unavailable." }
  ] },
  "file.job.reorder.result": { required: ["operationId", "succeeded", "message"], frames: [
    { type: "file.job.reorder.result", operationId: "op-file-reorder-ok", succeeded: true, message: "Reordered." },
    { type: "file.job.reorder.result", operationId: "op-file-reorder", succeeded: false, message: "Unavailable." }
  ] },
  "file.job.conflict.resolve.result": { required: ["operationId", "succeeded", "message"], frames: [
    { type: "file.job.conflict.resolve.result", operationId: "op-file-conflict-ok", succeeded: true, message: "Resolved." },
    { type: "file.job.conflict.resolve.result", operationId: "op-file-conflict", succeeded: false, message: "Unavailable." }
  ] },
  "file.sort.result": { required: ["operationId", "succeeded", "message"], frames: [
    { type: "file.sort.result", operationId: "op-file-sort-ok", succeeded: true, message: "Sorted.", page: filePage },
    { type: "file.sort.result", operationId: "op-file-sort", succeeded: false, message: "Unavailable." }
  ] },
  "file.jobs.status": { required: ["jobs"], frames: [
    { type: "file.jobs.status", jobs: [] },
    { type: "file.jobs.status", operationId: "op-file-paste-status", jobs: [{ ...fileJob, jobId: "job-paste", operation: "paste" }] }
  ] },
  "file.transfer.start.result": { required: ["operationId", "succeeded", "message"], frames: [
    { type: "file.transfer.start.result", operationId: "op-transfer-start", succeeded: true, message: "Ready.", transferId: "transfer-a" },
    { type: "file.transfer.start.result", operationId: "op-transfer-failed", succeeded: false, code: "busy", message: "Busy." }
  ] },
  "file.transfer.offer": { required: ["transferId", "direction", "fileName", "declaredSize", "offerSdp", "hostSignature"], frames: [{
    type: "file.transfer.offer", transferId: "transfer-a", direction: "download", fileName: "report.pdf", declaredSize: 12,
    offerSdp: "v=0\r\n", hostSignature: "signature-a", iceServers: null
  }] },
  "file.transfer.answer.result": { required: ["operationId", "succeeded", "message"], frames: [
    { type: "file.transfer.answer.result", operationId: "op-transfer-answer", succeeded: true, message: "Connected." },
    { type: "file.transfer.answer.result", operationId: "op-transfer-answer-failed", succeeded: false, code: "offer-expired", message: "Expired." }
  ] },
  "file.transfer.cancel.result": { required: ["operationId", "succeeded", "message"], frames: [
    { type: "file.transfer.cancel.result", operationId: "op-transfer-cancel", succeeded: true, message: "Canceled." },
    { type: "file.transfer.cancel.result", operationId: "op-transfer-cancel-failed", succeeded: false, code: "transfer-unavailable", message: "Unavailable." }
  ] },
  "file.transfer.status": { required: ["transferId", "direction", "state", "bytesCompleted", "bytesTotal"], frames: [{
    type: "file.transfer.status", transferId: "transfer-a", direction: "download", state: "transferring", bytesCompleted: 6, bytesTotal: 12
  }] },
  "file.transfer.result": { required: ["transferId", "direction", "succeeded", "message", "fileName", "declaredSize"], frames: [{
    type: "file.transfer.result", transferId: "transfer-a", direction: "download", succeeded: true, message: "Ready.", fileName: "report.pdf", declaredSize: 12
  }, {
    type: "file.transfer.result", transferId: "transfer-b", direction: "upload", succeeded: false, code: "stalled", message: "Stopped.", fileName: "report.pdf", declaredSize: 12
  }] },
  "audio.state": { required: ["volume", "muted"], frames: [{ type: "audio.state", volume: 72, muted: false }] }
} satisfies { [T in ServerMessageType]: ServerFrameContract<T> };

const serverFrameContracts = Object.values(serverFrameCatalog) as unknown as readonly ServerFrameContract<ServerMessageType>[];

export const catalogFrames = serverFrameContracts.flatMap((contract) => contract.frames);
