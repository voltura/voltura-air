import { getPcDisplayName } from "../pcDisplayName";
import type { PcProfile } from "../pcProfiles";
import type { AudioStateMessage, ClientMessage, HostStatusMetadata, ServerCapabilities, ServerMessage } from "../protocol";
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
  const baseMessage = reason?.trim() || `${getDisplayPcName(pc, "", screenshotMode)} disconnected.`;
  return /retrying/i.test(baseMessage) ? baseMessage : `${baseMessage} Retrying...`;
}

export function getInputAckTimeoutMessage(pc: PcProfile, screenshotMode = false): string {
  return `${getDisplayPcName(pc, "", screenshotMode)} stopped confirming input events. Retrying...`;
}

export function getInputErrorMessage(reason: string | undefined, pc: PcProfile, screenshotMode = false): string {
  return reason?.trim() || `${getDisplayPcName(pc, "", screenshotMode)} could not process input.`;
}

export function diagnosticCodeForPairingReason(reason: string): string {
  const normalized = reason.replace(/[^a-z0-9]+/gi, "-").replace(/^-|-$/g, "").toUpperCase();
  return `VAIR-PAIR-${normalized || "UNKNOWN"}`;
}

export function normalizeHostStatus(metadata: HostStatusMetadata | undefined): HostStatusMetadata | null {
  if (!metadata) {
    return null;
  }

  const normalized: HostStatusMetadata = {
    defaultRemoteMode: metadata.defaultRemoteMode === undefined ? undefined : normalizeRemoteMode(metadata.defaultRemoteMode),
    developerMode: metadata.developerMode === true ? true : undefined,
    developerSessionId: normalizeOptionalString(metadata.developerSessionId),
    hostVersion: normalizeOptionalString(metadata.hostVersion),
    pcName: normalizeOptionalString(metadata.pcName),
    pointerSpeed: normalizePointerSpeed(metadata.pointerSpeed),
    selectedAdapterName: normalizeOptionalString(metadata.selectedAdapterName),
    selectedIp: normalizeOptionalString(metadata.selectedIp),
    selectedPort: Number.isFinite(metadata.selectedPort) ? metadata.selectedPort : undefined,
    webSocketUrl: normalizeOptionalString(metadata.webSocketUrl)
  };

  return Object.values(normalized).some((value) => value !== undefined) ? normalized : null;
}

function normalizeOptionalString(value: string | undefined): string | undefined {
  const trimmed = value?.trim();
  return trimmed ? trimmed : undefined;
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

export function normalizeAudioState(message: AudioStateMessage): AudioStateMessage {
  return {
    type: "audio.state",
    volume: Math.max(0, Math.min(100, Math.round(message.volume))),
    muted: message.muted === true
  };
}

export const hasSleepCapability = (capabilities: ServerCapabilities | undefined) => capabilities?.sleep === true;
export const hasVolumeCapability = (capabilities: ServerCapabilities | undefined) => capabilities?.volume === true;
export const hasInputAckCapability = (capabilities: ServerCapabilities | undefined) => capabilities?.inputAck === true;
export const hasRemoteLaunchCapability = (capabilities: ServerCapabilities | undefined) => capabilities?.remoteLaunch === true;
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
    const oldestSequence = pending.keys().next().value as number | undefined;
    if (oldestSequence === undefined) {
      return;
    }

    pending.delete(oldestSequence);
  }
}
