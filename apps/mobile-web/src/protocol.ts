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

export type StatusPingMessage = {
  type: "status.ping";
};

export type ServerCapabilities = {
  sleep?: boolean;
  volume?: boolean;
};

export type PairAcceptedMessage = {
  type: "pair.accepted";
  clientId: string;
  pcName: string;
  secret: string;
  paired: true;
  capabilities?: ServerCapabilities;
};

export type PairRejectedMessage = {
  type: "pair.rejected";
  reason: string;
};

export type StatusMessage = {
  type: "status";
  connected: boolean;
  message?: string;
  pcName?: string;
  capabilities?: ServerCapabilities;
};

export type StatusPongMessage = {
  type: "status.pong";
  pcName: string;
  capabilities?: ServerCapabilities;
};

export type PointerMoveMessage = {
  type: "pointer.move";
  dx: number;
  dy: number;
};

export type PointerButtonMessage = {
  type: "pointer.button";
  button: "left" | "right";
  action: "down" | "up" | "click";
};

export type PointerWheelMessage = {
  type: "pointer.wheel";
  dx: number;
  dy: number;
};

export type PointerZoomMessage = {
  type: "pointer.zoom";
  direction: "in" | "out";
};

export type KeyboardTextMessage = {
  type: "keyboard.text";
  text: string;
};

export type KeyboardSpecialMessage = {
  type: "keyboard.special";
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
  | StatusPingMessage
  | PointerMoveMessage
  | PointerButtonMessage
  | PointerWheelMessage
  | PointerZoomMessage
  | KeyboardTextMessage
  | KeyboardSpecialMessage
  | SystemSleepMessage
  | AudioMuteToggleMessage
  | AudioVolumeSetMessage;

export type ServerMessage = PairAcceptedMessage | PairRejectedMessage | StatusMessage | StatusPongMessage | AudioStateMessage;
