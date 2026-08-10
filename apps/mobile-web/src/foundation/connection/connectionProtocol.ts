import { getPcDisplayName } from "../pairing/pcDisplayName";
import type { PcProfile } from "./pcProfiles";
import type { AppLaunchActionSummary, AudioStateMessage, AwakeCapability, ClientMessage, HostStatusMetadata, PowerCapabilities, PresentationCapability, ServerCapabilities, ServerMessage, TextTransferTarget, UrlOpenCapability } from "../protocol/messages";
import { isRemoteModeId, normalizeRemoteMode } from "../settings/remoteSettings";

const movementAckIntervalMs = 200;
const maxPendingInputAcks = 64;

type ClientInputMessage = Extract<ClientMessage, { type: "pointer.move" | "pointer.button" | "pointer.wheel" | "pointer.zoom" | "screen.pointer.move" | "screen.pointer.button" | "screen.pointer.wheel" | "keyboard.text" | "keyboard.special" }>;

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
    controlDepth: typeof metadata.controlDepth === "boolean" ? metadata.controlDepth : undefined,
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
      return hasOnlyFields(value, ["type", "clientId", "pcName", "paired", "capabilities", "host", "hostIdentity"]) &&
        isBoundedString(value.clientId, 128, false) &&
        isBoundedString(value.pcName, 120, false) &&
        value.paired === true &&
        isOptional(value, "capabilities", isServerCapabilities) &&
        isOptional(value, "host", isHostStatusMetadata) &&
        isOptional(value, "hostIdentity", isHostIdentity);
    case "pair.challenge":
      return hasOnlyFields(value, ["type", "clientId", "challenge"]) &&
        isBoundedString(value.clientId, 128, false) &&
        isBoundedString(value.challenge, 512, false);
    case "pair.bootstrap.challenge":
      return hasOnlyFields(value, ["type", "clientId", "clientNonce", "serverNonce", "hostIdentity", "proof"]) &&
        isBoundedString(value.clientId, 128, false) &&
        isBoundedString(value.clientNonce, 43, false) &&
        isBoundedString(value.serverNonce, 43, false) &&
        isHostIdentity(value.hostIdentity) &&
        isBoundedString(value.proof, 43, false);
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
        isOneOf(value.action, ["next", "previous", "start", "start-current", "first", "last", "goto", "end", "black", "white", "pause", "pointer", "activate"]) &&
        typeof value.laserPointerActive === "boolean" &&
        isOptional(value, "runtimePresentationId", (candidate) =>
          candidate === null || isOperationId(candidate)) &&
        isOptional(value, "presentation", (candidate) =>
          candidate === null || isPowerPointPresentation(candidate)) &&
        isResultBase(value);
    case "presentation.powerpoint.refresh.result":
      return isOperationId(value.operationId) &&
        isResultBase(value) &&
        isOneOf(value.state, ["ready", "busy", "unavailable"]) &&
        isPowerPointPresentations(value.presentations);
    case "presentation.powerpoint.launch.result":
      return isOperationId(value.operationId) &&
        isOperationId(value.presentationId) &&
        isOptional(value, "runtimePresentationId", (candidate) =>
          candidate === null || isOperationId(candidate)) &&
        isOptional(value, "presentation", (candidate) =>
          candidate === null || isPowerPointPresentation(candidate)) &&
        isResultBase(value);
    case "presentation.session.result":
      return isOperationId(value.operationId) &&
        isOneOf(value.action, ["start", "break", "save", "discard"]) &&
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
    case "custom.screen.get.result":
      return isOperationId(value.operationId) &&
        typeof value.succeeded === "boolean" &&
        isOptional(value, "screen", isCustomScreenDefinition) &&
        isOptional(value, "code", isString) &&
        isOptional(value, "message", isString) &&
        (value.succeeded ? isCustomScreenDefinition(value.screen) : isString(value.message));
    case "custom.screen.invoke.result":
      return isOperationId(value.operationId) &&
        isCustomScreenId(value.screenId) &&
        isCustomScreenId(value.buttonId) &&
        isResultBase(value);
    case "screen.view.sources.result":
      return hasOnlyFields(value, ["type", "operationId", "succeeded", "code", "message", "sources"]) &&
        isOperationId(value.operationId) && isResultBase(value) && Array.isArray(value.sources) &&
        value.sources.length <= 16 && value.sources.every(isScreenViewSource);
    case "screen.view.start.result":
      return hasOnlyFields(value, ["type", "operationId", "displayId", "succeeded", "code", "message", "offerSdp", "hostSignature", "iceServers", "turnExpiresAt", "relayUsageBytes", "relayUsageCheckedAt", "relayScreenQuality"]) &&
        isOperationId(value.operationId) && isBoundedString(value.displayId, 80, false) && isResultBase(value) &&
        isOptional(value, "offerSdp", (candidate) => candidate === null || isBoundedString(candidate, 32 * 1024, false)) &&
        isOptional(value, "hostSignature", (candidate) => candidate === null || isBoundedString(candidate, 128, false)) &&
        isOptional(value, "iceServers", (candidate) => candidate === null || isRelayIceServers(candidate)) &&
        isOptional(value, "turnExpiresAt", (candidate) => candidate === null || isBoundedString(candidate, 40, false)) &&
        isOptional(value, "relayUsageBytes", (candidate) => candidate === null || typeof candidate === "number" && Number.isSafeInteger(candidate) && candidate >= 0) &&
        isOptional(value, "relayUsageCheckedAt", (candidate) => candidate === null || isBoundedString(candidate, 40, false)) &&
        isOptional(value, "relayScreenQuality", (candidate) => candidate === null || isBoundedString(candidate, 32, false));
    case "screen.view.answer.result":
      return hasOnlyFields(value, ["type", "operationId", "succeeded", "code", "message"]) &&
        isOperationId(value.operationId) && isResultBase(value);
    case "screen.view.source.result":
      return hasOnlyFields(value, ["type", "operationId", "displayId", "succeeded", "code", "message"]) &&
        isOperationId(value.operationId) && isBoundedString(value.displayId, 80, false) && isResultBase(value);
    case "screen.view.stop.result":
      return hasOnlyFields(value, ["type", "operationId", "succeeded", "code", "message"]) &&
        isOperationId(value.operationId) && isResultBase(value);
    case "screen.view.ended":
      return hasOnlyFields(value, ["type", "reason", "message"]) &&
        isOneOf(value.reason, ["host-stopped", "permission-revoked"]) &&
        isBoundedString(value.message, 240, false);
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
    case "file.session.open.result":
    case "file.page.get.result":
    case "file.navigate.result":
    case "file.refresh.result":
    case "file.sort.result":
    case "file.properties.get.result":
    case "file.clipboard.set.result":
    case "file.open.result":
    case "file.job.create.result":
    case "file.job.control.result":
    case "file.job.reorder.result":
    case "file.job.conflict.resolve.result":
    case "file.jobs.status":
      return isFileManagerServerMessage(value);
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
    isOptional(value, "customScreens", (candidate) =>
      candidate === null || isCustomScreensCapability(candidate)) &&
    isOptional(value, "screenView", (candidate) =>
      candidate === null || isScreenViewCapability(candidate)) &&
    isOptional(value, "fileManager", (candidate) =>
      candidate === null || isFileManagerCapability(candidate)) &&
    isOptional(value, "urlOpen", (candidate) => isBooleanCapability(candidate, "canOpen")) &&
    isOptional(value, "sleep", isBoolean) &&
    isOptional(value, "textTransfer", isBoolean) &&
    isOptional(value, "volume", isBoolean);
}

