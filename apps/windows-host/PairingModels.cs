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
    DevicePermissionOverrides? PermissionOverrides = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? PointerSpeedOverride = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? ShowModeButtonsOverride = null);

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
    DevicePermissionOverrides PermissionOverrides,
    int? PointerSpeedOverride,
    int PointerSpeed,
    bool? ShowModeButtonsOverride,
    bool ShowModeButtons)
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

public sealed record PairingResult(bool Accepted, string Reason);

internal sealed record PairingCode(string Value, DateTimeOffset ExpiresAt, DateTimeOffset RefreshAt);
