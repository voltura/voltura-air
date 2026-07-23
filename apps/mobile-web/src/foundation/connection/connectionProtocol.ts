import { getPcDisplayName } from "../pairing/pcDisplayName";
import type { PcProfile } from "./pcProfiles";
import type { AppLaunchActionSummary, AudioStateMessage, AwakeCapability, ClientMessage, HostStatusMetadata, PowerCapabilities, PresentationCapability, ServerCapabilities, ServerMessage, TextTransferTarget, UrlOpenCapability } from "../protocol/messages";
import { isRemoteModeId, normalizeRemoteMode } from "../settings/remoteSettings";

const movementAckIntervalMs = 200;
const maxPendingInputAcks = 64;

type ClientInputMessage = Extract<ClientMessage, { type: "pointer.move" | "pointer.button" | "pointer.wheel" | "pointer.zoom" | "keyboard.text" | "keyboard.special" }>;

export function getDisplayPcName(pc: PcProfile, hostName: string, screenshotMode = false): string {
  if (screenshotMode) {
    return "PC";
  }

  const trimmedHostName = hostName.trim();
  return pc.customName || trimmedHostName.length === 0 ? getPcDisplayName(pc) : trimmedHostName;
}

export function getPcUnavailableMessage(pc: PcProfile, screenshotMode = false): string {
  return `${getDisplayPcName(pc, "", screenshotMode)} is currently not available. Check that Voltura Air is running on the PC. Retrying...`;
}

export function getPcDisconnectedMessage(pc: PcProfile, reason: string | undefined, screenshotMode = false): string {
  const trimmedReason = reason?.trim();
  const baseMessage = trimmedReason && trimmedReason.length > 0 ? trimmedReason : `${getDisplayPcName(pc, "", screenshotMode)} disconnected.`;
  return /retrying/i.test(baseMessage) ? baseMessage : `${baseMessage} Retrying...`;
}

export function getInputAckTimeoutMessage(pc: PcProfile, screenshotMode = false): string {
  return `${getDisplayPcName(pc, "", screenshotMode)} stopped confirming input events. Retrying...`;
}

export function getInputErrorMessage(reason: string | undefined, pc: PcProfile, screenshotMode = false): string {
  const trimmedReason = reason?.trim();
  return trimmedReason && trimmedReason.length > 0 ? trimmedReason : `${getDisplayPcName(pc, "", screenshotMode)} could not process input.`;
}

export function diagnosticCodeForPairingReason(reason: string): string {
  const normalized = reason.replace(/[^a-z0-9]+/gi, "-").replace(/^-|-$/g, "").toUpperCase();
  return `VAIR-PAIR-${normalized.length > 0 ? normalized : "UNKNOWN"}`;
}

export function normalizeHostStatus(metadata: HostStatusMetadata | undefined): HostStatusMetadata | null {
  if (!metadata) {
    return null;
  }

  const normalized: HostStatusMetadata = {
    appLaunchActions: normalizeAppLaunchActions(metadata.appLaunchActions),
    defaultRemoteMode: metadata.defaultRemoteMode === undefined ? undefined : normalizeRemoteMode(metadata.defaultRemoteMode),
    developerMode: metadata.developerMode === true ? true : undefined,
    developerSessionId: normalizeOptionalString(metadata.developerSessionId),
    hostVersion: normalizeOptionalString(metadata.hostVersion),
    inputBlockedByElevation: typeof metadata.inputBlockedByElevation === "boolean" ? metadata.inputBlockedByElevation : undefined,
    webClientBuildId: normalizeOptionalString(metadata.webClientBuildId),
    pcName: normalizeOptionalString(metadata.pcName),
    pointerSpeed: normalizePointerSpeed(metadata.pointerSpeed),
    showModeButtons: typeof metadata.showModeButtons === "boolean" ? metadata.showModeButtons : undefined,
    customPointerEnabled: typeof metadata.customPointerEnabled === "boolean" ? metadata.customPointerEnabled : undefined,
    selectedAdapterName: normalizeOptionalString(metadata.selectedAdapterName),
    selectedIp: normalizeOptionalString(metadata.selectedIp),
    selectedPort: typeof metadata.selectedPort === "number" && Number.isFinite(metadata.selectedPort) ? metadata.selectedPort : undefined,
    textTransferTarget: normalizeTextTransferTarget(metadata.textTransferTarget),
    webSocketUrl: normalizeOptionalString(metadata.webSocketUrl)
  };

  return Object.values(normalized).some((value) => value !== undefined) ? normalized : null;
}

