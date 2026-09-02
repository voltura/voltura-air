using System.Text.Json;
using System.Collections.Frozen;

namespace VolturaAir.Host;

internal sealed record PairHelloRequest(
    string ClientId,
    string DeviceName,
    string? PairTokenId,
    string? ClientNonce,
    string? ReconnectPublicKey,
    string? Platform,
    string? Browser,
    string? DisplayMode);

internal sealed record PairProofRequest(
    string ClientId,
    string Signature);

internal sealed record PairBootstrapProofRequest(
    string ClientId,
    string Proof);

internal static class ClientMessageValidator
{
    private static readonly FrozenSet<string> PairHelloProperties = new[]
    {
        "type", "clientId", "deviceName", "pairTokenId", "clientNonce", "reconnectPublicKey", "platform", "browser", "displayMode"
    }.ToFrozenSet(StringComparer.Ordinal);
    private static readonly FrozenSet<string> PairBootstrapProofProperties = new[]
    {
        "type", "clientId", "proof"
    }.ToFrozenSet(StringComparer.Ordinal);
    private static readonly FrozenSet<string> PairProofProperties = new[]
    {
        "type", "clientId", "signature"
    }.ToFrozenSet(StringComparer.Ordinal);
    private static readonly FrozenDictionary<string, FrozenSet<string>> AuthenticatedMessageProperties =
        new Dictionary<string, FrozenSet<string>>(StringComparer.Ordinal)
        {
            ["pair.disconnect"] = Fields("type"),
            ["device.rename"] = Fields("type", "deviceName"),
            ["health.ping"] = Fields("type"),
            ["status.get"] = Fields("type"),
            ["pointer.speed.set"] = Fields("type", "pointerSpeed"),
            ["appearance.mode-buttons.set"] = Fields("type", "showModeButtons"),
            ["appearance.control-depth.set"] = Fields("type", "controlDepth"),
            ["appearance.accent-color.set"] = Fields("type", "accentColor"),
            ["screen.view.sound-quality.set"] = Fields("type", "soundQuality"),
            ["custom.pointer.set"] = Fields("type", "enabled"),
            ["device.viewport.set"] = Fields("type", "width", "height", "orientation"),
            ["custom.screen.get"] = Fields("type", "operationId", "screenId"),
            ["custom.screen.invoke"] = Fields(
                "type",
                "operationId",
                "screenId",
                "screenRevision",
                "buttonId",
                "enabled"),
            ["screen.view.sources.get"] = Fields("type", "operationId"),
            ["screen.view.start"] = Fields("type", "operationId", "displayId", "clientSignature"),
            ["screen.view.answer"] = Fields("type", "operationId", "answerSdp", "clientSignature"),
            ["screen.view.quality"] = Fields("type", "operationId", "width", "height", "framesPerSecond", "framesDecoded", "framesDropped", "freezeCount", "packetsLost"),
            ["screen.view.source.set"] = Fields("type", "operationId", "displayId"),
            ["screen.view.stop"] = Fields("type", "operationId"),
            ["phone.webcam.start"] = Fields("type", "operationId", "captureWidth", "captureHeight", "captureFps", "useMicrophone", "clientSignature"),
            ["phone.webcam.answer"] = Fields("type", "operationId", "answerSdp", "clientSignature"),
            ["phone.webcam.stop"] = Fields("type", "operationId"),
            ["audio.get"] = Fields("type"),
            ["system.sleep"] = Fields("type"),
            ["system.power"] = Fields("type", "operationId", "action"),
            ["awake.set"] = Fields("type", "operationId", "enabled"),
            ["presentation.command"] = Fields(
                "type",
                "operationId",
                "target",
                "action",
                "enabled",
                "runtimePresentationId",
                "slideNumber"),
            ["presentation.powerpoint.refresh"] = Fields("type", "operationId"),
            ["presentation.powerpoint.launch"] = Fields("type", "operationId", "presentationId"),
            ["presentation.session"] = Fields(
                "type",
                "operationId",
                "action",
                "enabled",
                "runtimePresentationId"),
            ["presentation.report.save"] = Fields(
                "type",
                "operationId",
                "reportId",
                "target",
                "startedAt",
                "endedAt",
                "utcOffsetMinutes",
                "plannedDurationSeconds",
                "presentationDurationSeconds",
                "endedDuringBreak",
                "breaks",
                "slides"),
            ["remote.launch"] = Fields("type", "action"),
            ["app.launch"] = Fields("type", "operationId", "actionId"),
            ["url.open"] = Fields("type", "operationId", "url"),
            ["text.send"] = Fields("type", "operationId", "text", "sendEnter"),
            ["clipboard.get"] = Fields("type", "operationId"),
            ["diagnostics.get"] = Fields("type", "operationId"),
            ["apps.list"] = Fields("type", "operationId"),
            ["apps.activate"] = Fields("type", "operationId", "revision", "windowId"),
            ["apps.close"] = Fields("type", "operationId", "revision", "windowId"),
            ["apps.preview.answer"] = Fields("type", "operationId", "offerOperationId", "previewId", "answerSdp", "clientSignature"),
            ["apps.preview.stop"] = Fields("type", "operationId", "previewId"),
            ["file.session.open"] = Fields("type", "operationId"),
            ["file.page.get"] = Fields("type", "operationId", "sessionId", "panel", "revision", "continuation"),
            ["file.navigate"] = Fields("type", "operationId", "sessionId", "panel", "revision", "targetId"),
            ["file.refresh"] = Fields("type", "operationId", "sessionId", "panel"),
            ["file.sort"] = Fields("type", "operationId", "sessionId", "panel", "sortBy", "descending"),
            ["file.properties.get"] = Fields("type", "operationId", "sessionId", "panel", "revision", "entryId"),
            ["file.clipboard.set"] = Fields("type", "operationId", "sessionId", "panel", "revision", "effect", "selectionAll", "entryIds", "excludedEntryIds"),
            ["file.open"] = Fields("type", "operationId", "sessionId", "panel", "revision", "entryId"),
            ["file.jobs.get"] = Fields("type", "operationId"),
            ["file.job.create"] = Fields("type", "operationId", "sessionId", "panel", "revision", "operation", "destinationPanel", "destinationRevision", "newName", "selectionAll", "entryIds", "excludedEntryIds"),
            ["file.job.control"] = Fields("type", "operationId", "jobId", "action"),
            ["file.job.reorder"] = Fields("type", "operationId", "jobId", "direction"),
            ["file.job.conflict.resolve"] = Fields("type", "operationId", "jobId", "resolution", "applyToAll"),
            ["file.transfer.start"] = Fields("type", "operationId", "direction", "source", "sessionId", "panel", "revision", "entryId", "fileName", "declaredSize", "screenOperationId", "displayId", "clientSignature"),
            ["file.transfer.answer"] = Fields("type", "operationId", "transferId", "answerSdp", "clientSignature"),
            ["file.transfer.cancel"] = Fields("type", "operationId", "transferId", "requestId"),
            ["terminal.start"] = Fields("type", "operationId", "columns", "rows", "clientSignature"),
            ["terminal.attach"] = Fields("type", "operationId", "terminalId", "acknowledgedOffset", "columns", "rows", "clientSignature"),
            ["terminal.answer"] = Fields("type", "operationId", "offerOperationId", "terminalId", "answerSdp", "clientSignature"),
            ["terminal.stop"] = Fields("type", "operationId", "terminalId"),
            ["ai.assistant.open"] = Fields("type", "operationId", "clientSignature"),
            ["ai.assistant.ask"] = Fields("type", "operationId", "question", "clientSignature"),
            ["ai.assistant.reset"] = Fields("type", "operationId", "clientSignature"),
            ["ai.assistant.close"] = Fields("type", "operationId"),
            ["audio.mute.toggle"] = Fields("type", "inputContext"),
            ["audio.volume.set"] = Fields("type", "volume", "inputContext"),
            ["pointer.move"] = Fields("type", "seq", "dx", "dy", "inputContext"),
            ["pointer.button"] = Fields("type", "seq", "button", "action", "inputContext"),
            ["pointer.wheel"] = Fields("type", "seq", "dx", "dy", "inputContext"),
            ["pointer.zoom"] = Fields("type", "seq", "direction", "inputContext"),
            ["screen.pointer.move"] = Fields("type", "seq", "displayId", "x", "y", "inputContext"),
            ["screen.pointer.button"] = Fields("type", "seq", "displayId", "x", "y", "button", "action", "inputContext"),
            ["screen.pointer.wheel"] = Fields("type", "seq", "displayId", "x", "y", "dx", "dy", "inputContext"),
            ["keyboard.text"] = Fields("type", "seq", "text", "inputContext"),
            ["keyboard.special"] = Fields("type", "seq", "key", "modifiers", "inputContext")
        }.ToFrozenDictionary(StringComparer.Ordinal);
    private const int MaxClientIdLength = 128;
    private const int MaxDeviceNameLength = 120;
    private const int MaxCredentialLength = 512;
    private const int MaxMetadataLength = 80;
    private const int MaxKeyLength = 80;
    private const int MaxModifierLength = 40;
    private const int MaxModifierCount = 8;
    private const int MaxRemoteActionLength = 80;
    private const int MaxOperationIdLength = 64;
    private const double MaxPointerDelta = 5000;

