namespace VolturaAir.Host;

public sealed record HostPermissionSet(
    bool AllowPcSleep = false,
    bool AllowVolumeControl = true,
    bool AllowRemoteAppLaunch = true,
    bool AllowPcLock = true,
    bool AllowBlackoutDisplay = true,
    bool AllowDisplayOff = false,
    bool AllowScreenSaver = true,
    bool AllowSignOut = false,
    bool AllowRestart = false,
    bool AllowShutdown = false);

public sealed record DevicePermissionOverrides(
    bool? AllowPcSleep = null,
    bool? AllowVolumeControl = null,
    bool? AllowPcLock = null,
    bool? AllowBlackoutDisplay = null,
    bool? AllowDisplayOff = null,
    bool? AllowScreenSaver = null,
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
        AllowSignOut: false,
        AllowRestart: false,
        AllowShutdown: false);

    public static HostPermissionSet Resolve(HostPermissionSet global, DevicePermissionOverrides? deviceOverrides)
    {
        return new HostPermissionSet(
            AllowPcSleep: deviceOverrides?.AllowPcSleep ?? global.AllowPcSleep,
            AllowVolumeControl: deviceOverrides?.AllowVolumeControl ?? global.AllowVolumeControl,
            AllowRemoteAppLaunch: global.AllowRemoteAppLaunch,
            AllowPcLock: deviceOverrides?.AllowPcLock ?? global.AllowPcLock,
            AllowBlackoutDisplay: deviceOverrides?.AllowBlackoutDisplay ?? global.AllowBlackoutDisplay,
            AllowDisplayOff: deviceOverrides?.AllowDisplayOff ?? global.AllowDisplayOff,
            AllowScreenSaver: deviceOverrides?.AllowScreenSaver ?? global.AllowScreenSaver,
            AllowSignOut: deviceOverrides?.AllowSignOut ?? global.AllowSignOut,
            AllowRestart: deviceOverrides?.AllowRestart ?? global.AllowRestart,
            AllowShutdown: deviceOverrides?.AllowShutdown ?? global.AllowShutdown);
    }
}