export function normalizeTextTransferTarget(value: unknown): TextTransferTarget | undefined {
  if (typeof value !== "object" || value === null) {
    return undefined;
  }

  const target = value as Partial<TextTransferTarget>;
  if ((target.mode !== "focused" && target.mode !== "configured" && target.mode !== "clipboard") ||
      typeof target.displayName !== "string" || target.displayName.trim().length < 1 || target.displayName.trim().length > 80 ||
      typeof target.available !== "boolean") {
    return undefined;
  }

  return { mode: target.mode, displayName: target.displayName.trim(), available: target.available };
}

export function normalizeAppLaunchActions(value: unknown): AppLaunchActionSummary[] | undefined {
  if (!Array.isArray(value)) {
    return undefined;
  }

  const actions: AppLaunchActionSummary[] = [];
  const ids = new Set<string>();
  for (const candidate of value) {
    if (actions.length >= 16 || typeof candidate !== "object" || candidate === null) {
      continue;
    }

    const { id, label, kind } = candidate as Partial<AppLaunchActionSummary>;
    if (typeof id !== "string" || id.length < 1 || id.length > 64 || !/^[a-zA-Z0-9._-]+$/.test(id) || ids.has(id) ||
      typeof label !== "string" || label.trim().length < 1 || label.trim().length > 10 ||
      !["browser", "spotify", "vlc", "powerpoint", "custom"].includes(kind ?? "")) {
      continue;
    }

    ids.add(id);
    actions.push({ id, label: label.trim(), kind: kind! });
  }

  return actions;
}

function normalizeOptionalString(value: string | undefined): string | undefined {
  const trimmed = value?.trim();
  if (!trimmed) {
    return undefined;
  }

  return trimmed;
}

export function normalizePointerSpeed(value: unknown): number | undefined {
  if (typeof value !== "number" || !Number.isFinite(value)) {
    return undefined;
  }

  return Math.max(10, Math.min(100, Math.round(value)));
}

export function parseServerMessage(data: unknown): ServerMessage | null {
  if (typeof data !== "string") {
    return null;
  }

  try {
    const parsed: unknown = JSON.parse(data);
    return isServerMessage(parsed) ? parsed : null;
  } catch {
    return null;
  }
}

