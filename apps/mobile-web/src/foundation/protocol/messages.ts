import type { RemoteModeId } from "../settings/remoteSettings";

export interface PairHelloMessage {
  type: "pair.hello";
  clientId: string;
  deviceName: string;
  platform?: string;
  browser?: string;
  displayMode?: "browser" | "installed" | "unknown";
  pairToken?: string | undefined;
  secret?: string | undefined;
}

export interface PairDisconnectMessage {
  type: "pair.disconnect";
}

export interface DeviceRenameMessage {
  type: "device.rename";
  deviceName: string;
}

export interface HealthPingMessage {
  type: "health.ping";
}

export interface StatusGetMessage {
  type: "status.get";
}

export interface PointerSpeedSetMessage {
  type: "pointer.speed.set";
  pointerSpeed: number;
}

export interface CustomPointerSetMessage {
  type: "custom.pointer.set";
  enabled: boolean;
}

export interface AudioGetMessage {
  type: "audio.get";
}

export interface ServerCapabilities {
  awake?: AwakeCapability;
  gestureDebug?: boolean;
  inputAck?: boolean;
  clipboardRead?: boolean;
  presentation?: PresentationCapability | null;
  power?: PowerCapabilities;
  remoteLaunch?: boolean;
  urlOpen?: UrlOpenCapability;
  sleep?: boolean;
  textTransfer?: boolean;
  volume?: boolean;
}

export interface PresentationCapability {
  canControl: boolean;
}

export interface UrlOpenCapability {
  canOpen: boolean;
}

export interface TextTransferTarget {
  mode: "focused" | "configured" | "clipboard";
  displayName: string;
  available: boolean;
}

export interface AwakeCapability {
  canControl: boolean;
  active: boolean;
  mode: "off" | "indefinite" | "timed" | "expiration";
  expiresAt?: string | null | undefined;
}

export interface PowerCapabilities {
  lock: boolean;
  lockAvailability?: "notExplicitlyDisabled" | "disabledByPolicy" | "unavailable" | undefined;
  blackoutDisplay: boolean;
  displayOff: boolean;
  screenSaver: boolean;
  screenSaverAvailable: boolean;
  signOut: boolean;
  restart: boolean;
  shutdown: boolean;
}

export interface HostStatusMetadata {
  appLaunchActions?: AppLaunchActionSummary[] | undefined;
  defaultRemoteMode?: RemoteModeId | undefined;
  developerMode?: boolean | undefined;
  developerSessionId?: string | undefined;
  hostVersion?: string | undefined;
  webClientBuildId?: string | undefined;
  pcName?: string | undefined;
  pointerSpeed?: number | undefined;
  customPointerEnabled?: boolean | undefined;
  inputBlockedByElevation?: boolean | undefined;
  selectedAdapterName?: string | undefined;
  selectedIp?: string | undefined;
  selectedPort?: number | undefined;
  textTransferTarget?: TextTransferTarget | undefined;
  webSocketUrl?: string | undefined;
}

export interface PairAcceptedMessage {
  type: "pair.accepted";
  clientId: string;
  pcName: string;
  secret: string;
  paired: true;
  capabilities?: ServerCapabilities;
  host?: HostStatusMetadata;
}

export type PairRejectionReason =
  | "pair-first"
  | "missing-token"
  | "invalid-token"
  | "expired-token"
  | "stale-token"
  | "device-revoked"
  | "secret-revoked"
  | "protocol-version-mismatch"
  | "rate-limited"
  | "invalid-message"
  | (string & {});

export interface PairRejectedMessage {
  type: "pair.rejected";
  reason: PairRejectionReason;
  diagnosticCode?: string;
}

export interface StatusMessage {
  type: "status";
  connected: boolean;
  message?: string;
  pcName?: string;
  capabilities?: ServerCapabilities;
  host?: HostStatusMetadata;
}

export interface HealthPongMessage {
  type: "health.pong";
}

export interface InputAckMessage {
  type: "input.ack";
  seq?: number;
}

export interface InputErrorMessage {
  type: "input.error";
  seq?: number;
  code?: string;
  message: string;
}

export interface PointerMoveMessage {
  type: "pointer.move";
  seq?: number;
  dx: number;
  dy: number;
}

export interface PointerButtonMessage {
  type: "pointer.button";
  seq?: number;
  button: "left" | "right";
  action: "down" | "up" | "click";
}

export interface PointerWheelMessage {
  type: "pointer.wheel";
  seq?: number;
  dx: number;
  dy: number;
}

export interface PointerZoomMessage {
  type: "pointer.zoom";
  seq?: number;
  direction: "in" | "out";
}

