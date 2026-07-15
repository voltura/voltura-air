import type { RemoteModeId } from "./remoteSettings";

export type PairHelloMessage = {
  type: "pair.hello";
  clientId: string;
  deviceName: string;
  platform?: string;
  browser?: string;
  displayMode?: "browser" | "installed" | "unknown";
  pairToken?: string;
  secret?: string;
};

export type PairDisconnectMessage = {
  type: "pair.disconnect";
};

export type DeviceRenameMessage = {
  type: "device.rename";
  deviceName: string;
};

export type HealthPingMessage = {
  type: "health.ping";
};

export type StatusGetMessage = {
  type: "status.get";
};

export type PointerSpeedSetMessage = {
  type: "pointer.speed.set";
  pointerSpeed: number;
};

export type AudioGetMessage = {
  type: "audio.get";
};

export type ServerCapabilities = {
  awake?: AwakeCapability;
  gestureDebug?: boolean;
  inputAck?: boolean;
  power?: PowerCapabilities;
  remoteLaunch?: boolean;
  sleep?: boolean;
  textTransfer?: boolean;
  volume?: boolean;
};

export type PointerHighlightSetMessage = {
  type: "pointer.highlight.set";
  enabled: boolean;
};

export type TextTransferTarget = {
  mode: "focused" | "configured" | "clipboard";
  displayName: string;
  available: boolean;
};

export type AwakeCapability = {
  canControl: boolean;
  active: boolean;
  mode: "off" | "indefinite" | "timed" | "expiration";
  expiresAt?: string | null;
};

export type PowerCapabilities = {
  lock: boolean;
  lockAvailability?: "notExplicitlyDisabled" | "disabledByPolicy" | "unavailable";
  blackoutDisplay: boolean;
  displayOff: boolean;
  screenSaver: boolean;
  screenSaverAvailable: boolean;
  signOut: boolean;
  restart: boolean;
  shutdown: boolean;
};

export type HostStatusMetadata = {
  appLaunchActions?: AppLaunchActionSummary[];
  defaultRemoteMode?: RemoteModeId;
  developerMode?: boolean;
  developerSessionId?: string;
  hostVersion?: string;
  webClientBuildId?: string;
  pcName?: string;
  pointerSpeed?: number;
  highlightPointer?: boolean;
  inputBlockedByElevation?: boolean;
  selectedAdapterName?: string;
  selectedIp?: string;
  selectedPort?: number;
  textTransferTarget?: TextTransferTarget;
  webSocketUrl?: string;
};

export type PairAcceptedMessage = {
  type: "pair.accepted";
  clientId: string;
  pcName: string;
  secret: string;
  paired: true;
  capabilities?: ServerCapabilities;
  host?: HostStatusMetadata;
};

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

export type PairRejectedMessage = {
  type: "pair.rejected";
  reason: PairRejectionReason;
  diagnosticCode?: string;
};

export type StatusMessage = {
  type: "status";
  connected: boolean;
  message?: string;
  pcName?: string;
  capabilities?: ServerCapabilities;
  host?: HostStatusMetadata;
};

export type HealthPongMessage = {
  type: "health.pong";
};

export type InputAckMessage = {
  type: "input.ack";
  seq?: number;
};

export type InputErrorMessage = {
  type: "input.error";
  seq?: number;
  code?: string;
  message: string;
};

export type PointerMoveMessage = {
  type: "pointer.move";
  seq?: number;
  dx: number;
  dy: number;
};

export type PointerButtonMessage = {
  type: "pointer.button";
  seq?: number;
  button: "left" | "right";
  action: "down" | "up" | "click";
};

export type PointerWheelMessage = {
  type: "pointer.wheel";
  seq?: number;
  dx: number;
  dy: number;
};

export type PointerZoomMessage = {
  type: "pointer.zoom";
  seq?: number;
  direction: "in" | "out";
};

export type KeyboardTextMessage = {
  type: "keyboard.text";
  seq?: number;
  text: string;
};

export type KeyboardSpecialMessage = {
  type: "keyboard.special";
  seq?: number;
  key: string;
  modifiers?: string[];
};

export type SystemSleepMessage = {
  type: "system.sleep";
};

export type AppLaunchActionKind = "browser" | "spotify" | "vlc" | "powerpoint" | "custom";

export type AppLaunchActionSummary = {
  id: string;
  label: string;
  kind: AppLaunchActionKind;
};

export type SystemPowerAction = "lock" | "blackoutDisplay" | "displayOff" | "screenSaver" | "signOut" | "restart" | "shutdown";

export type SystemPowerMessage = {
  type: "system.power";
  action: SystemPowerAction;
};

export type SystemPowerResultMessage = {
  type: "system.power.result";
  action: string;
  succeeded: boolean;
  code?: string;
  message: string;
};

export type AwakeSetMessage = {
  type: "awake.set";
  enabled: boolean;
};

export type AwakeResultMessage = {
  type: "awake.result";
  enabled: boolean;
  succeeded: boolean;
  code?: string;
  message: string;
};

export type RemoteLaunchAction = "openYoutube" | "startOrActivateKodi";

export type RemoteLaunchMessage = {
  type: "remote.launch";
  action: RemoteLaunchAction;
};

export type AppLaunchMessage = {
  type: "app.launch";
  actionId: string;
};

export type AppLaunchResultMessage = {
  type: "app.launch.result";
  actionId: string;
  succeeded: boolean;
  code?: string;
  message: string;
};

export type TextSendMessage = {
  type: "text.send";
  operationId: string;
  text: string;
  sendEnter: boolean;
};

export type TextSendResultMessage = {
  type: "text.send.result";
  operationId: string;
  succeeded: boolean;
  code?: string;
  message: string;
  deliveryKind?: "typed" | "pasted" | "clipboard";
};

export type AudioMuteToggleMessage = {
  type: "audio.mute.toggle";
};

export type AudioVolumeSetMessage = {
  type: "audio.volume.set";
  volume: number;
};

export type AudioStateMessage = {
  type: "audio.state";
  volume: number;
  muted: boolean;
};

export type ClientMessage =
  | PairHelloMessage
  | PairDisconnectMessage
  | DeviceRenameMessage
  | HealthPingMessage
  | StatusGetMessage
  | PointerSpeedSetMessage
  | PointerHighlightSetMessage
  | AudioGetMessage
  | PointerMoveMessage
  | PointerButtonMessage
  | PointerWheelMessage
  | PointerZoomMessage
  | KeyboardTextMessage
  | KeyboardSpecialMessage
  | SystemSleepMessage
  | SystemPowerMessage
  | AwakeSetMessage
  | RemoteLaunchMessage
  | AppLaunchMessage
  | TextSendMessage
  | AudioMuteToggleMessage
  | AudioVolumeSetMessage;

export type ServerMessage = PairAcceptedMessage | PairRejectedMessage | StatusMessage | HealthPongMessage | InputAckMessage | InputErrorMessage | SystemPowerResultMessage | AwakeResultMessage | AppLaunchResultMessage | TextSendResultMessage | AudioStateMessage;