    public static bool TryReadType(JsonElement root, out string? type)
    {
        type = null;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("type", out var typeProperty) ||
            typeProperty.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        type = typeProperty.GetString();
        return !string.IsNullOrEmpty(type);
    }

    public static bool TryValidatePairHello(JsonElement root, out PairHelloRequest request)
    {
        request = new PairHelloRequest(string.Empty, string.Empty, null, null, null, null, null, null);
        if (!TryReadType(root, out var type) ||
            type != "pair.hello" ||
            !HasOnlyUniqueProperties(root, PairHelloProperties))
        {
            return false;
        }

        if (!TryGetRequiredString(root, "clientId", MaxClientIdLength, allowEmpty: false, out var clientId) ||
            !TryGetRequiredString(root, "deviceName", MaxDeviceNameLength, allowEmpty: false, out var deviceName) ||
            string.IsNullOrWhiteSpace(deviceName) ||
            !TryGetOptionalString(root, "pairTokenId", MaxCredentialLength, out var pairTokenId) ||
            !TryGetOptionalString(root, "clientNonce", MaxCredentialLength, out var clientNonce) ||
            !TryGetOptionalString(root, "reconnectPublicKey", MaxCredentialLength, out var reconnectPublicKey) ||
            !TryGetOptionalString(root, "platform", MaxMetadataLength, out var platform) ||
            !TryGetOptionalString(root, "browser", MaxMetadataLength, out var browser) ||
            !TryGetOptionalString(root, "displayMode", MaxMetadataLength, out var displayMode))
        {
            return false;
        }

        request = new PairHelloRequest(clientId, deviceName, pairTokenId, clientNonce, reconnectPublicKey, platform, browser, displayMode);
        return true;
    }

    public static bool TryValidatePairBootstrapProof(JsonElement root, out PairBootstrapProofRequest request)
    {
        request = new PairBootstrapProofRequest(string.Empty, string.Empty);
        if (!TryReadType(root, out var type) ||
            type != "pair.bootstrap.proof" ||
            !HasOnlyUniqueProperties(root, PairBootstrapProofProperties) ||
            !TryGetRequiredString(root, "clientId", MaxClientIdLength, allowEmpty: false, out var clientId) ||
            !TryGetRequiredString(root, "proof", MaxCredentialLength, allowEmpty: false, out var proof))
        {
            return false;
        }

        request = new PairBootstrapProofRequest(clientId, proof);
        return true;
    }

    public static bool TryValidatePairProof(JsonElement root, out PairProofRequest request)
    {
        request = new PairProofRequest(string.Empty, string.Empty);
        if (!TryReadType(root, out var type) ||
            type != "pair.proof" ||
            !HasOnlyUniqueProperties(root, PairProofProperties))
        {
            return false;
        }

        if (!TryGetRequiredString(root, "clientId", MaxClientIdLength, allowEmpty: false, out var clientId) ||
            !TryGetRequiredString(root, "signature", MaxCredentialLength, allowEmpty: false, out var signature))
        {
            return false;
        }

        request = new PairProofRequest(clientId, signature);
        return true;
    }

