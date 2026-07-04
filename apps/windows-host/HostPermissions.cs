namespace VolturaAir.Host;

public sealed record HostPermissionSet(
    bool AllowPcSleep = false,
    bool AllowVolumeControl = true);

public sealed record DevicePermissionOverrides(
    bool? AllowPcSleep = null,
    bool? AllowVolumeControl = null);

public static class HostPermissions
{
    public static HostPermissionSet DefaultGlobal { get; } = new(AllowPcSleep: false);

    public static HostPermissionSet Resolve(HostPermissionSet global, DevicePermissionOverrides? deviceOverrides)
    {
        return new HostPermissionSet(
            AllowPcSleep: deviceOverrides?.AllowPcSleep ?? global.AllowPcSleep,
            AllowVolumeControl: deviceOverrides?.AllowVolumeControl ?? global.AllowVolumeControl);
    }
}