function isFileManagerCapability(value: unknown): boolean {
  return isRecord(value) && typeof value.canBrowse === "boolean" && typeof value.canModify === "boolean" &&
    typeof value.hidesProtectedSystemItems === "boolean" &&
    Number.isInteger(value.maxPageSize) && (value.maxPageSize as number) >= 1 && (value.maxPageSize as number) <= 100;
}

function isFileManagerServerMessage(value: Record<string, unknown>): boolean {
  if (value.type === "file.jobs.status") {
    return isOptional(value, "operationId", isOperationId) && Array.isArray(value.jobs) && value.jobs.length <= 32 && value.jobs.every(isFileJob);
  }
  if (!isOperationId(value.operationId) || !isResultBase(value)) {return false;}
  if (value.type === "file.session.open.result") {
    return isOptional(value, "session", (candidate) => candidate === null || isFileSession(candidate)) &&
      (value.succeeded === false || isFileSession(value.session));
  }
  if (value.type === "file.page.get.result" || value.type === "file.navigate.result" || value.type === "file.refresh.result" || value.type === "file.sort.result") {
    return isOptional(value, "page", (candidate) => candidate === null || isFilePanelPage(candidate)) &&
      (value.succeeded === false || isFilePanelPage(value.page));
  }
  if (value.type === "file.properties.get.result") {
    return isOptional(value, "properties", (candidate) => candidate === null || isFileProperties(candidate)) &&
      (value.succeeded === false || isFileProperties(value.properties));
  }
  if (value.type === "file.job.create.result") {
    return isOptional(value, "job", (candidate) => candidate === null || isFileJob(candidate));
  }
  return true;
}

