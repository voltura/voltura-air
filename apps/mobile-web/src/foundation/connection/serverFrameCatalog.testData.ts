import type { ServerMessage } from "../protocol/messages";

type ServerMessageType = ServerMessage["type"];
type MessageOfType<T extends ServerMessageType> = Extract<ServerMessage, { type: T }>;

export interface ServerFrameContract<T extends ServerMessageType> {
  required: readonly string[];
  frames: readonly MessageOfType<T>[];
}

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
  "pair.challenge": { required: ["clientId", "challenge"], frames: [{ type: "pair.challenge", clientId: "client-a", challenge: "challenge-a" }] },
  "pair.rejected": { required: ["reason"], frames: [{ type: "pair.rejected", reason: "invalid-token" }] },
  "status": {
    required: ["connected"],
    frames: [
      { type: "status", connected: true, message: "Connected", pcName: "Office PC" },
      { type: "status", connected: true, host: { appLaunchActions: [] } }
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
  "audio.state": { required: ["volume", "muted"], frames: [{ type: "audio.state", volume: 72, muted: false }] }
} satisfies { [T in ServerMessageType]: ServerFrameContract<T> };

const serverFrameContracts = Object.values(serverFrameCatalog) as unknown as readonly ServerFrameContract<ServerMessageType>[];

export const catalogFrames = serverFrameContracts.flatMap((contract) => contract.frames);