function isServerMessage(value: unknown): value is ServerMessage {
  if (!isRecord(value) || typeof value.type !== "string") {
    return false;
  }

  switch (value.type) {
    case "pair.accepted":
      return hasOnlyFields(value, ["type", "clientId", "pcName", "paired", "capabilities", "host"]) &&
        isBoundedString(value.clientId, 128, false) &&
        isBoundedString(value.pcName, 120, false) &&
        value.paired === true &&
        isOptional(value, "capabilities", isServerCapabilities) &&
        isOptional(value, "host", isHostStatusMetadata);
    case "pair.challenge":
      return hasOnlyFields(value, ["type", "clientId", "challenge"]) &&
        isBoundedString(value.clientId, 128, false) &&
        isBoundedString(value.challenge, 512, false);
    case "pair.rejected":
      return hasOnlyFields(value, ["type", "reason"]) &&
        isBoundedString(value.reason, 120, false);
    case "status":
      return typeof value.connected === "boolean" &&
        isOptional(value, "message", isString) &&
        isOptional(value, "pcName", isString) &&
        isOptional(value, "capabilities", isServerCapabilities) &&
        isOptional(value, "host", isHostStatusMetadata);
    case "health.pong":
      return true;
    case "input.ack":
      return isOptional(value, "seq", isInputSequence);
    case "input.error":
      return isString(value.message) &&
        isOptional(value, "seq", isInputSequence) &&
        isOptional(value, "code", isString);
    case "presentation.command.result":
      return isOperationId(value.operationId) &&
        isOneOf(value.target, ["powerpoint", "google-slides", "pdf"]) &&
        isOneOf(value.action, ["next", "previous", "start", "end", "black", "pointer"]) &&
        typeof value.laserPointerActive === "boolean" &&
        isResultBase(value);
    case "presentation.report.save.result":
      return isOperationId(value.operationId) &&
        isOperationId(value.reportId) &&
        isResultBase(value);
    case "system.power.result":
      return isOperationId(value.operationId) && isBoundedString(value.action, 80, false) && isResultBase(value);
    case "awake.result":
      return isOperationId(value.operationId) && typeof value.enabled === "boolean" && isResultBase(value);
    case "app.launch.result":
      return isOperationId(value.operationId) && isAppLaunchActionId(value.actionId) && isResultBase(value);
    case "url.open.result":
      return isOperationId(value.operationId) && isResultBase(value) &&
        isOptional(value, "normalizedUrl", isString);
    case "text.send.result":
      return isOperationId(value.operationId) && isResultBase(value) &&
        isOptional(value, "deliveryKind", (candidate) => isOneOf(candidate, ["typed", "pasted", "clipboard"]));
    case "clipboard.get.result":
      return isOperationId(value.operationId) && isResultBase(value) &&
        isOptional(value, "text", isString);
    case "audio.state":
      return typeof value.volume === "number" && Number.isFinite(value.volume) && value.volume >= 0 && value.volume <= 100 &&
        typeof value.muted === "boolean";
    default:
      return false;
  }
}

function isServerCapabilities(value: unknown): boolean {
  if (!isRecord(value)) {
    return false;
  }

  return isOptional(value, "awake", isAwakeCapability) &&
    isOptional(value, "gestureDebug", isBoolean) &&
    isOptional(value, "inputAck", isBoolean) &&
    isOptional(value, "remoteInput", isBoolean) &&
    isOptional(value, "clipboardRead", isBoolean) &&
    isOptional(value, "presentation", isPresentationCapability) &&
    isOptional(value, "power", isPowerCapabilities) &&
    isOptional(value, "remoteLaunch", isBoolean) &&
    isOptional(value, "urlOpen", (candidate) => isBooleanCapability(candidate, "canOpen")) &&
    isOptional(value, "sleep", isBoolean) &&
    isOptional(value, "textTransfer", isBoolean) &&
    isOptional(value, "volume", isBoolean);
}

function isAwakeCapability(value: unknown): boolean {
  return isRecord(value) &&
    typeof value.canControl === "boolean" &&
    typeof value.active === "boolean" &&
    isOneOf(value.mode, ["off", "indefinite", "timed", "expiration"]) &&
    isOptional(value, "expiresAt", isString);
}

function isPowerCapabilities(value: unknown): boolean {
  if (!isRecord(value)) {
    return false;
  }

  const booleanFields = ["lock", "blackoutDisplay", "displayOff", "screenSaver", "screenSaverAvailable", "signOut", "restart", "shutdown"];
  return booleanFields.every((field) => typeof value[field] === "boolean") &&
    isOptional(value, "lockAvailability", (candidate) =>
      isOneOf(candidate, ["notExplicitlyDisabled", "disabledByPolicy", "unavailable"]));
}