function isFileSession(value: unknown): boolean {
  return isRecord(value) && isBoundedString(value.sessionId, 512, false) &&
    Array.isArray(value.drives) && value.drives.length <= 64 && value.drives.every((item) =>
      isRecord(item) && isBoundedString(item.id, 512, false) && isBoundedString(item.label, 260, false) && isBoundedString(item.driveType, 32, false)) &&
    Array.isArray(value.shortcuts) && value.shortcuts.length <= 16 && value.shortcuts.every((item) =>
      isRecord(item) && isBoundedString(item.id, 512, false) && isBoundedString(item.label, 80, false)) &&
    isFilePanelPage(value.left) && isFilePanelPage(value.right);
}

function isFilePanelPage(value: unknown): boolean {
  return isRecord(value) && isOneOf(value.panel, ["left", "right"]) &&
    isBoundedString(value.revision, 512, false) && isBoundedString(value.displayPath, 32767, false) &&
    isOptional(value, "parentId", (candidate) => candidate === null || isBoundedString(candidate, 512, false)) &&
    isOptional(value, "driveId", (candidate) => candidate === null || isBoundedString(candidate, 512, false)) &&
    isOneOf(value.sortBy, ["name", "size", "type", "modified"]) && typeof value.descending === "boolean" &&
    Number.isInteger(value.totalCount) && (value.totalCount as number) >= 0 &&
    Array.isArray(value.entries) && value.entries.length <= 100 && value.entries.every(isFileEntry) &&
    isOptional(value, "continuation", (candidate) => candidate === null || isBoundedString(candidate, 512, false));
}

function isFileEntry(value: unknown): boolean {
  return isRecord(value) && isBoundedString(value.id, 512, false) && isBoundedString(value.name, 255, false) &&
    isOneOf(value.kind, ["file", "folder"]) && isBoundedString(value.extension, 255, true) &&
    isOptional(value, "size", (candidate) => candidate === null || Number.isSafeInteger(candidate) && (candidate as number) >= 0) &&
    isBoundedString(value.modifiedUtc, 64, false) && Array.isArray(value.attributes) && value.attributes.length <= 8 &&
    value.attributes.every((attribute) => isBoundedString(attribute, 32, false));
}

