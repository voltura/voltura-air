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
    bool AllowDisplayOff = false,
    bool AllowScreenSaver = true,
    bool AllowAwakeControl = false,
    bool AllowClipboardRead = false,
    bool AllowSignOut = false,
    bool AllowRestart = false,
    bool AllowShutdown = false);

public sealed record DevicePermissionOverrides(
    bool? AllowRemoteInput = null,
    bool? AllowPcSleep = null,
    bool? AllowVolumeControl = null,
    bool? AllowPresentationControl = null,
    bool? AllowRemoteAppLaunch = null,
    bool? AllowUrlOpen = null,
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
        AllowRemoteInput: true,
        AllowPcSleep: false,
        AllowVolumeControl: true,
        AllowPresentationControl: true,
        AllowRemoteAppLaunch: true,
        AllowUrlOpen: false,
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
            AllowRemoteInput: deviceOverrides?.AllowRemoteInput ?? global.AllowRemoteInput,
            AllowPcSleep: deviceOverrides?.AllowPcSleep ?? global.AllowPcSleep,
            AllowVolumeControl: deviceOverrides?.AllowVolumeControl ?? global.AllowVolumeControl,
            AllowPresentationControl: deviceOverrides?.AllowPresentationControl ?? global.AllowPresentationControl,
            AllowRemoteAppLaunch: deviceOverrides?.AllowRemoteAppLaunch ?? global.AllowRemoteAppLaunch,
            AllowUrlOpen: deviceOverrides?.AllowUrlOpen ?? global.AllowUrlOpen,
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