export interface KeyboardTextMessage {
  type: "keyboard.text";
  seq?: number;
  text: string;
}

export interface KeyboardSpecialMessage {
  type: "keyboard.special";
  seq?: number;
  key: string;
  modifiers?: string[] | undefined;
}

export interface SystemSleepMessage {
  type: "system.sleep";
}

export type PresentationTarget = "powerpoint" | "google-slides" | "pdf";

export type PresentationAction = "next" | "previous" | "start" | "end" | "black" | "pointer";

export interface PresentationCommandMessage {
  type: "presentation.command";
  operationId: string;
  target: PresentationTarget;
  action: PresentationAction;
}

export interface PresentationCommandResultMessage {
  type: "presentation.command.result";
  operationId: string;
  target: PresentationTarget;
  action: PresentationAction;
  succeeded: boolean;
  code?: string;
  message: string;
}

export type AppLaunchActionKind = "browser" | "spotify" | "vlc" | "powerpoint" | "custom";

export interface AppLaunchActionSummary {
  id: string;
  label: string;
  kind: AppLaunchActionKind;
}

export type SystemPowerAction = "lock" | "blackoutDisplay" | "displayOff" | "screenSaver" | "signOut" | "restart" | "shutdown";

export interface SystemPowerMessage {
  type: "system.power";
  action: SystemPowerAction;
}

export interface SystemPowerResultMessage {
  type: "system.power.result";
  action: string;
  succeeded: boolean;
  code?: string;
  message: string;
}

export interface AwakeSetMessage {
  type: "awake.set";
  enabled: boolean;
}

export interface AwakeResultMessage {
  type: "awake.result";
  enabled: boolean;
  succeeded: boolean;
  code?: string;
  message: string;
}

export type RemoteLaunchAction = "openYoutube" | "startOrActivateKodi";

export interface RemoteLaunchMessage {
  type: "remote.launch";
  action: RemoteLaunchAction;
}

export interface AppLaunchMessage {
  type: "app.launch";
  actionId: string;
}

export interface AppLaunchResultMessage {
  type: "app.launch.result";
  actionId: string;
  succeeded: boolean;
  code?: string;
  message: string;
}

export interface UrlOpenMessage {
  type: "url.open";
  operationId: string;
  url: string;
}

export interface UrlOpenResultMessage {
  type: "url.open.result";
  operationId: string;
  succeeded: boolean;
  code?: string;
  message: string;
  normalizedUrl?: string | null;
}

export interface TextSendMessage {
  type: "text.send";
  operationId: string;
  text: string;
  sendEnter: boolean;
}

export interface TextSendResultMessage {
  type: "text.send.result";
  operationId: string;
  succeeded: boolean;
  code?: string;
  message: string;
  deliveryKind?: "typed" | "pasted" | "clipboard";
}

export interface ClipboardGetMessage {
  type: "clipboard.get";
  operationId: string;
}

export interface ClipboardGetResultMessage {
  type: "clipboard.get.result";
  operationId: string;
  succeeded: boolean;
  code?: string;
  message: string;
  text?: string | null;
}

export interface AudioMuteToggleMessage {
  type: "audio.mute.toggle";
}

export interface AudioVolumeSetMessage {
  type: "audio.volume.set";
  volume: number;
}

export interface AudioStateMessage {
  type: "audio.state";
  volume: number;
  muted: boolean;
}

export type ClientMessage =
  | PairHelloMessage
  | PairDisconnectMessage
  | DeviceRenameMessage
  | HealthPingMessage
  | StatusGetMessage
  | PointerSpeedSetMessage
  | CustomPointerSetMessage
  | AudioGetMessage
  | PointerMoveMessage
  | PointerButtonMessage
  | PointerWheelMessage
  | PointerZoomMessage
  | KeyboardTextMessage
  | KeyboardSpecialMessage
  | PresentationCommandMessage
  | SystemSleepMessage
  | SystemPowerMessage
  | AwakeSetMessage
  | RemoteLaunchMessage
  | AppLaunchMessage
  | UrlOpenMessage
  | TextSendMessage
  | ClipboardGetMessage
  | AudioMuteToggleMessage
  | AudioVolumeSetMessage;

export type ServerMessage = PairAcceptedMessage | PairRejectedMessage | StatusMessage | HealthPongMessage | InputAckMessage | InputErrorMessage | PresentationCommandResultMessage | SystemPowerResultMessage | AwakeResultMessage | AppLaunchResultMessage | UrlOpenResultMessage | TextSendResultMessage | ClipboardGetResultMessage | AudioStateMessage;