function isFileProperties(value: unknown): boolean {
  return isRecord(value) && isBoundedString(value.entryId, 512, false) && isBoundedString(value.name, 255, false) &&
    isBoundedString(value.fullPath, 32767, false) && isOneOf(value.kind, ["file", "folder"]) &&
    isBoundedString(value.extension, 255, true) && isBoundedString(value.createdUtc, 64, false) &&
    isBoundedString(value.modifiedUtc, 64, false) && isBoundedString(value.accessedUtc, 64, false) &&
    Array.isArray(value.attributes) && value.attributes.length <= 8;
}

function isFileJob(value: unknown): boolean {
  return isRecord(value) && isBoundedString(value.jobId, 512, false) && isOneOf(value.operation, ["copy", "move", "paste", "delete", "rename"]) &&
    isOneOf(value.state, ["queued", "preparing", "running", "paused", "needs-attention", "canceling", "completed", "failed", "canceled", "interrupted"]) &&
    ["queuePosition", "itemsCompleted", "itemsTotal", "bytesCompleted", "bytesTotal"].every((field) =>
      Number.isFinite(value[field]) && (value[field] as number) >= 0) &&
    typeof value.canPause === "boolean" && typeof value.canResume === "boolean" && typeof value.canCancel === "boolean";
}

function isHostIdentity(value: unknown): boolean {
  return isRecord(value) &&
    isBoundedString(value.publicKey, 128, false) &&
    /^[A-Za-z0-9_-]{87}$/.test(value.publicKey) &&
    isBoundedString(value.fingerprint, 64, false) &&
    /^[A-Za-z0-9_-]{22}$/.test(value.fingerprint);
}

function isScreenViewCapability(value: unknown): boolean {
  return isRecord(value) &&
    typeof value.enabled === "boolean" &&
    typeof value.permissionGranted === "boolean" &&
    typeof value.canView === "boolean" &&
    typeof value.requiresRepair === "boolean" &&
    value.encrypted === true &&
    value.maxWidth === 1920 &&
    value.maxHeight === 1080 &&
    value.maxFramesPerSecond === 30 &&
    isOptional(value, "directPointer", (candidate) =>
      isRecord(candidate) && typeof candidate.permissionGranted === "boolean");
}

function isScreenViewSource(value: unknown): boolean {
  return isRecord(value) && hasOnlyFields(value, ["id", "label", "width", "height", "isPrimary"]) &&
    isBoundedString(value.id, 80, false) && isBoundedString(value.label, 120, false) &&
    Number.isInteger(value.width) && (value.width as number) > 0 && (value.width as number) <= 16384 &&
    Number.isInteger(value.height) && (value.height as number) > 0 && (value.height as number) <= 16384 &&
    typeof value.isPrimary === "boolean";
}

function isCustomScreensCapability(value: unknown): boolean {
  return isRecord(value) &&
    isCustomScreenId(value.catalogRevision) &&
    Array.isArray(value.screens) &&
    value.screens.length <= 128 &&
    value.screens.every((candidate) =>
      isRecord(candidate) &&
      isCustomScreenId(candidate.id) &&
      isBoundedString(candidate.name, 24, false) &&
      isCustomScreenId(candidate.revision));
}

function isCustomScreenDefinition(value: unknown): boolean {
  return isRecord(value) &&
    isCustomScreenId(value.id) &&
    isBoundedString(value.name, 24, false) &&
    isCustomScreenId(value.revision) &&
    typeof value.orientationLayoutsEnabled === "boolean" &&
    typeof value.showNavigationHeader === "boolean" &&
    Array.isArray(value.sections) &&
    value.sections.length <= 64 &&
    value.sections.every(isCustomScreenSection);
}

