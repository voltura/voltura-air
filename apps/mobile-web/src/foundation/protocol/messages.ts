import type { RemoteModeId } from "../settings/remoteSettings";

export interface PairHelloMessage {
  type: "pair.hello";
  clientId: string;
  deviceName: string;
  platform?: string;
  browser?: string;
  displayMode?: "browser" | "installed" | "unknown";
  pairTokenId?: string | undefined;
  clientNonce?: string | undefined;
  reconnectPublicKey?: string | undefined;
}

export interface PairBootstrapProofMessage {
  type: "pair.bootstrap.proof";
  clientId: string;
  proof: string;
}

export interface PairProofMessage {
  type: "pair.proof";
  clientId: string;
  signature: string;
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

export interface AppearanceModeButtonsSetMessage {
  type: "appearance.mode-buttons.set";
  showModeButtons: boolean;
}

export interface AppearanceControlDepthSetMessage {
  type: "appearance.control-depth.set";
  controlDepth: boolean;
}

export interface CustomPointerSetMessage {
  type: "custom.pointer.set";
  enabled: boolean;
}

export interface AudioGetMessage {
  type: "audio.get";
}

export interface ServerCapabilities {
  remoteInput?: boolean;
  awake?: AwakeCapability;
  gestureDebug?: boolean;
  inputAck?: boolean;
  clipboardRead?: boolean;
  presentation?: PresentationCapability;
  power?: PowerCapabilities;
  remoteLaunch?: boolean;
  urlOpen?: UrlOpenCapability;
  sleep?: boolean;
  textTransfer?: boolean;
  volume?: boolean;
  customScreens?: CustomScreensCapability | null;
  screenView?: ScreenViewCapability | null;
}

export interface ScreenViewCapability {
  enabled: boolean;
  permissionGranted: boolean;
  canView: boolean;
  requiresRepair: boolean;
  encrypted: true;
  maxWidth: number;
  maxHeight: number;
  maxFramesPerSecond: number;
}

export interface ScreenViewSource {
  id: string;
  label: string;
  width: number;
  height: number;
  isPrimary: boolean;
}

export interface ScreenViewSourcesGetMessage { type: "screen.view.sources.get"; operationId: string; }
export interface ScreenViewStartMessage {
  type: "screen.view.start";
  operationId: string;
  displayId: string;
  clientSignature: string;
}
export interface ScreenViewAnswerMessage { type: "screen.view.answer"; operationId: string; answerSdp: string; clientSignature: string; }
export interface ScreenViewStopMessage { type: "screen.view.stop"; operationId: string; }
export interface ScreenViewSourceSetMessage { type: "screen.view.source.set"; operationId: string; displayId: string; }
export interface ScreenViewSourcesResultMessage {
  type: "screen.view.sources.result";
  operationId: string;
  succeeded: boolean;
  code?: string;
  message: string;
  sources: ScreenViewSource[];
}
export interface ScreenViewStartResultMessage {
  type: "screen.view.start.result";
  operationId: string;
  displayId: string;
  succeeded: boolean;
  code?: string;
  message: string;
  offerSdp?: string | null;
  hostSignature?: string | null;
}
export interface ScreenViewAnswerResultMessage {
  type: "screen.view.answer.result";
  operationId: string;
  succeeded: boolean;
  code?: string;
  message: string;
}
export interface ScreenViewStopResultMessage {
  type: "screen.view.stop.result";
  operationId: string;
  succeeded: boolean;
  code?: string;
  message: string;
}
export interface ScreenViewSourceResultMessage {
  type: "screen.view.source.result";
  operationId: string;
  displayId: string;
  succeeded: boolean;
  code?: string;
  message: string;
}

export interface CustomScreenSummary {
  id: string;
  name: string;
  revision: string;
}

export interface CustomScreensCapability {
  catalogRevision: string;
  screens: CustomScreenSummary[];
}

export interface CustomScreenLayoutOverride {
  order: number;
  visible: boolean;
  widthColumns?: number | null;
  size?: "compact" | "standard" | "wide" | "fill" | null;
  row?: number | null;
}

export interface CustomScreenButtonDefinition {
  id: string;
  name: string;
  label: string;
  icon: string;
  presentation: "iconLabel" | "icon" | "label";
  size: "compact" | "standard" | "wide" | "fill";
  repeat: boolean;
  row?: number;
  portrait?: CustomScreenLayoutOverride | null;
  landscape?: CustomScreenLayoutOverride | null;
  enabled: boolean;
  unavailableReason?: string | null;
}

export interface CustomScreenSectionDefinition {
  id: string;
  name: string;
  showHeader: boolean;
  widthColumns: number;
  heightMode: "content" | "fill";
  fillWeight: number;
  rowLimit: number;
  buttonAlignment:
    | "start"
    | "center"
    | "end"
    | "space-between"
    | "space-around"
    | "space-evenly";
  kind: "buttons" | "trackpad" | "volume" | "navigationRing" | "dpad";
  collapsible: boolean;
  initiallyExpanded: boolean;
  trackpadLeftClick: boolean;
  trackpadRightClick: boolean;
  trackpadButtonSide: "left" | "right";
  trackpadFullscreenControl: boolean;
  trackpadEnabled: boolean;
  trackpadUnavailableReason?: string | null;
  volumeEnabled: boolean;
  volumeUnavailableReason?: string | null;
  portrait?: CustomScreenLayoutOverride | null;
  landscape?: CustomScreenLayoutOverride | null;
  buttons: CustomScreenButtonDefinition[];
}

export interface CustomScreenDefinition {
  id: string;
  name: string;
  revision: string;
  orientationLayoutsEnabled: boolean;
  showNavigationHeader: boolean;
  sections: CustomScreenSectionDefinition[];
}

export interface DeviceViewportSetMessage {
  type: "device.viewport.set";
  width: number;
  height: number;
  orientation: "portrait" | "landscape";
}

export interface CustomScreenGetMessage {
  type: "custom.screen.get";
  operationId: string;
  screenId: string;
}

export interface CustomScreenGetResultMessage {
  type: "custom.screen.get.result";
  operationId: string;
  succeeded: boolean;
  screen?: CustomScreenDefinition;
  code?: string;
  message?: string;
}

export interface CustomScreenInvokeMessage {
  type: "custom.screen.invoke";
  operationId: string;
  screenId: string;
  screenRevision: string;
  buttonId: string;
}

export interface CustomScreenInvokeResultMessage {
  type: "custom.screen.invoke.result";
  operationId: string;
  screenId: string;
  buttonId: string;
  succeeded: boolean;
  code?: string;
  message: string;
}

export interface PresentationCapability {
  canControl: boolean;
  canSaveReports: boolean;
  laserPointerActive: boolean;
  powerPoint?: PowerPointCapability | null;
}

export interface PowerPointPresentation {
  runtimePresentationId: string;
  name: string;
  state: "ready" | "presenting";
  slideCount: number;
  currentSlideIndex?: number | null;
  currentShowPosition?: number | null;
  slideShowState: "ready" | "running" | "paused" | "black" | "white";
}

export interface PowerPointCapability {
  state: "ready" | "busy" | "unavailable";
  foregroundActivationSupported?: boolean | undefined;
  presentations: PowerPointPresentation[];
  availablePresentations?: AvailablePowerPointPresentation[] | undefined;
  session?: PowerPointSession | undefined;
}

export interface AvailablePowerPointPresentation {
  presentationId: string;
  title: string;
  fileName: string;
}

export interface PowerPointSession {
  state: "inactive" | "tracking" | "pending-review";
  runtimePresentationId?: string | null;
  presentationName?: string | null;
  ownerDeviceName?: string | null;
  isOwner: boolean;
  startedAt?: string | null;
  elapsedSeconds: number;
  breakActive: boolean;
  breakElapsedSeconds: number;
  currentSlideIndex?: number | null;
  slideCount: number;
  slideShowState: PowerPointPresentation["slideShowState"];
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
  expiresAt?: string | undefined;
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
  showModeButtons?: boolean | undefined;
  controlDepth?: boolean | undefined;
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
  paired: true;
  capabilities?: ServerCapabilities;
  host?: HostStatusMetadata;
  hostIdentity?: { publicKey: string; fingerprint: string } | undefined;
}

export interface PairChallengeMessage {
  type: "pair.challenge";
  clientId: string;
  challenge: string;
}

export interface PairBootstrapChallengeMessage {
  type: "pair.bootstrap.challenge";
  clientId: string;
  clientNonce: string;
  serverNonce: string;
  hostIdentity: { publicKey: string; fingerprint: string };
  proof: string;
}

export type PairRejectionReason =
  | "pair-first"
  | "invalid-token"
  | "expired-token"
  | "stale-token"
  | "device-revoked"
  | "invalid-proof"
  | "protocol-version-mismatch"
  | "rate-limited"
  | "invalid-message"
  | (string & {});

export interface PairRejectedMessage {
  type: "pair.rejected";
  reason: PairRejectionReason;
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

export type PresentationAction =
  | "next"
  | "previous"
  | "start"
  | "start-current"
  | "first"
  | "last"
  | "goto"
  | "end"
  | "black"
  | "white"
  | "pause"
  | "pointer"
  | "activate";

export interface PresentationCommandOptions {
  enabled?: boolean | undefined;
  runtimePresentationId?: string | undefined;
  slideNumber?: number | undefined;
}

export interface PresentationCommandMessage {
  type: "presentation.command";
  operationId: string;
  target: PresentationTarget;
  action: PresentationAction;
  enabled?: boolean | undefined;
  runtimePresentationId?: string | undefined;
  slideNumber?: number | undefined;
}

export interface PresentationCommandResultMessage {
  type: "presentation.command.result";
  operationId: string;
  target: PresentationTarget;
  action: PresentationAction;
  succeeded: boolean;
  code?: string;
  message: string;
  laserPointerActive: boolean;
  runtimePresentationId?: string | null | undefined;
  presentation?: PowerPointPresentation | null | undefined;
}

export interface PowerPointRefreshMessage {
  type: "presentation.powerpoint.refresh";
  operationId: string;
}

export interface PowerPointRefreshResultMessage {
  type: "presentation.powerpoint.refresh.result";
  operationId: string;
  succeeded: boolean;
  code?: string;
  message: string;
  state: PowerPointCapability["state"];
  presentations: PowerPointPresentation[];
}

export interface PowerPointLaunchMessage {
  type: "presentation.powerpoint.launch";
  operationId: string;
  presentationId: string;
}

export interface PowerPointLaunchResultMessage {
  type: "presentation.powerpoint.launch.result";
  operationId: string;
  presentationId: string;
  succeeded: boolean;
  code?: string;
  message: string;
  runtimePresentationId?: string | null | undefined;
  presentation?: PowerPointPresentation | null | undefined;
}

export type PresentationSessionAction = "start" | "break" | "save" | "discard";

export interface PresentationSessionMessage {
  type: "presentation.session";
  operationId: string;
  action: PresentationSessionAction;
  enabled?: boolean | undefined;
  runtimePresentationId?: string | undefined;
}

export interface PresentationSessionResultMessage {
  type: "presentation.session.result";
  operationId: string;
  action: PresentationSessionAction;
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
  operationId: string;
  action: SystemPowerAction;
}

export interface PresentationReportBreak {
  breakNumber: number;
  presentationElapsedSeconds: number;
  breakDurationSeconds: number;
  startedAt: string;
  endedAt: string;
  sessionSlideMinimum?: number;
  sessionSlideMaximum?: number;
  slideNumberAtStart?: number;
  slideNumberAtEnd?: number;
}

export interface PresentationReportSlide {
  slideNumber: number;
  durationSeconds?: number;
}

export interface PresentationReportSavePayload {
  reportId: string;
  target: PresentationTarget;
  startedAt: string;
  endedAt: string;
  utcOffsetMinutes: number;
  plannedDurationSeconds: number;
  presentationDurationSeconds: number;
  endedDuringBreak: boolean;
  breaks: PresentationReportBreak[];
  slides: PresentationReportSlide[];
}

export interface PresentationReportSaveMessage extends PresentationReportSavePayload {
  type: "presentation.report.save";
  operationId: string;
}

export interface PresentationReportSaveResultMessage {
  type: "presentation.report.save.result";
  operationId: string;
  reportId: string;
  succeeded: boolean;
  code?: string;
  message: string;
}

export interface SystemPowerResultMessage {
  type: "system.power.result";
  operationId: string;
  action: string;
  succeeded: boolean;
  code?: string;
  message: string;
}

export interface AwakeSetMessage {
  type: "awake.set";
  operationId: string;
  enabled: boolean;
}

export interface AwakeResultMessage {
  type: "awake.result";
  operationId: string;
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
  operationId: string;
  actionId: string;
}

export interface AppLaunchResultMessage {
  type: "app.launch.result";
  operationId: string;
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
  normalizedUrl?: string;
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
  text?: string;
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
  | PairProofMessage
  | PairBootstrapProofMessage
  | PairDisconnectMessage
  | DeviceRenameMessage
  | HealthPingMessage
  | StatusGetMessage
  | PointerSpeedSetMessage
  | AppearanceModeButtonsSetMessage
  | AppearanceControlDepthSetMessage
  | CustomPointerSetMessage
  | DeviceViewportSetMessage
  | CustomScreenGetMessage
  | CustomScreenInvokeMessage
  | AudioGetMessage
  | PointerMoveMessage
  | PointerButtonMessage
  | PointerWheelMessage
  | PointerZoomMessage
  | KeyboardTextMessage
  | KeyboardSpecialMessage
  | PresentationCommandMessage
  | PowerPointRefreshMessage
  | PowerPointLaunchMessage
  | PresentationSessionMessage
  | PresentationReportSaveMessage
  | SystemSleepMessage
  | SystemPowerMessage
  | AwakeSetMessage
  | RemoteLaunchMessage
  | AppLaunchMessage
  | UrlOpenMessage
  | TextSendMessage
  | ClipboardGetMessage
  | AudioMuteToggleMessage
  | AudioVolumeSetMessage
  | ScreenViewSourcesGetMessage
  | ScreenViewStartMessage
  | ScreenViewAnswerMessage
  | ScreenViewSourceSetMessage
  | ScreenViewStopMessage;

export type ServerMessage = PairAcceptedMessage | PairChallengeMessage | PairBootstrapChallengeMessage | PairRejectedMessage | StatusMessage | HealthPongMessage | InputAckMessage | InputErrorMessage | PresentationCommandResultMessage | PowerPointRefreshResultMessage | PowerPointLaunchResultMessage | PresentationSessionResultMessage | PresentationReportSaveResultMessage | SystemPowerResultMessage | AwakeResultMessage | AppLaunchResultMessage | UrlOpenResultMessage | TextSendResultMessage | ClipboardGetResultMessage | AudioStateMessage | CustomScreenGetResultMessage | CustomScreenInvokeResultMessage | ScreenViewSourcesResultMessage | ScreenViewStartResultMessage | ScreenViewAnswerResultMessage | ScreenViewSourceResultMessage | ScreenViewStopResultMessage;
