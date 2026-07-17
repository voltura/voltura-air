import { getPcDisplayName } from "../pcDisplayName";
import type { PcProfile } from "../pcProfiles";
import type { AppLaunchActionSummary, AudioStateMessage, AwakeCapability, ClientMessage, HostStatusMetadata, PowerCapabilities, PresentationCapability, ServerCapabilities, ServerMessage, TextTransferTarget, UrlOpenCapability } from "../protocol";
import { normalizeRemoteMode } from "../remoteSettings";

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
    return JSON.parse(data) as ServerMessage;
  } catch {
    return null;
  }
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
  typeof capabilities?.presentation?.canControl === "boolean"
    ? { canControl: capabilities.presentation.canControl }
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