function isCustomScreenSection(value: unknown): boolean {
  return isRecord(value) &&
    isCustomScreenId(value.id) &&
    isBoundedString(value.name, 20, false) &&
    typeof value.showHeader === "boolean" &&
    isOneOf(value.widthColumns, [3, 4, 6, 8, 9, 12]) &&
    isOneOf(value.heightMode, ["content", "fill"]) &&
    typeof value.fillWeight === "number" &&
    Number.isInteger(value.fillWeight) &&
    value.fillWeight >= 1 &&
    value.fillWeight <= 4 &&
    typeof value.rowLimit === "number" &&
    Number.isInteger(value.rowLimit) &&
    value.rowLimit >= 0 &&
    value.rowLimit <= 3 &&
    isOneOf(value.buttonAlignment, [
      "start",
      "center",
      "end",
      "space-between",
      "space-around",
      "space-evenly"
    ]) &&
    isOneOf(value.kind, ["buttons", "trackpad", "volume", "navigationRing", "dpad"]) &&
    typeof value.collapsible === "boolean" &&
    typeof value.initiallyExpanded === "boolean" &&
    typeof value.trackpadLeftClick === "boolean" &&
    typeof value.trackpadRightClick === "boolean" &&
    isOneOf(value.trackpadButtonSide, ["left", "right"]) &&
    typeof value.trackpadFullscreenControl === "boolean" &&
    typeof value.trackpadEnabled === "boolean" &&
    isOptional(value, "trackpadUnavailableReason", (candidate) =>
      candidate === null || isBoundedString(candidate, 300, false)) &&
    typeof value.volumeEnabled === "boolean" &&
    isOptional(value, "volumeUnavailableReason", (candidate) =>
      candidate === null || isBoundedString(candidate, 300, false)) &&
    isOptional(value, "portrait", isNullableCustomScreenOverride) &&
    isOptional(value, "landscape", isNullableCustomScreenOverride) &&
    Array.isArray(value.buttons) &&
    value.buttons.length <= 256 &&
    value.buttons.every(isCustomScreenButton) &&
    (value.kind === "buttons" || value.buttons.length === 0) &&
    (value.kind !== "volume" || isOneOf(value.widthColumns, [3, 6, 9, 12])) &&
    ((value.kind !== "navigationRing" && value.kind !== "dpad") || (
      isOneOf(value.widthColumns, [6, 8, 9, 12]) &&
      hasAllowedCustomScreenOverrideWidth(value.portrait, [6, 8, 9, 12]) &&
      hasAllowedCustomScreenOverrideWidth(value.landscape, [6, 8, 9, 12])));
}

function hasAllowedCustomScreenOverrideWidth(
  value: unknown,
  widths: readonly number[]
): boolean {
  return value === undefined || value === null || (
    isRecord(value) &&
    (value.widthColumns === undefined ||
      value.widthColumns === null ||
      widths.includes(value.widthColumns as number))
  );
}

function isCustomScreenButton(value: unknown): boolean {
  return isRecord(value) &&
    isCustomScreenId(value.id) &&
    isBoundedString(value.name, 24, false) &&
    isBoundedString(value.label, 16, true) &&
    isBoundedString(value.icon, 40, false) &&
    isOneOf(value.presentation, ["iconLabel", "icon", "label"]) &&
    isOneOf(value.size, ["compact", "standard", "wide", "fill"]) &&
    typeof value.repeat === "boolean" &&
    isOptional(value, "row", (candidate) =>
      typeof candidate === "number" &&
      Number.isInteger(candidate) &&
      candidate >= 0 &&
      candidate <= 3) &&
    typeof value.enabled === "boolean" &&
    isOptional(value, "portrait", isNullableCustomScreenOverride) &&
    isOptional(value, "landscape", isNullableCustomScreenOverride) &&
    isOptional(value, "unavailableReason", (candidate) => candidate === null || isBoundedString(candidate, 300, false)) &&
    isOptional(value, "confirmation", (candidate) => candidate === null || isOneOf(candidate, ["confirm", "hold"])) &&
    isOptional(value, "confirmationMessage", (candidate) => candidate === null || isBoundedString(candidate, 300, false)) &&
    isOptional(value, "laserPointerColor", (candidate) =>
      candidate === null || isOneOf(candidate, ["default", "red", "green", "blue"]));
}

