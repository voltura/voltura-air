using System.Text.Json;
using System.Text.Json.Serialization;

namespace VolturaAir.Host;

[JsonConverter(typeof(DeviceConnectionMethodJsonConverter))]
public enum DeviceConnectionMethod
{
    Unknown,
    StandardLocal,
    EnhancedDirect,
    DebugDirect,
    CloudRelay
}

public sealed record PairingRecord(
    string ClientId,
    string ReconnectPublicKey,
    string DeviceName,
    DateTimeOffset AddedAt = default,
    DateTimeOffset? LastConnectedAt = null,
    DateTimeOffset? LastDisconnectedAt = null,
    DateTimeOffset? LastRenamedAt = null,
    string Platform = "",
    string Browser = "",
    string DisplayMode = "",
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? HostIdentityFingerprint = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [property: JsonConverter(typeof(NullableDeviceAccessProfileJsonConverter))]
    DeviceAccessProfile? AccessProfile = null,
    DevicePermissionOverrides? PermissionOverrides = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? InitialAccessNoticePending = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? PointerSpeedOverride = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? ShowModeButtonsOverride = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? ControlDepthOverride = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AccentColorOverride = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] CustomScreenViewport? CustomScreenViewport = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] DeviceConnectionMethod LastConnectionMethod = DeviceConnectionMethod.Unknown);

public sealed record PairedDeviceStatus(
    string ClientId,
    string DeviceName,
    bool IsActive,
    int ActiveConnections,
    DateTimeOffset AddedAt,
    DateTimeOffset? LastConnectedAt,
    DateTimeOffset? LastDisconnectedAt,
    DateTimeOffset? LastRenamedAt,
    string Platform,
    string Browser,
    string DisplayMode,
    string? HostIdentityFingerprint,
    DeviceAccessProfile AccessProfile,
    DevicePermissionOverrides PermissionOverrides,
    int? PointerSpeedOverride,
    int PointerSpeed,
    bool? ShowModeButtonsOverride,
    bool ShowModeButtons,
    bool? ControlDepthOverride,
    bool ControlDepth,
    string? AccentColorOverride,
    string? AccentColor,
    CustomScreenViewport? CustomScreenViewport,
    DeviceConnectionMethod LastConnectionMethod)
{
    public DateTimeOffset LatestActivityAt => new[] { AddedAt, LastConnectedAt, LastDisconnectedAt, LastRenamedAt }
        .Where(value => value.HasValue)
        .Select(value => value!.Value)
        .DefaultIfEmpty(DateTimeOffset.MinValue)
        .Max();
}

public static class DevicePointerProfile
{
    public const int MinPointerSpeed = 10;
    public const int MaxPointerSpeed = 100;
    public const int DefaultPointerSpeed = 100;

    public static int NormalizePointerSpeed(int pointerSpeed)
    {
        return Math.Max(MinPointerSpeed, Math.Min(MaxPointerSpeed, pointerSpeed));
    }
}

public sealed class PairingRevokedEventArgs(string? clientId) : EventArgs
{
    public string? ClientId { get; } = clientId;
}

internal sealed class DeviceConnectionMethodJsonConverter : JsonConverter<DeviceConnectionMethod>
{
    public override DeviceConnectionMethod Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            reader.Skip();
            return DeviceConnectionMethod.Unknown;
        }

        return reader.GetString() switch
        {
            "standard-local" => DeviceConnectionMethod.StandardLocal,
            "enhanced-direct" => DeviceConnectionMethod.EnhancedDirect,
            "debug-direct" => DeviceConnectionMethod.DebugDirect,
            "cloud-relay" => DeviceConnectionMethod.CloudRelay,
            _ => DeviceConnectionMethod.Unknown
        };
    }

    public override void Write(
        Utf8JsonWriter writer,
        DeviceConnectionMethod value,
        JsonSerializerOptions options) =>
        writer.WriteStringValue(value switch
        {
            DeviceConnectionMethod.StandardLocal => "standard-local",
            DeviceConnectionMethod.EnhancedDirect => "enhanced-direct",
            DeviceConnectionMethod.DebugDirect => "debug-direct",
            DeviceConnectionMethod.CloudRelay => "cloud-relay",
            _ => "unknown"
        });
}

public sealed record InitialDeviceConnectionNotice(
    string ClientId,
    string DeviceName,
    DeviceAccessProfile AccessProfile);

public sealed class InitialDeviceConnectionEventArgs(InitialDeviceConnectionNotice notice) : EventArgs
{
    public InitialDeviceConnectionNotice Notice { get; } = notice;
}

public sealed record PairingResult(bool Accepted, string Reason);

internal sealed record PairingCode(string Value, DateTimeOffset ExpiresAt, DateTimeOffset RefreshAt);

internal sealed record PairingBootstrapStartResult(
    bool Accepted,
    string Reason,
    PairingBootstrapPending? Pending = null);

internal sealed record PairingBootstrapPending(
    string ClientId,
    string DeviceName,
    string Token,
    string ClientNonce,
    string ServerNonce,
    string ReconnectPublicKey,
    string HostPublicKey,
    string HostFingerprint,
    string HostProof,
    string ExpectedClientProof,
    string Platform,
    string Browser,
    string DisplayMode);
