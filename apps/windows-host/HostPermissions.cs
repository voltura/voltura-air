namespace VolturaAir.Host;

public sealed record HostPermissionSet(
    bool AllowPcSleep = false,
    bool AllowVolumeControl = true,
    bool AllowRemoteAppLaunch = true,
    bool AllowPcLock = true,
    bool AllowBlackoutDisplay = true,
    bool AllowDisplayOff = false,
    bool AllowScreenSaver = true,
    bool AllowAwakeControl = false,
    bool AllowClipboardRead = false,
    bool AllowSignOut = false,
    bool AllowRestart = false,
    bool AllowShutdown = false);

public sealed record DevicePermissionOverrides(
    bool? AllowPcSleep = null,
    bool? AllowVolumeControl = null,
    bool? AllowRemoteAppLaunch = null,
    bool? AllowPcLock = null,
    bool? AllowBlackoutDisplay = null,
    bool? AllowDisplayOff = null,
    bool? AllowScreenSaver = null,
    bool? AllowAwakeControl = null,
    bool? AllowClipboardRead = null,
    bool? AllowSignOut = null,
    bool? AllowRestart = null,
    bool? AllowShutdown = null);

public static class HostPermissions
{
    public static HostPermissionSet DefaultGlobal { get; } = new(
        AllowPcSleep: false,
        AllowVolumeControl: true,
        AllowRemoteAppLaunch: true,
        AllowPcLock: true,
        AllowBlackoutDisplay: true,
        AllowDisplayOff: false,
        AllowScreenSaver: true,
        AllowAwakeControl: false,
        AllowClipboardRead: false,
        AllowSignOut: false,
        AllowRestart: false,
        AllowShutdown: false);

    public static HostPermissionSet Resolve(HostPermissionSet global, DevicePermissionOverrides? deviceOverrides)
    {
        return new HostPermissionSet(
            AllowPcSleep: deviceOverrides?.AllowPcSleep ?? global.AllowPcSleep,
            AllowVolumeControl: deviceOverrides?.AllowVolumeControl ?? global.AllowVolumeControl,
            AllowRemoteAppLaunch: deviceOverrides?.AllowRemoteAppLaunch ?? global.AllowRemoteAppLaunch,
            AllowPcLock: deviceOverrides?.AllowPcLock ?? global.AllowPcLock,
            AllowBlackoutDisplay: deviceOverrides?.AllowBlackoutDisplay ?? global.AllowBlackoutDisplay,
            AllowDisplayOff: deviceOverrides?.AllowDisplayOff ?? global.AllowDisplayOff,
            AllowScreenSaver: deviceOverrides?.AllowScreenSaver ?? global.AllowScreenSaver,
            AllowAwakeControl: deviceOverrides?.AllowAwakeControl ?? global.AllowAwakeControl,
            AllowClipboardRead: deviceOverrides?.AllowClipboardRead ?? global.AllowClipboardRead,
            AllowSignOut: deviceOverrides?.AllowSignOut ?? global.AllowSignOut,
            AllowRestart: deviceOverrides?.AllowRestart ?? global.AllowRestart,
            AllowShutdown: deviceOverrides?.AllowShutdown ?? global.AllowShutdown);
    }
}
