namespace VolturaAir.Host;

internal static class PairingRecordNormalization
{
    public static PairingRecord Normalize(
        PairingRecord record,
        HostPermissionSet legacyGlobalPermissions)
    {
        var normalized = (record.AddedAt == default ? record with { AddedAt = DateTimeOffset.UtcNow } : record) with
        {
            Platform = NormalizeMetadata(record.Platform),
            Browser = NormalizeMetadata(record.Browser),
            DisplayMode = NormalizeMetadata(record.DisplayMode),
            PermissionOverrides = NormalizePermissionOverrides(record.PermissionOverrides),
            PointerSpeedOverride = NormalizePointerSpeedOverride(record.PointerSpeedOverride),
            CustomScreenViewport = NormalizeCustomScreenViewport(record.CustomScreenViewport)
        };
        return DeviceAccessProfilePersistence.Normalize(normalized, legacyGlobalPermissions);
    }

    public static string NormalizeDeviceName(string deviceName)
    {
        var trimmed = deviceName.Trim();
        return trimmed.Length > 0 ? trimmed : "Mobile device";
    }

    public static string NormalizeMetadata(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? string.Empty : trimmed.Length > 80 ? trimmed[..80] : trimmed;
    }

    public static void Upsert(List<PairingRecord> records, PairingRecord record)
    {
        var index = records.FindIndex(existing =>
            string.Equals(existing.ClientId, record.ClientId, StringComparison.Ordinal));
        if (index < 0)
        {
            records.Add(record);
            return;
        }

        var existing = records[index];
        records[index] = record with
        {
            AddedAt = existing.AddedAt == default ? record.AddedAt : existing.AddedAt,
            LastConnectedAt = existing.LastConnectedAt,
            LastDisconnectedAt = existing.LastDisconnectedAt,
            LastRenamedAt = existing.LastRenamedAt,
            Platform = string.IsNullOrWhiteSpace(record.Platform) ? existing.Platform : record.Platform,
            Browser = string.IsNullOrWhiteSpace(record.Browser) ? existing.Browser : record.Browser,
            DisplayMode = string.IsNullOrWhiteSpace(record.DisplayMode) ? existing.DisplayMode : record.DisplayMode,
            HostIdentityFingerprint = record.HostIdentityFingerprint,
            AccessProfile = existing.AccessProfile,
            PermissionOverrides = existing.PermissionOverrides,
            InitialAccessNoticePending = existing.InitialAccessNoticePending,
            PointerSpeedOverride = existing.PointerSpeedOverride,
            ShowModeButtonsOverride = existing.ShowModeButtonsOverride,
            ControlDepthOverride = existing.ControlDepthOverride,
            CustomScreenViewport = existing.CustomScreenViewport
        };
    }

    private static DevicePermissionOverrides NormalizePermissionOverrides(DevicePermissionOverrides? permissionOverrides) => new(
        AllowRemoteInput: permissionOverrides?.AllowRemoteInput,
        AllowPcSleep: permissionOverrides?.AllowPcSleep,
        AllowVolumeControl: permissionOverrides?.AllowVolumeControl,
        AllowPresentationControl: permissionOverrides?.AllowPresentationControl,
        AllowRemoteAppLaunch: permissionOverrides?.AllowRemoteAppLaunch,
        AllowUrlOpen: permissionOverrides?.AllowUrlOpen,
        AllowPcLock: permissionOverrides?.AllowPcLock,
        AllowBlackoutDisplay: permissionOverrides?.AllowBlackoutDisplay,
        AllowDisplayControl: permissionOverrides?.AllowDisplayControl,
        AllowScreenSaver: permissionOverrides?.AllowScreenSaver,
        AllowAwakeControl: permissionOverrides?.AllowAwakeControl,
        AllowClipboardRead: permissionOverrides?.AllowClipboardRead,
        AllowScreenViewing: permissionOverrides?.AllowScreenViewing,
        AllowPhoneWebcam: permissionOverrides?.AllowPhoneWebcam,
        AllowSignOut: permissionOverrides?.AllowSignOut,
        AllowRestart: permissionOverrides?.AllowRestart,
        AllowShutdown: permissionOverrides?.AllowShutdown,
        AllowFileBrowsing: permissionOverrides?.AllowFileBrowsing,
        AllowFileChanges: permissionOverrides?.AllowFileChanges,
        HideProtectedFileSystemItems: permissionOverrides?.HideProtectedFileSystemItems);

    private static CustomScreenViewport? NormalizeCustomScreenViewport(CustomScreenViewport? viewport) =>
        viewport is not null &&
        viewport.Width is >= CustomScreenLimits.MinViewportWidth and <= CustomScreenLimits.MaxViewportWidth &&
        viewport.Height is >= CustomScreenLimits.MinViewportHeight and <= CustomScreenLimits.MaxViewportHeight &&
        viewport.Orientation is "portrait" or "landscape"
            ? viewport
            : null;

    private static int? NormalizePointerSpeedOverride(int? pointerSpeedOverride) =>
        pointerSpeedOverride is not null
            ? DevicePointerProfile.NormalizePointerSpeed(pointerSpeedOverride.Value)
            : null;
}