    public static bool IsValidAuthenticatedMessage(JsonElement root, string type)
    {
        if (!AuthenticatedMessageProperties.TryGetValue(type, out var allowedProperties) ||
            !HasOnlyUniqueProperties(root, allowedProperties))
        {
            return false;
        }

        return type switch
        {
            "pair.disconnect" => true,
            "device.rename" => TryGetRequiredString(root, "deviceName", MaxDeviceNameLength, allowEmpty: false, out var deviceName) &&
                !string.IsNullOrWhiteSpace(deviceName),
            "health.ping" => true,
            "status.get" => true,
            "pointer.speed.set" => TryGetNumber(root, "pointerSpeed", DevicePointerProfile.MinPointerSpeed, DevicePointerProfile.MaxPointerSpeed, out _),
            "appearance.mode-buttons.set" => root.TryGetProperty("showModeButtons", out var showModeButtons) && showModeButtons.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "appearance.control-depth.set" => root.TryGetProperty("controlDepth", out var controlDepth) && controlDepth.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "appearance.accent-color.set" =>
                root.TryGetProperty("accentColor", out var accentColor) &&
                (accentColor.ValueKind == JsonValueKind.Null ||
                 accentColor.ValueKind == JsonValueKind.String && AccentColor.IsCanonical(accentColor.GetString())),
            "screen.view.sound-quality.set" =>
                root.TryGetProperty("soundQuality", out var screenSoundQuality) &&
                (screenSoundQuality.ValueKind == JsonValueKind.Null ||
                 screenSoundQuality.ValueKind == JsonValueKind.String &&
                 ScreenViewSoundQualityProfile.TryParseProtocolId(screenSoundQuality.GetString(), out _)),
            "custom.pointer.set" => root.TryGetProperty("enabled", out var customPointerEnabled) && customPointerEnabled.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "device.viewport.set" =>
                TryGetNumber(root, "width", CustomScreenLimits.MinViewportWidth, CustomScreenLimits.MaxViewportWidth, out _) &&
                TryGetNumber(root, "height", CustomScreenLimits.MinViewportHeight, CustomScreenLimits.MaxViewportHeight, out _) &&
                TryGetRequiredString(root, "orientation", 16, allowEmpty: false, out var orientation) &&
                orientation is "portrait" or "landscape",
            "custom.screen.get" =>
                TryGetRequiredString(root, "operationId", MaxOperationIdLength, allowEmpty: false, out var screenGetOperationId) &&
                IsValidOperationId(screenGetOperationId) &&
                TryGetRequiredString(root, "screenId", CustomScreenLimits.MaxIdLength, allowEmpty: false, out var getScreenId) &&
                IsValidCustomScreenId(getScreenId),
            "custom.screen.invoke" =>
                TryGetRequiredString(root, "operationId", MaxOperationIdLength, allowEmpty: false, out var screenInvokeOperationId) &&
                IsValidOperationId(screenInvokeOperationId) &&
                TryGetRequiredString(root, "screenId", CustomScreenLimits.MaxIdLength, allowEmpty: false, out var invokeScreenId) &&
                IsValidCustomScreenId(invokeScreenId) &&
                TryGetRequiredString(root, "screenRevision", CustomScreenLimits.MaxIdLength, allowEmpty: false, out var screenRevision) &&
                IsValidCustomScreenId(screenRevision) &&
                TryGetRequiredString(root, "buttonId", CustomScreenLimits.MaxIdLength, allowEmpty: false, out var buttonId) &&
                IsValidCustomScreenId(buttonId) &&
                (!root.TryGetProperty("enabled", out var customScreenEnabled) ||
                    customScreenEnabled.ValueKind is JsonValueKind.True or JsonValueKind.False),
            "screen.view.sources.get" =>
                TryGetRequiredString(root, "operationId", MaxOperationIdLength, allowEmpty: false, out var screenSourcesOperationId) &&
                IsValidOperationId(screenSourcesOperationId),
            "screen.view.start" =>
                TryGetRequiredString(root, "operationId", MaxOperationIdLength, allowEmpty: false, out var screenStartOperationId) &&
                IsValidOperationId(screenStartOperationId) &&
                TryGetRequiredString(root, "displayId", MaxMetadataLength, allowEmpty: false, out _) &&
                TryGetRequiredString(root, "clientSignature", MaxCredentialLength, allowEmpty: false, out _),
            "screen.view.answer" =>
                TryGetRequiredString(root, "operationId", MaxOperationIdLength, allowEmpty: false, out var screenAnswerOperationId) &&
                IsValidOperationId(screenAnswerOperationId) &&
                TryGetRequiredString(root, "answerSdp", ScreenViewProtocol.MaxSdpLength, allowEmpty: false, out _) &&
                TryGetRequiredString(root, "clientSignature", MaxCredentialLength, allowEmpty: false, out _),
            "screen.view.quality" =>
                TryGetRequiredString(root, "operationId", MaxOperationIdLength, allowEmpty: false, out var screenQualityOperationId) &&
                IsValidOperationId(screenQualityOperationId) &&
                TryGetBoundedInt(root, "width", 0, 16384, out _) &&
                TryGetBoundedInt(root, "height", 0, 16384, out _) &&
                TryGetNumber(root, "framesPerSecond", 0, 240, out _) &&
                TryGetBoundedInt(root, "framesDecoded", 0, 1_000_000, out _) &&
                TryGetBoundedInt(root, "framesDropped", 0, 1_000_000, out _) &&
                TryGetBoundedInt(root, "freezeCount", 0, 1_000_000, out _) &&
                TryGetBoundedInt(root, "packetsLost", 0, 1_000_000, out _),
            "screen.view.source.set" =>
                TryGetRequiredString(root, "operationId", MaxOperationIdLength, allowEmpty: false, out var screenSourceOperationId) &&
                IsValidOperationId(screenSourceOperationId) &&
                TryGetRequiredString(root, "displayId", MaxMetadataLength, allowEmpty: false, out _),
            "screen.view.stop" =>
                TryGetRequiredString(root, "operationId", MaxOperationIdLength, allowEmpty: false, out var screenStopOperationId) &&
                IsValidOperationId(screenStopOperationId),
            "phone.webcam.start" =>
                TryGetRequiredString(root, "operationId", MaxOperationIdLength, allowEmpty: false, out var webcamStartOperationId) &&
                IsValidOperationId(webcamStartOperationId) &&
                TryGetBoundedInt(root, "captureWidth", 1, 4096, out _) &&
                TryGetBoundedInt(root, "captureHeight", 1, 4096, out _) &&
                TryGetBoundedInt(root, "captureFps", 1, 60, out _) &&
                root.TryGetProperty("useMicrophone", out var useMicrophone) &&
                useMicrophone.ValueKind is JsonValueKind.True or JsonValueKind.False &&
                TryGetRequiredString(root, "clientSignature", MaxCredentialLength, allowEmpty: false, out _),
            "phone.webcam.answer" =>
                TryGetRequiredString(root, "operationId", MaxOperationIdLength, allowEmpty: false, out var webcamAnswerOperationId) &&
                IsValidOperationId(webcamAnswerOperationId) &&
                TryGetRequiredString(root, "answerSdp", ScreenViewProtocol.MaxSdpLength, allowEmpty: false, out _) &&
                TryGetRequiredString(root, "clientSignature", MaxCredentialLength, allowEmpty: false, out _),
            "phone.webcam.stop" =>
                TryGetRequiredString(root, "operationId", MaxOperationIdLength, allowEmpty: false, out var webcamStopOperationId) &&
                IsValidOperationId(webcamStopOperationId),
            "audio.get" => true,
            "system.sleep" => true,
            "system.power" => TryGetRequiredString(root, "operationId", MaxOperationIdLength, allowEmpty: false, out var powerOperationId) &&
                IsValidOperationId(powerOperationId) &&
                TryGetRequiredString(root, "action", MaxRemoteActionLength, allowEmpty: false, out _),
            "awake.set" => TryGetRequiredString(root, "operationId", MaxOperationIdLength, allowEmpty: false, out var awakeOperationId) &&
                IsValidOperationId(awakeOperationId) &&
                root.TryGetProperty("enabled", out var enabled) &&
                enabled.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "presentation.command" => TryGetRequiredString(root, "operationId", MaxOperationIdLength, allowEmpty: false, out var presentationOperationId) &&
                IsValidOperationId(presentationOperationId) &&
                TryGetRequiredString(root, "target", MaxMetadataLength, allowEmpty: false, out var presentationTarget) &&
                PresentationCommands.IsTarget(presentationTarget) &&
                TryGetRequiredString(root, "action", MaxRemoteActionLength, allowEmpty: false, out var presentationAction) &&
                PresentationCommands.IsAction(presentationAction) &&
                IsValidPresentationCommandFields(
                    root,
                    presentationTarget,
                    presentationAction),
            "presentation.powerpoint.refresh" =>
                TryGetRequiredString(
                    root,
                    "operationId",
                    MaxOperationIdLength,
                    allowEmpty: false,
                    out var refreshOperationId) &&
                IsValidOperationId(refreshOperationId),
            "presentation.powerpoint.launch" =>
                TryGetRequiredString(root, "operationId", MaxOperationIdLength, allowEmpty: false, out var launchOperationId) &&
                IsValidOperationId(launchOperationId) &&
                TryGetRequiredString(root, "presentationId", MaxOperationIdLength, allowEmpty: false, out var presentationId) &&
                IsValidOperationId(presentationId),
            "presentation.session" => IsValidPresentationSessionMessage(root),
            "presentation.report.save" => IsValidPresentationReportEnvelope(root),
            "remote.launch" => TryGetRequiredString(root, "action", MaxRemoteActionLength, allowEmpty: false, out var action) &&
                RemoteLaunchActions.IsSupported(action),
            "app.launch" => TryGetRequiredString(root, "operationId", MaxOperationIdLength, allowEmpty: false, out var appLaunchOperationId) &&
                IsValidOperationId(appLaunchOperationId) &&
                TryGetRequiredString(root, "actionId", AppLaunchSettings.MaxIdLength, allowEmpty: false, out var actionId) &&
                IsValidAppLaunchActionId(actionId),
            "url.open" => TryGetRequiredString(root, "operationId", MaxOperationIdLength, allowEmpty: false, out var urlOperationId) &&
                IsValidOperationId(urlOperationId) &&
                TryGetRequiredString(root, "url", UrlOpenLimits.MaxUrlLength, allowEmpty: false, out _),
            "text.send" => TryGetRequiredString(root, "operationId", MaxOperationIdLength, allowEmpty: false, out var operationId) &&
                IsValidOperationId(operationId) &&
                TryGetRequiredString(root, "text", TextTransferLimits.MaxTextLength, allowEmpty: false, out _) &&
                root.TryGetProperty("sendEnter", out var sendEnter) && sendEnter.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "clipboard.get" => TryGetRequiredString(root, "operationId", MaxOperationIdLength, allowEmpty: false, out var clipboardOperationId) &&
                IsValidOperationId(clipboardOperationId),
            "file.session.open" or "file.jobs.get" or "diagnostics.get" or "apps.list" => IsValidFileOperationId(root),
            "apps.activate" or "apps.close" => IsValidFileOperationId(root) &&
                TryGetRequiredString(root, "revision", Features.Apps.AppsProtocol.OpaqueIdLength, allowEmpty: false, out var appsRevision) &&
                Features.Apps.AppsProtocol.IsOpaqueId(appsRevision) &&
                TryGetRequiredString(root, "windowId", Features.Apps.AppsProtocol.OpaqueIdLength, allowEmpty: false, out var appsWindowId) &&
                Features.Apps.AppsProtocol.IsOpaqueId(appsWindowId),
            "apps.preview.answer" => IsValidFileOperationId(root) &&
                TryGetRequiredString(root, "offerOperationId", MaxOperationIdLength, allowEmpty: false, out var appsOfferOperationId) &&
                IsValidOperationId(appsOfferOperationId) &&
                TryGetRequiredString(root, "previewId", Features.Apps.AppsProtocol.OpaqueIdLength, allowEmpty: false, out var appsPreviewId) &&
                Features.Apps.AppsProtocol.IsOpaqueId(appsPreviewId) &&
                TryGetRequiredString(root, "answerSdp", ScreenViewProtocol.MaxSdpLength, allowEmpty: false, out _) &&
                TryGetRequiredString(root, "clientSignature", MaxCredentialLength, allowEmpty: false, out _),
            "apps.preview.stop" => IsValidFileOperationId(root) &&
                TryGetRequiredString(root, "previewId", Features.Apps.AppsProtocol.OpaqueIdLength, allowEmpty: false, out var appsStopPreviewId) &&
                Features.Apps.AppsProtocol.IsOpaqueId(appsStopPreviewId),
            "file.page.get" => IsValidFilePanelRequest(root, requireRevision: true) &&
                TryGetRequiredString(root, "continuation", MaxCredentialLength, allowEmpty: false, out _),
            "file.navigate" => IsValidFilePanelRequest(root, requireRevision: true) &&
                TryGetRequiredString(root, "targetId", MaxCredentialLength, allowEmpty: false, out _),
            "file.refresh" => IsValidFilePanelRequest(root, requireRevision: false),
            "file.sort" => IsValidFilePanelRequest(root, requireRevision: false) &&
                TryGetRequiredString(root, "sortBy", 16, allowEmpty: false, out var sortBy) && sortBy is "name" or "size" or "type" or "modified" &&
                root.TryGetProperty("descending", out var descending) && descending.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "file.properties.get" or "file.open" => IsValidFilePanelRequest(root, requireRevision: true) &&
                TryGetRequiredString(root, "entryId", MaxCredentialLength, allowEmpty: false, out _),
            "file.clipboard.set" => IsValidFilePanelRequest(root, requireRevision: true) &&
                TryGetRequiredString(root, "effect", 8, allowEmpty: false, out var effect) && effect is "copy" or "move" &&
                IsValidFileSelection(root),
            "file.job.create" => IsValidFilePanelRequest(root, requireRevision: true) &&
                TryGetRequiredString(root, "operation", 16, allowEmpty: false, out var fileOperation) &&
                fileOperation is "copy" or "move" or "paste" or "rename" or "delete" &&
                TryGetOptionalString(root, "destinationPanel", 8, out var destinationPanel) && destinationPanel is null or "left" or "right" &&
                TryGetOptionalString(root, "destinationRevision", MaxCredentialLength, out var destinationRevision) &&
                (fileOperation is "copy" or "move"
                    ? destinationPanel is not null && destinationRevision is not null
                    : destinationPanel is null && destinationRevision is null) &&
                TryGetOptionalString(root, "newName", FileManagerProtocol.MaxNameLength, out _) &&
                IsValidFileSelection(root),
            "file.job.control" => IsValidFileOperationId(root) &&
                TryGetRequiredString(root, "jobId", MaxCredentialLength, allowEmpty: false, out _) &&
                TryGetRequiredString(root, "action", 16, allowEmpty: false, out var jobAction) && jobAction is "pause" or "resume" or "cancel" or "dismiss",
            "file.job.reorder" => IsValidFileOperationId(root) &&
                TryGetRequiredString(root, "jobId", MaxCredentialLength, allowEmpty: false, out _) &&
                TryGetRequiredString(root, "direction", 8, allowEmpty: false, out var direction) && direction is "up" or "down",
            "file.job.conflict.resolve" => IsValidFileOperationId(root) &&
                TryGetRequiredString(root, "jobId", MaxCredentialLength, allowEmpty: false, out _) &&
                TryGetRequiredString(root, "resolution", 16, allowEmpty: false, out var resolution) && resolution is "replace" or "skip" or "keep-both" or "cancel" &&
                root.TryGetProperty("applyToAll", out var applyToAll) && applyToAll.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "file.transfer.start" => IsValidFileTransferStart(root),
            "file.transfer.answer" => IsValidFileOperationId(root) &&
                TryGetRequiredString(root, "transferId", MaxCredentialLength, allowEmpty: false, out var transferId) && IsValidOperationId(transferId) &&
                TryGetRequiredString(root, "answerSdp", ScreenViewProtocol.MaxSdpLength, allowEmpty: false, out _) &&
                TryGetRequiredString(root, "clientSignature", MaxCredentialLength, allowEmpty: false, out _),
            "file.transfer.cancel" => IsValidFileOperationId(root) && IsValidFileTransferCancel(root),
            "terminal.start" => IsValidTerminalStart(root),
            "terminal.attach" => IsValidTerminalAttach(root),
            "terminal.answer" => IsValidFileOperationId(root) &&
                TryGetRequiredString(root, "offerOperationId", MaxCredentialLength, allowEmpty: false, out var terminalOfferOperationId) && IsValidOperationId(terminalOfferOperationId) &&
                IsValidTerminalId(root) &&
                TryGetRequiredString(root, "answerSdp", ScreenViewProtocol.MaxSdpLength, allowEmpty: false, out _) &&
                TryGetRequiredString(root, "clientSignature", MaxCredentialLength, allowEmpty: false, out _),
            "terminal.stop" => IsValidFileOperationId(root) && IsValidTerminalId(root),
            "ai.assistant.open" or "ai.assistant.reset" => IsValidFileOperationId(root) &&
                TryGetRequiredString(root, "clientSignature", MaxCredentialLength, allowEmpty: false, out _),
            "ai.assistant.ask" => IsValidFileOperationId(root) &&
                TryGetRequiredString(root, "question", Features.AiAssistant.AiAssistantProtocol.MaximumQuestionCharacters, allowEmpty: false, out var assistantQuestion) &&
                !string.IsNullOrWhiteSpace(assistantQuestion) &&
                TryGetRequiredString(root, "clientSignature", MaxCredentialLength, allowEmpty: false, out _),
            "ai.assistant.close" => IsValidFileOperationId(root),
            "audio.mute.toggle" => TryGetOptionalInputContext(root, out var muteContext) &&
                IsInputContextAllowed(type, muteContext),
            "audio.volume.set" => TryGetNumber(root, "volume", 0, 100, out _) &&
                TryGetOptionalInputContext(root, out var volumeContext) &&
                IsInputContextAllowed(type, volumeContext),
            "pointer.move" or "pointer.button" or "pointer.wheel" or "pointer.zoom" or
                "screen.pointer.move" or "screen.pointer.button" or "screen.pointer.wheel" or
                "keyboard.text" or "keyboard.special" =>
                TryDecodeInputMessageFields(root, type, out _),
            _ => false
        };
    }