function isNullableCustomScreenOverride(value: unknown): boolean {
  return value === null || (
    isRecord(value) &&
    typeof value.order === "number" &&
    Number.isInteger(value.order) &&
    value.order >= 0 &&
    typeof value.visible === "boolean" &&
    isOptional(value, "widthColumns", (candidate) =>
      candidate === null || isOneOf(candidate, [3, 4, 6, 8, 9, 12])) &&
    isOptional(value, "size", (candidate) =>
      candidate === null || isOneOf(candidate, ["compact", "standard", "wide", "fill"]))
  );
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
  const booleanFields = ["developerMode", "customPointerEnabled", "inputBlockedByElevation", "showModeButtons", "controlDepth"];
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
    typeof value.laserPointerActive === "boolean" &&
    isOptional(value, "laserPointerColor", (candidate) =>
      candidate === null || isOneOf(candidate, ["red", "green", "blue"])) &&
    isOptional(value, "laserPointerDefaultColor", (candidate) =>
      isOneOf(candidate, ["red", "green", "blue"])) &&
    isOptional(value, "powerPoint", (candidate) =>
      candidate === null || isPowerPointCapability(candidate));
}

function isPowerPointCapability(value: unknown): boolean {
  return isRecord(value) &&
    isOneOf(value.state, ["ready", "busy", "unavailable"]) &&
    isOptional(value, "foregroundActivationSupported", (candidate) =>
      typeof candidate === "boolean") &&
    isPowerPointPresentations(value.presentations) &&
    isOptional(value, "availablePresentations", isAvailablePowerPointPresentations) &&
    isOptional(value, "session", isPowerPointSession);
}

function isAvailablePowerPointPresentations(value: unknown): boolean {
  return Array.isArray(value) &&
    value.length <= 100 &&
    value.every((candidate) =>
      isRecord(candidate) &&
      isOperationId(candidate.presentationId) &&
      isBoundedString(candidate.title, 300, false) &&
      isBoundedString(candidate.fileName, 260, false));
}

function isPowerPointSession(value: unknown): boolean {
  if (!isRecord(value)) {
    return false;
  }

  return isOneOf(value.state, ["inactive", "tracking", "pending-review"]) &&
    isOptional(value, "runtimePresentationId", (candidate) =>
      candidate === null || isOperationId(candidate)) &&
    isOptional(value, "presentationName", (candidate) =>
      candidate === null || isBoundedString(candidate, 120, false)) &&
    isOptional(value, "ownerDeviceName", (candidate) =>
      candidate === null || isBoundedString(candidate, 120, false)) &&
    typeof value.isOwner === "boolean" &&
    isOptional(value, "startedAt", (candidate) => candidate === null || isString(candidate)) &&
    isNonNegativeNumber(value.elapsedSeconds) &&
    typeof value.breakActive === "boolean" &&
    isNonNegativeNumber(value.breakElapsedSeconds) &&
    isOptional(value, "currentSlideIndex", isNullableSlideNumber) &&
    typeof value.slideCount === "number" &&
    Number.isInteger(value.slideCount) &&
    value.slideCount >= 0 &&
    value.slideCount <= 10000 &&
    isOneOf(value.slideShowState, ["ready", "running", "paused", "black", "white"]);
}

function isPowerPointPresentations(value: unknown): boolean {
  return Array.isArray(value) &&
    value.length <= 32 &&
    value.every(isPowerPointPresentation);
}