function isHostStatusMetadata(value: unknown): boolean {
  if (!isRecord(value)) {
    return false;
  }

  const stringFields = ["developerSessionId", "hostVersion", "webClientBuildId", "pcName", "selectedAdapterName", "selectedIp", "webSocketUrl"];
  const booleanFields = ["developerMode", "customPointerEnabled", "inputBlockedByElevation", "showModeButtons"];
  return isOptional(value, "appLaunchActions", isAppLaunchActions) &&
    isOptional(value, "defaultRemoteMode", isRemoteModeId) &&
    stringFields.every((field) => isOptional(value, field, isString)) &&
    booleanFields.every((field) => isOptional(value, field, isBoolean)) &&
    isOptional(value, "pointerSpeed", (candidate) =>
      typeof candidate === "number" && Number.isFinite(candidate) && candidate >= 10 && candidate <= 100) &&
    isOptional(value, "selectedPort", (candidate) =>
      typeof candidate === "number" && Number.isInteger(candidate) && candidate > 0 && candidate <= 65535) &&
    isOptional(value, "textTransferTarget", (candidate) => normalizeTextTransferTarget(candidate) !== undefined);
}

function isAppLaunchActions(value: unknown): boolean {
  if (!Array.isArray(value) || value.length > 16) {
    return false;
  }

  const ids = new Set<string>();
  return value.every((candidate) => {
    if (!isRecord(candidate) || !isAppLaunchActionId(candidate.id) || ids.has(candidate.id) ||
      !isBoundedString(candidate.label, 10, false) ||
      !isOneOf(candidate.kind, ["browser", "spotify", "vlc", "powerpoint", "custom"])) {
      return false;
    }

    ids.add(candidate.id);
    return true;
  });
}

function isResultBase(value: Record<string, unknown>): boolean {
  return typeof value.succeeded === "boolean" && isString(value.message) && isOptional(value, "code", isString);
}

function isBooleanCapability(value: unknown, field: string): boolean {
  return isRecord(value) && typeof value[field] === "boolean";
}