    public static bool TryDecodeInputMessage(JsonElement root, string type, out ValidatedInputCommand command)
    {
        if (!AuthenticatedMessageProperties.TryGetValue(type, out var allowedProperties) ||
            !HasOnlyUniqueProperties(root, allowedProperties))
        {
            command = default;
            return false;
        }

        return TryDecodeInputMessageFields(root, type, out command);
    }

    private static bool TryDecodeInputMessageFields(JsonElement root, string type, out ValidatedInputCommand command)
    {
        command = default;
        if (!TryGetOptionalSequence(root, out var sequence) ||
            !TryGetOptionalInputContext(root, out var inputContext) ||
            !IsInputContextAllowed(type, inputContext))
        {
            return false;
        }

        switch (type)
        {
            case "pointer.move":
                if (!TryGetPointerDelta(root, "dx", out var moveDx) ||
                    !TryGetPointerDelta(root, "dy", out var moveDy))
                {
                    return false;
                }

                command = new ValidatedInputCommand(InputCommandKind.PointerMove, sequence, Dx: moveDx, Dy: moveDy, Context: inputContext);
                return true;
            case "pointer.button":
                if (!TryGetRequiredString(root, "button", MaxMetadataLength, allowEmpty: false, out var button) ||
                    button is not ("left" or "right") ||
                    !TryGetRequiredString(root, "action", MaxMetadataLength, allowEmpty: false, out var buttonAction) ||
                    buttonAction is not ("down" or "up" or "click"))
                {
                    return false;
                }

                command = new ValidatedInputCommand(InputCommandKind.PointerButton, sequence, Button: button, Action: buttonAction, Context: inputContext);
                return true;
            case "pointer.wheel":
                if (!TryGetPointerDelta(root, "dx", out var wheelDx) ||
                    !TryGetPointerDelta(root, "dy", out var wheelDy))
                {
                    return false;
                }

                command = new ValidatedInputCommand(InputCommandKind.PointerWheel, sequence, Dx: wheelDx, Dy: wheelDy, Context: inputContext);
                return true;
            case "pointer.zoom":
                if (!TryGetRequiredString(root, "direction", MaxMetadataLength, allowEmpty: false, out var direction) ||
                    direction is not ("in" or "out"))
                {
                    return false;
                }

                command = new ValidatedInputCommand(InputCommandKind.PointerZoom, sequence, Action: direction, Context: inputContext);
                return true;
            case "screen.pointer.move":
            case "screen.pointer.button":
            case "screen.pointer.wheel":
                if (!TryGetRequiredString(root, "displayId", MaxMetadataLength, allowEmpty: false, out var displayId) ||
                    !TryGetNumber(root, "x", 0, 1, out var x) ||
                    !TryGetNumber(root, "y", 0, 1, out var y))
                {
                    return false;
                }

                if (type == "screen.pointer.move")
                {
                    command = new ValidatedInputCommand(InputCommandKind.ScreenPointerMove, sequence, DisplayId: displayId, X: x, Y: y, Context: inputContext);
                    return true;
                }

                if (type == "screen.pointer.button")
                {
                    if (!TryGetRequiredString(root, "button", 8, allowEmpty: false, out var directButton) ||
                        directButton is not ("left" or "right") ||
                        !TryGetRequiredString(root, "action", 8, allowEmpty: false, out var directAction) ||
                        directAction is not ("down" or "up"))
                    {
                        return false;
                    }

                    command = new ValidatedInputCommand(
                        InputCommandKind.ScreenPointerButton,
                        sequence,
                        Button: directButton,
                        Action: directAction,
                        DisplayId: displayId,
                        X: x,
                        Y: y,
                        Context: inputContext);
                    return true;
                }

                if (!TryGetPointerDelta(root, "dx", out var directWheelDx) ||
                    !TryGetPointerDelta(root, "dy", out var directWheelDy))
                {
                    return false;
                }

                command = new ValidatedInputCommand(
                    InputCommandKind.ScreenPointerWheel,
                    sequence,
                    Dx: directWheelDx,
                    Dy: directWheelDy,
                    DisplayId: displayId,
                    X: x,
                    Y: y,
                    Context: inputContext);
                return true;
            case "keyboard.text":
                if (!TryGetRequiredString(root, "text", TextTransferLimits.MaxTextLength, allowEmpty: false, out var text))
                {
                    return false;
                }

                command = new ValidatedInputCommand(InputCommandKind.KeyboardText, sequence, Text: text, Context: inputContext);
                return true;
            case "keyboard.special":
                if (!TryGetRequiredString(root, "key", MaxKeyLength, allowEmpty: false, out var key) ||
                    !TryGetOptionalStringArray(root, "modifiers", MaxModifierCount, MaxModifierLength, out var modifiers))
                {
                    return false;
                }

                command = new ValidatedInputCommand(InputCommandKind.KeyboardSpecial, sequence, Key: key, ModifierValues: modifiers, Context: inputContext);
                return true;
            default:
                return false;
        }
    }