function isPowerPointPresentation(value: unknown): boolean {
  return isRecord(value) &&
    isOperationId(value.runtimePresentationId) &&
    isBoundedString(value.name, 120, false) &&
    isOneOf(value.state, ["ready", "presenting"]) &&
    typeof value.slideCount === "number" &&
    Number.isInteger(value.slideCount) &&
    value.slideCount >= 0 &&
    value.slideCount <= 10000 &&
    isOptional(value, "currentSlideIndex", isNullableSlideNumber) &&
    isOptional(value, "currentShowPosition", isNullableSlideNumber) &&
    isOneOf(value.slideShowState, ["ready", "running", "paused", "black", "white"]);
}

function isNullableSlideNumber(value: unknown): boolean {
  return value === null ||
    (typeof value === "number" && Number.isInteger(value) && value >= 1 && value <= 10000);
}

function isNonNegativeNumber(value: unknown): boolean {
  return typeof value === "number" && Number.isFinite(value) && value >= 0;
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

function isRelayIceServers(value: unknown): value is RTCIceServer[] {
  return Array.isArray(value) && value.length > 0 && value.length <= 2 && value.every((server) => {
    if (!isRecord(server) || !hasOnlyFields(server, ["urls", "username", "credential"]) ||
        !isBoundedString(server.username, 512, false) || !isBoundedString(server.credential, 512, false)) {return false;}
    const urls = server.urls;
    return Array.isArray(urls) && urls.length > 0 && urls.length <= 4 && urls.every(isRelayTurnUrl);
  });
}

function isRelayTurnUrl(value: unknown): value is string {
  if (typeof value !== "string" || value.length > 512) {return false;}
  const match = /^(?:turn|turns):[A-Za-z0-9.-]+(?::([0-9]{1,5}))?(?:\?transport=(?:tcp|udp))?$/u.exec(value);
  if (!match) {return false;}
  return match[1] === undefined || Number(match[1]) >= 1 && Number(match[1]) <= 65_535;
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

function isCustomScreenId(value: unknown): value is string {
  return isBoundedString(value, 64, false) && /^[A-Za-z0-9._-]+$/.test(value);
}

function isInputSequence(value: unknown): boolean {
  return typeof value === "number" && Number.isSafeInteger(value) && value > 0;
}

function isOneOf<const Value>(value: unknown, allowed: readonly Value[]): value is Value {
  return allowed.includes(value as Value);
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
        laserPointerActive: capabilities.presentation.laserPointerActive,
        ...(capabilities.presentation.laserPointerColor === undefined
          ? {}
          : { laserPointerColor: capabilities.presentation.laserPointerColor }),
        ...(capabilities.presentation.laserPointerDefaultColor === undefined
          ? {}
          : { laserPointerDefaultColor: capabilities.presentation.laserPointerDefaultColor }),
        ...(capabilities.presentation.powerPoint === undefined
          ? {}
          : { powerPoint: capabilities.presentation.powerPoint })
      }
    : undefined;
export const hasRemoteLaunchCapability = (capabilities: ServerCapabilities | undefined) => capabilities?.remoteLaunch === true;
export const getCustomScreensCapability = (capabilities: ServerCapabilities | undefined) =>
  capabilities?.customScreens && Array.isArray(capabilities.customScreens.screens)
    ? capabilities.customScreens
    : undefined;
export const getScreenViewCapability = (capabilities: ServerCapabilities | undefined) => capabilities?.screenView ?? undefined;
export const getFileManagerCapability = (capabilities: ServerCapabilities | undefined) => capabilities?.fileManager ?? undefined;
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
    payload.type === "screen.pointer.move" ||
    payload.type === "screen.pointer.button" ||
    payload.type === "screen.pointer.wheel" ||
    payload.type === "keyboard.text" ||
    payload.type === "keyboard.special";
}

export function isMovementInput(payload: ClientMessage): payload is Extract<ClientInputMessage, { type: "pointer.move" | "pointer.wheel" | "pointer.zoom" | "screen.pointer.move" }> {
  return payload.type === "pointer.move" || payload.type === "pointer.wheel" || payload.type === "pointer.zoom" || payload.type === "screen.pointer.move";
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
