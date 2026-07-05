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

export type AudioGetMessage = {
  type: "audio.get";
};

export type ServerCapabilities = {
  gestureDebug?: boolean;
  inputAck?: boolean;
  sleep?: boolean;
  volume?: boolean;
};

export type HostStatusMetadata = {
  defaultRemoteMode?: RemoteModeId;
  developerMode?: boolean;
  developerSessionId?: string;
  hostVersion?: string;
  pcName?: string;
  selectedAdapterName?: string;
  selectedIp?: string;
  selectedPort?: number;
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
  | AudioGetMessage
  | PointerMoveMessage
  | PointerButtonMessage
  | PointerWheelMessage
  | PointerZoomMessage
  | KeyboardTextMessage
  | KeyboardSpecialMessage
  | SystemSleepMessage
  | AudioMuteToggleMessage
  | AudioVolumeSetMessage;

export type ServerMessage = PairAcceptedMessage | PairRejectedMessage | StatusMessage | HealthPongMessage | InputAckMessage | InputErrorMessage | AudioStateMessage;
