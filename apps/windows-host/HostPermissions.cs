using System.Text.Json.Serialization;

namespace VolturaAir.Host;

public sealed record HostPermissionSet(
    bool AllowRemoteInput = true,
    bool AllowPcSleep = false,
    bool AllowVolumeControl = true,
    bool AllowPresentationControl = true,
    bool AllowRemoteAppLaunch = true,
    bool AllowUrlOpen = false,
    bool AllowPcLock = true,
    bool AllowBlackoutDisplay = true,
    bool AllowDisplayControl = false,
    bool AllowScreenSaver = true,
    bool AllowAwakeControl = false,
    bool AllowClipboardRead = false,
    bool AllowScreenViewing = false,
    bool AllowPhoneWebcam = false,
    bool AllowSignOut = false,
    bool AllowRestart = false,
    bool AllowShutdown = false,
    bool AllowFileBrowsing = false,
    bool AllowFileChanges = false,
    bool AllowFileTransfer = false,
    bool AllowDiagnostics = false,
    bool HideProtectedFileSystemItems = true);

[JsonConverter(typeof(DevicePermissionOverridesJsonConverter))]
public sealed record DevicePermissionOverrides(
    bool? AllowRemoteInput = null,
    bool? AllowPcSleep = null,
    bool? AllowVolumeControl = null,
    bool? AllowPresentationControl = null,
    bool? AllowRemoteAppLaunch = null,
    bool? AllowUrlOpen = null,
    bool? AllowPcLock = null,
    bool? AllowBlackoutDisplay = null,
    bool? AllowDisplayControl = null,
    bool? AllowScreenSaver = null,
    bool? AllowAwakeControl = null,
    bool? AllowClipboardRead = null,
    bool? AllowScreenViewing = null,
    bool? AllowPhoneWebcam = null,
    bool? AllowSignOut = null,
    bool? AllowRestart = null,
    bool? AllowShutdown = null,
    bool? AllowFileBrowsing = null,
    bool? AllowFileChanges = null,
    bool? AllowFileTransfer = null,
    bool? AllowDiagnostics = null,
    bool? HideProtectedFileSystemItems = null);

public static class HostPermissions
{
    public static HostPermissionSet DefaultGlobal { get; } = new(
        AllowRemoteInput: true,
        AllowPcSleep: false,
        AllowVolumeControl: true,
        AllowPresentationControl: true,
        AllowRemoteAppLaunch: true,
        AllowUrlOpen: false,
        AllowPcLock: true,
        AllowBlackoutDisplay: true,
        AllowDisplayControl: false,
        AllowScreenSaver: true,
        AllowAwakeControl: false,
        AllowClipboardRead: false,
        AllowScreenViewing: false,
        AllowPhoneWebcam: false,
        AllowSignOut: false,
        AllowRestart: false,
        AllowShutdown: false,
        AllowFileBrowsing: false,
        AllowFileChanges: false,
        AllowFileTransfer: false,
        AllowDiagnostics: false,
        HideProtectedFileSystemItems: true);

    public static HostPermissionSet Resolve(
        DeviceAccessProfile profile,
        DevicePermissionOverrides? deviceOverrides,
        HostPermissionSet global)
    {
        var hideProtected = deviceOverrides?.HideProtectedFileSystemItems ?? global.HideProtectedFileSystemItems;
        if (DeviceAccessProfiles.IsBuiltIn(profile))
        {
            var matrix = DeviceAccessProfiles.GetBuiltInMatrix(profile);
            return matrix.HideProtectedFileSystemItems == hideProtected
                ? matrix
                : matrix with { HideProtectedFileSystemItems = hideProtected };
        }

        return profile == DeviceAccessProfile.Custom &&
            DeviceAccessProfiles.TryResolveCustom(deviceOverrides, hideProtected, out var custom)
                ? custom
                : DeviceAccessProfiles.AllBlocked with { HideProtectedFileSystemItems = hideProtected };
    }

    public static HostPermissionSet ResolveLegacy(HostPermissionSet global, DevicePermissionOverrides? deviceOverrides)
    {
        return new HostPermissionSet(
            AllowRemoteInput: deviceOverrides?.AllowRemoteInput ?? global.AllowRemoteInput,
            AllowPcSleep: deviceOverrides?.AllowPcSleep ?? global.AllowPcSleep,
            AllowVolumeControl: deviceOverrides?.AllowVolumeControl ?? global.AllowVolumeControl,
            AllowPresentationControl: deviceOverrides?.AllowPresentationControl ?? global.AllowPresentationControl,
            AllowRemoteAppLaunch: deviceOverrides?.AllowRemoteAppLaunch ?? global.AllowRemoteAppLaunch,
            AllowUrlOpen: deviceOverrides?.AllowUrlOpen ?? global.AllowUrlOpen,
            AllowPcLock: deviceOverrides?.AllowPcLock ?? global.AllowPcLock,
            AllowBlackoutDisplay: deviceOverrides?.AllowBlackoutDisplay ?? global.AllowBlackoutDisplay,
            AllowDisplayControl: deviceOverrides?.AllowDisplayControl ?? global.AllowDisplayControl,
            AllowScreenSaver: deviceOverrides?.AllowScreenSaver ?? global.AllowScreenSaver,
            AllowAwakeControl: deviceOverrides?.AllowAwakeControl ?? global.AllowAwakeControl,
            AllowClipboardRead: deviceOverrides?.AllowClipboardRead ?? global.AllowClipboardRead,
            AllowScreenViewing: deviceOverrides?.AllowScreenViewing ?? global.AllowScreenViewing,
            AllowPhoneWebcam: deviceOverrides?.AllowPhoneWebcam ?? global.AllowPhoneWebcam,
            AllowSignOut: deviceOverrides?.AllowSignOut ?? global.AllowSignOut,
            AllowRestart: deviceOverrides?.AllowRestart ?? global.AllowRestart,
            AllowShutdown: deviceOverrides?.AllowShutdown ?? global.AllowShutdown,
            AllowFileBrowsing: deviceOverrides?.AllowFileBrowsing ?? global.AllowFileBrowsing,
            AllowFileChanges: deviceOverrides?.AllowFileChanges ?? global.AllowFileChanges,
            AllowFileTransfer: deviceOverrides?.AllowFileTransfer ?? global.AllowFileTransfer,
            AllowDiagnostics: deviceOverrides?.AllowDiagnostics ?? global.AllowDiagnostics,
            HideProtectedFileSystemItems: deviceOverrides?.HideProtectedFileSystemItems ?? global.HideProtectedFileSystemItems);
    }
}
