using System.Text.Json.Serialization;

namespace VolturaAir.Host.Features.UsageTelemetry;

internal enum UsageConnectionMethod
{
    StandardLocal,
    EnhancedDirect,
    Relay
}

internal enum UsageFeature
{
    Trackpad,
    Keyboard,
    Dictation,
    MediaControls,
    Presentation,
    CustomScreens,
    Files,
    ScreenViewing,
    PhoneWebcam,
    GyroMouse
}

[Flags]
internal enum UsageFeatureMask
{
    None = 0,
    Trackpad = 1 << 0,
    Keyboard = 1 << 1,
    Dictation = 1 << 2,
    MediaControls = 1 << 3,
    Presentation = 1 << 4,
    CustomScreens = 1 << 5,
    Files = 1 << 6,
    ScreenViewing = 1 << 7,
    PhoneWebcam = 1 << 8,
    GyroMouse = 1 << 9
}

internal readonly record struct UsageTelemetryRecordingToken(long Generation)
{
    public bool IsEnabled => Generation > 0;
}

internal enum UsageStatisticsRuntimeState
{
    Off,
    On,
    OffChoiceNotSaved,
    OffIdentityCleanupPending
}

internal interface IUsageTelemetryRecorder
{
    UsageTelemetryRecordingToken CurrentRecordingToken { get; }

    UsageTelemetrySessionRegistry? SessionRegistry { get; }

    bool TryRecordConnection(
        UsageConnectionMethod method,
        UsageTelemetryRecordingToken token);

    bool TryRecordFeature(
        UsageFeature feature,
        UsageTelemetryRecordingToken token);
}

internal interface IUsageStatisticsControl
{
    UsageStatisticsRuntimeState State { get; }

    UsageStatisticsDistribution Distribution { get; }

    event EventHandler? StateChanged;

    Task<UsageStatisticsTransitionResult> SetEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken = default);
}

internal sealed class NullUsageTelemetryRecorder : IUsageTelemetryRecorder
{
    public static NullUsageTelemetryRecorder Instance { get; } = new();

    private NullUsageTelemetryRecorder()
    {
    }

    public UsageTelemetryRecordingToken CurrentRecordingToken => default;

    public UsageTelemetrySessionRegistry? SessionRegistry => null;

    public bool TryRecordConnection(
        UsageConnectionMethod method,
        UsageTelemetryRecordingToken token)
    {
        _ = method;
        _ = token;
        return false;
    }

    public bool TryRecordFeature(
        UsageFeature feature,
        UsageTelemetryRecordingToken token)
    {
        _ = feature;
        _ = token;
        return false;
    }
}

internal sealed class NullUsageStatisticsControl : IUsageStatisticsControl
{
    public static NullUsageStatisticsControl Instance { get; } = new();

    private NullUsageStatisticsControl()
    {
    }

    public UsageStatisticsRuntimeState State => UsageStatisticsRuntimeState.Off;

    public UsageStatisticsDistribution Distribution => UsageStatisticsDistribution.Portable;

    public event EventHandler? StateChanged
    {
        add { }
        remove { }
    }

    public Task<UsageStatisticsTransitionResult> SetEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        _ = enabled;
        _ = cancellationToken;
        return Task.FromResult(new UsageStatisticsTransitionResult(false, false, false));
    }
}

internal sealed record UsageTelemetryConnections(
    [property: JsonPropertyName("standardLocal")] ushort StandardLocal,
    [property: JsonPropertyName("enhancedDirect")] ushort EnhancedDirect,
    [property: JsonPropertyName("relay")] ushort Relay);

internal sealed record UsageTelemetryFeatures(
    [property: JsonPropertyName("trackpad")] ushort Trackpad,
    [property: JsonPropertyName("keyboard")] ushort Keyboard,
    [property: JsonPropertyName("dictation")] ushort Dictation,
    [property: JsonPropertyName("mediaControls")] ushort MediaControls,
    [property: JsonPropertyName("presentation")] ushort Presentation,
    [property: JsonPropertyName("customScreens")] ushort CustomScreens,
    [property: JsonPropertyName("files")] ushort Files,
    [property: JsonPropertyName("screenViewing")] ushort ScreenViewing,
    [property: JsonPropertyName("phoneWebcam")] ushort PhoneWebcam,
    [property: JsonPropertyName("gyroMouse")] ushort GyroMouse);

internal sealed record UsageTelemetryBatch(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("installationId")] Guid InstallationId,
    [property: JsonPropertyName("batchId")] Guid BatchId,
    [property: JsonPropertyName("hostVersion")] string HostVersion,
    [property: JsonPropertyName("hostStarts")] ushort HostStarts,
    [property: JsonPropertyName("connections")] UsageTelemetryConnections Connections,
    [property: JsonPropertyName("features")] UsageTelemetryFeatures Features)
{
    [JsonIgnore]
    public bool HasCounts => HostStarts != 0 ||
        Connections.StandardLocal != 0 ||
        Connections.EnhancedDirect != 0 ||
        Connections.Relay != 0 ||
        Features.Trackpad != 0 ||
        Features.Keyboard != 0 ||
        Features.Dictation != 0 ||
        Features.MediaControls != 0 ||
        Features.Presentation != 0 ||
        Features.CustomScreens != 0 ||
        Features.Files != 0 ||
        Features.ScreenViewing != 0 ||
        Features.PhoneWebcam != 0 ||
        Features.GyroMouse != 0;
}

internal static class UsageFeatureMasks
{
    public static UsageFeatureMask For(UsageFeature feature) => feature switch
    {
        UsageFeature.Trackpad => UsageFeatureMask.Trackpad,
        UsageFeature.Keyboard => UsageFeatureMask.Keyboard,
        UsageFeature.Dictation => UsageFeatureMask.Dictation,
        UsageFeature.MediaControls => UsageFeatureMask.MediaControls,
        UsageFeature.Presentation => UsageFeatureMask.Presentation,
        UsageFeature.CustomScreens => UsageFeatureMask.CustomScreens,
        UsageFeature.Files => UsageFeatureMask.Files,
        UsageFeature.ScreenViewing => UsageFeatureMask.ScreenViewing,
        UsageFeature.PhoneWebcam => UsageFeatureMask.PhoneWebcam,
        UsageFeature.GyroMouse => UsageFeatureMask.GyroMouse,
        _ => throw new ArgumentOutOfRangeException(nameof(feature), feature, "Unsupported usage feature.")
    };
}
