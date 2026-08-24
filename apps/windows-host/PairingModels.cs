using System.Text.Json.Serialization;

namespace VolturaAir.Host;

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
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] CustomScreenViewport? CustomScreenViewport = null);

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
    CustomScreenViewport? CustomScreenViewport)
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