function isPresentationCapability(value: unknown): boolean {
  return isRecord(value) &&
    typeof value.canControl === "boolean" &&
    typeof value.canSaveReports === "boolean" &&
    typeof value.laserPointerActive === "boolean";
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

function hasOnlyFields(value: Record<string, unknown>, allowedFields: readonly string[]): boolean {
  return Object.keys(value).every((field) => allowedFields.includes(field));
}

function isOptional(value: Record<string, unknown>, field: string, predicate: (candidate: unknown) => boolean): boolean {
  return !Object.hasOwn(value, field) || predicate(value[field]);
}

function isString(value: unknown): value is string {
  return typeof value === "string";
}

function isBoolean(value: unknown): boolean {
  return typeof value === "boolean";
}

function isBoundedString(value: unknown, maxLength: number, allowEmpty: boolean): value is string {
  return typeof value === "string" && value.length <= maxLength && (allowEmpty || value.trim().length > 0);
}

function isOperationId(value: unknown): value is string {
  return isBoundedString(value, 64, false) && /^[A-Za-z0-9-]+$/.test(value);
}

function isAppLaunchActionId(value: unknown): value is string {
  return isBoundedString(value, 64, false) && /^[A-Za-z0-9._-]+$/.test(value);
}

function isInputSequence(value: unknown): boolean {
  return typeof value === "number" && Number.isSafeInteger(value) && value > 0;
}

function isOneOf(value: unknown, allowed: readonly string[]): value is string {
  return typeof value === "string" && allowed.includes(value);
}

export function normalizeAudioState(message: { muted?: unknown; volume?: unknown }): AudioStateMessage {
  const volume = typeof message.volume === "number" && Number.isFinite(message.volume)
    ? message.volume
    : 0;

  return {
    type: "audio.state",
    volume: Math.max(0, Math.min(100, Math.round(volume))),
    muted: message.muted === true
  };
}

export const hasSleepCapability = (capabilities: ServerCapabilities | undefined) => capabilities?.sleep === true;
export const getAwakeCapability = (capabilities: ServerCapabilities | undefined): AwakeCapability | null => {
  const awake = capabilities?.awake;
  if (!awake || typeof awake.canControl !== "boolean" || typeof awake.active !== "boolean" ||
    !["off", "indefinite", "timed", "expiration"].includes(awake.mode)) {
    return null;
  }

  return {
    canControl: awake.canControl,
    active: awake.active,
    mode: awake.mode,
    expiresAt: typeof awake.expiresAt === "string" ? awake.expiresAt : undefined
  };
};
export const getPowerCapabilities = (capabilities: ServerCapabilities | undefined): PowerCapabilities | null => {
  const power = capabilities?.power;
  if (!power ||
    typeof power.lock !== "boolean" ||
    typeof power.blackoutDisplay !== "boolean" ||
    typeof power.displayOff !== "boolean" ||
    typeof power.screenSaver !== "boolean" ||
    typeof power.screenSaverAvailable !== "boolean" ||
    typeof power.signOut !== "boolean" ||
    typeof power.restart !== "boolean" ||
    typeof power.shutdown !== "boolean") {
    return null;
  }

  const lockAvailability = power.lockAvailability;
  if (lockAvailability === undefined || lockAvailability === "notExplicitlyDisabled" || lockAvailability === "disabledByPolicy" || lockAvailability === "unavailable") {
    return power;
  }

  return { ...power, lockAvailability: undefined };
};
export const hasVolumeCapability = (capabilities: ServerCapabilities | undefined) => capabilities?.volume === true;
export const hasInputAckCapability = (capabilities: ServerCapabilities | undefined) => capabilities?.inputAck === true;
export const getPresentationCapability = (capabilities: ServerCapabilities | undefined): PresentationCapability | undefined =>
  typeof capabilities?.presentation?.canControl === "boolean" &&
  typeof capabilities.presentation.canSaveReports === "boolean" &&
  typeof capabilities.presentation.laserPointerActive === "boolean"
    ? {
        canControl: capabilities.presentation.canControl,
        canSaveReports: capabilities.presentation.canSaveReports,
        laserPointerActive: capabilities.presentation.laserPointerActive
      }
    : undefined;
export const hasRemoteLaunchCapability = (capabilities: ServerCapabilities | undefined) => capabilities?.remoteLaunch === true;
export const hasTextTransferCapability = (capabilities: ServerCapabilities | undefined) => capabilities?.textTransfer === true;
export const getClipboardReadPermission = (capabilities: ServerCapabilities | undefined): boolean | undefined =>
  typeof capabilities?.clipboardRead === "boolean" ? capabilities.clipboardRead : undefined;
export const getUrlOpenCapability = (capabilities: ServerCapabilities | undefined): UrlOpenCapability | undefined =>
  typeof capabilities?.urlOpen?.canOpen === "boolean" ? { canOpen: capabilities.urlOpen.canOpen } : undefined;
export const hasGestureDebugCapability = (capabilities: ServerCapabilities | undefined) => capabilities?.gestureDebug === true;

export function shouldTrackInputAck(payload: ClientMessage, now: number, lastMovementAckAt: number): payload is ClientInputMessage {
  if (!isInputMessage(payload)) {
    return false;
  }

  return !isMovementInput(payload) || now - lastMovementAckAt >= movementAckIntervalMs;
}

function isInputMessage(payload: ClientMessage): payload is ClientInputMessage {
  return payload.type === "pointer.move" ||
    payload.type === "pointer.button" ||
    payload.type === "pointer.wheel" ||
    payload.type === "pointer.zoom" ||
    payload.type === "keyboard.text" ||
    payload.type === "keyboard.special";
}

export function isMovementInput(payload: ClientMessage): payload is Extract<ClientInputMessage, { type: "pointer.move" | "pointer.wheel" | "pointer.zoom" }> {
  return payload.type === "pointer.move" || payload.type === "pointer.wheel" || payload.type === "pointer.zoom";
}

export function isUserActivityMessage(payload: ClientMessage): boolean {
  return payload.type !== "health.ping" && payload.type !== "status.get" && payload.type !== "audio.get";
}

export function trimPendingInputAcks(pending: Map<number, number>): void {
  while (pending.size > maxPendingInputAcks) {
    const oldestSequence = pending.keys().next().value;
    if (oldestSequence === undefined) {
      return;
    }

    pending.delete(oldestSequence);
  }
}