    internal static bool TryGetOptionalInputContext(JsonElement root, out InputCommandContext? context)
    {
        context = null;
        if (!root.TryGetProperty("inputContext", out var property))
        {
            return true;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        context = property.GetString() switch
        {
            "trackpad" => InputCommandContext.Trackpad,
            "keyboard" => InputCommandContext.Keyboard,
            "dictation" => InputCommandContext.Dictation,
            "media-controls" => InputCommandContext.MediaControls,
            "presentation" => InputCommandContext.Presentation,
            "custom-screens" => InputCommandContext.CustomScreens,
            "screen-view" => InputCommandContext.ScreenView,
            "gyro-mouse" => InputCommandContext.GyroMouse,
            _ => null
        };
        return context is not null;
    }

    private static bool IsInputContextAllowed(string type, InputCommandContext? context)
    {
        if (context is null)
        {
            return true;
        }

        return type switch
        {
            "audio.mute.toggle" or "audio.volume.set" =>
                context is InputCommandContext.MediaControls or InputCommandContext.CustomScreens,
            "pointer.move" or "pointer.button" or "pointer.wheel" or "pointer.zoom" =>
                context is InputCommandContext.Trackpad or InputCommandContext.Keyboard or
                    InputCommandContext.Presentation or InputCommandContext.CustomScreens or
                    InputCommandContext.ScreenView or InputCommandContext.GyroMouse,
            "screen.pointer.move" or "screen.pointer.button" or "screen.pointer.wheel" =>
                context is InputCommandContext.ScreenView,
            "keyboard.text" =>
                context is InputCommandContext.Keyboard or InputCommandContext.Dictation or
                    InputCommandContext.ScreenView,
            "keyboard.special" =>
                context is InputCommandContext.Keyboard or InputCommandContext.MediaControls or
                    InputCommandContext.Presentation or InputCommandContext.CustomScreens or
                    InputCommandContext.ScreenView,
            _ => false
        };
    }

    private static bool IsValidAppLaunchActionId(string actionId)
    {
        return actionId.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_');
    }

    private static bool IsValidFileTransferStart(JsonElement root)
    {
        if (!IsValidFileOperationId(root) ||
            !TryGetRequiredString(root, "direction", 16, allowEmpty: false, out var direction) ||
            !TryGetRequiredString(root, "clientSignature", MaxCredentialLength, allowEmpty: false, out _)) return false;
        if (root.TryGetProperty("source", out var source))
        {
            return source.ValueKind == JsonValueKind.String && source.GetString() == "screen-capture" &&
                direction == "download" &&
                TryGetRequiredString(root, "screenOperationId", MaxOperationIdLength, allowEmpty: false, out var screenOperationId) &&
                IsValidOperationId(screenOperationId) &&
                TryGetRequiredString(root, "displayId", MaxCredentialLength, allowEmpty: false, out _) &&
                !root.TryGetProperty("sessionId", out _) &&
                !root.TryGetProperty("panel", out _) &&
                !root.TryGetProperty("revision", out _) &&
                !root.TryGetProperty("entryId", out _) &&
                !root.TryGetProperty("fileName", out _) &&
                !root.TryGetProperty("declaredSize", out _);
        }
        if (!IsValidFilePanelRequest(root, requireRevision: true) ||
            root.TryGetProperty("screenOperationId", out _) ||
            root.TryGetProperty("displayId", out _)) return false;
        if (direction == "download")
        {
            return TryGetRequiredString(root, "entryId", MaxCredentialLength, allowEmpty: false, out _) &&
                !root.TryGetProperty("fileName", out _) && !root.TryGetProperty("declaredSize", out _);
        }
        if (direction != "upload" || root.TryGetProperty("entryId", out _) ||
            !TryGetRequiredString(root, "fileName", FileManagerProtocol.MaxNameLength, allowEmpty: false, out _)) return false;
        return root.TryGetProperty("declaredSize", out var size) && size.ValueKind == JsonValueKind.Number &&
            size.TryGetInt64(out var declaredSize) && declaredSize is >= 0 and <= FileTransferProtocol.MaximumSafeFileSize;
    }

    private static bool IsValidFileTransferCancel(JsonElement root)
    {
        if (!TryGetOptionalString(root, "transferId", MaxCredentialLength, out var transferId) ||
            !TryGetOptionalString(root, "requestId", MaxOperationIdLength, out var requestId) ||
            (transferId is null) == (requestId is null)) return false;
        return transferId is not null ? IsValidOperationId(transferId) : IsValidOperationId(requestId!);
    }

    private static bool IsValidTerminalStart(JsonElement root) =>
        IsValidFileOperationId(root) &&
        TryGetNumber(root, "columns", TerminalProtocol.MinimumColumns, TerminalProtocol.MaximumColumns, out _) &&
        TryGetNumber(root, "rows", TerminalProtocol.MinimumRows, TerminalProtocol.MaximumRows, out _) &&
        TryGetRequiredString(root, "clientSignature", MaxCredentialLength, allowEmpty: false, out _);

    private static bool IsValidTerminalAttach(JsonElement root) =>
        IsValidTerminalStart(root) && IsValidTerminalId(root) &&
        root.TryGetProperty("acknowledgedOffset", out var offset) && offset.ValueKind == JsonValueKind.Number &&
        offset.TryGetInt64(out var parsedOffset) && parsedOffset >= 0;

    private static bool IsValidTerminalId(JsonElement root) =>
        TryGetRequiredString(root, "terminalId", 32, allowEmpty: false, out var terminalId) &&
        terminalId.Length == 32 && terminalId.All(char.IsAsciiHexDigit);

    private static bool IsValidOperationId(string operationId)
    {
        return operationId.All(character => char.IsAsciiLetterOrDigit(character) || character is '-');
    }

    private static bool IsValidCustomScreenId(string value)
    {
        return value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_');
    }

    private static bool IsValidPresentationReportEnvelope(JsonElement root) =>
        TryGetRequiredString(root, "operationId", MaxOperationIdLength, allowEmpty: false, out var operationId) &&
        IsValidOperationId(operationId) &&
        TryGetRequiredString(root, "reportId", MaxOperationIdLength, allowEmpty: false, out var reportId) &&
        IsValidOperationId(reportId);

    private static bool IsValidPresentationCommandFields(
        JsonElement root,
        string target,
        string action)
    {
        var hasEnabled = root.TryGetProperty("enabled", out var enabled);
        if (action is "pointer" or "pause")
        {
            if (!hasEnabled || enabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                return false;
            }
        }
        else if (hasEnabled)
        {
            return false;
        }

        var hasSlideNumber = root.TryGetProperty("slideNumber", out var slideNumber);
        if (action == "goto")
        {
            if (!hasSlideNumber ||
                slideNumber.ValueKind != JsonValueKind.Number ||
                !slideNumber.TryGetInt32(out var parsedSlideNumber) ||
                parsedSlideNumber is < 1 or > PresentationReportProtocol.MaxSlideCount)
            {
                return false;
            }
        }
        else if (hasSlideNumber)
        {
            return false;
        }

        if (!TryGetOptionalString(
                root,
                "runtimePresentationId",
                MaxOperationIdLength,
                out var runtimePresentationId) ||
            runtimePresentationId is not null && !IsValidOperationId(runtimePresentationId))
        {
            return false;
        }

        if (target != "powerpoint")
        {
            return runtimePresentationId is null &&
                action is "next" or "previous" or "end" or "black" or "pointer";
        }

        return true;
    }

    private static bool IsValidPresentationSessionMessage(JsonElement root)
    {
        if (!TryGetRequiredString(
                root,
                "operationId",
                MaxOperationIdLength,
                allowEmpty: false,
                out var operationId) ||
            !IsValidOperationId(operationId) ||
            !TryGetRequiredString(
                root,
                "action",
                MaxRemoteActionLength,
                allowEmpty: false,
                out var action) ||
            action is not ("start" or "break" or "save" or "discard"))
        {
            return false;
        }

        var hasEnabled = root.TryGetProperty("enabled", out var enabled);
        if (action == "break")
        {
            if (!hasEnabled || enabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                return false;
            }
        }
        else if (hasEnabled)
        {
            return false;
        }

        var hasRuntimeId = root.TryGetProperty("runtimePresentationId", out var runtimeId);
        return action == "start"
            ? !hasRuntimeId ||
                runtimeId.ValueKind == JsonValueKind.String &&
                runtimeId.GetString() is { } value &&
                value.Length <= MaxOperationIdLength &&
                IsValidOperationId(value)
            : !hasRuntimeId;
    }

    private static bool IsValidFileOperationId(JsonElement root) =>
        TryGetRequiredString(root, "operationId", MaxOperationIdLength, allowEmpty: false, out var operationId) &&
        IsValidOperationId(operationId);

    private static bool IsValidFilePanelRequest(JsonElement root, bool requireRevision)
    {
        if (!IsValidFileOperationId(root) ||
            !TryGetRequiredString(root, "sessionId", MaxCredentialLength, allowEmpty: false, out _) ||
            !TryGetRequiredString(root, "panel", 8, allowEmpty: false, out var panel) || panel is not ("left" or "right"))
        {
            return false;
        }
        return !requireRevision || TryGetRequiredString(root, "revision", MaxCredentialLength, allowEmpty: false, out _);
    }

    private static bool IsValidFileSelection(JsonElement root) =>
        root.TryGetProperty("selectionAll", out var all) && all.ValueKind is JsonValueKind.True or JsonValueKind.False &&
        TryGetRequiredStringArray(root, "entryIds", FileManagerProtocol.MaxSelectionItems, MaxCredentialLength, out var entryIds) &&
        TryGetRequiredStringArray(root, "excludedEntryIds", FileManagerProtocol.MaxSelectionItems, MaxCredentialLength, out var excluded) &&
        (all.GetBoolean() || entryIds.Length > 0) &&
        (!all.GetBoolean() || entryIds.Length == 0) &&
        (!all.GetBoolean() || excluded.Length <= FileManagerProtocol.MaxSelectionItems);

    private static FrozenSet<string> Fields(params string[] names) => names.ToFrozenSet(StringComparer.Ordinal);

    private static bool HasOnlyUniqueProperties(JsonElement root, FrozenSet<string> allowedProperties)
    {
        var seenProperties = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
        {
            if (!allowedProperties.Contains(property.Name) || !seenProperties.Add(property.Name))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryGetRequiredString(JsonElement root, string propertyName, int maxLength, bool allowEmpty, out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? string.Empty;
        return value.Length <= maxLength && (allowEmpty || value.Length > 0);
    }

    private static bool TryGetOptionalString(JsonElement root, string propertyName, int maxLength, out string? value)
    {
        value = null;
        if (!root.TryGetProperty(propertyName, out var property))
        {
            return true;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString();
        return value is not null && !string.IsNullOrWhiteSpace(value) && value.Length <= maxLength;
    }

    private static bool TryGetNumber(JsonElement root, string propertyName, double min, double max, out double value)
    {
        value = 0;
        return root.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.Number &&
            property.TryGetDouble(out value) &&
            value >= min &&
            value <= max;
    }

    private static bool TryGetBoundedInt(JsonElement root, string propertyName, int min, int max, out int value)
    {
        value = 0;
        return root.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.Number &&
            property.TryGetInt32(out value) &&
            value >= min &&
            value <= max;
    }

    private static bool TryGetOptionalSequence(JsonElement root, out long? sequence)
    {
        sequence = null;
        if (!root.TryGetProperty("seq", out var property))
        {
            return true;
        }

        if (property.ValueKind != JsonValueKind.Number || !property.TryGetInt64(out var value) || value <= 0)
        {
            return false;
        }

        sequence = value;
        return true;
    }

    private static bool TryGetOptionalStringArray(
        JsonElement root,
        string propertyName,
        int maxItems,
        int maxItemLength,
        out string[] values)
    {
        values = [];
        if (!root.TryGetProperty(propertyName, out var property))
        {
            return true;
        }

        if (property.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var items = new List<string>(Math.Min(maxItems, property.GetArrayLength()));
        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var value = item.GetString();
            if (string.IsNullOrEmpty(value) || value.Length > maxItemLength)
            {
                return false;
            }

            items.Add(value);
            if (items.Count > maxItems)
            {
                return false;
            }
        }

        values = [.. items];
        return values.Length > 0;
    }

    private static bool TryGetRequiredStringArray(
        JsonElement root,
        string propertyName,
        int maxItems,
        int maxItemLength,
        out string[] values)
    {
        values = [];
        if (!root.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return false;
        }
        var items = new List<string>(Math.Min(maxItems, property.GetArrayLength()));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in property.EnumerateArray())
        {
            var value = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
            if (string.IsNullOrEmpty(value) || value.Length > maxItemLength || !seen.Add(value) || items.Count >= maxItems)
            {
                return false;
            }
            items.Add(value);
        }
        values = [.. items];
        return true;
    }

    private static bool TryGetPointerDelta(JsonElement root, string propertyName, out int value)
    {
        value = 0;
        if (!TryGetNumber(root, propertyName, -MaxPointerDelta, MaxPointerDelta, out var number))
        {
            return false;
        }

        value = (int)Math.Clamp(Math.Round(number), -MaxPointerDelta, MaxPointerDelta);
        return true;
    }
}
