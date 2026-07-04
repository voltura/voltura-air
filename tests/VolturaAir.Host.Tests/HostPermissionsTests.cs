using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed class HostPermissionsTests
{
    [Fact]
    public void GlobalDefaultBlocksSleep()
    {
        Assert.False(HostPermissions.DefaultGlobal.AllowPcSleep);
        Assert.True(HostPermissions.DefaultGlobal.AllowVolumeControl);
    }

    [Fact]
    public void DeviceInheritsGlobalByDefault()
    {
        var globallyAllowed = HostPermissions.Resolve(new HostPermissionSet(AllowPcSleep: true), new DevicePermissionOverrides());
        var globallyBlocked = HostPermissions.Resolve(new HostPermissionSet(AllowPcSleep: false), new DevicePermissionOverrides());

        Assert.True(globallyAllowed.AllowPcSleep);
        Assert.False(globallyBlocked.AllowPcSleep);
    }

    [Fact]
    public void DeviceInheritsGlobalVolumeControlByDefault()
    {
        var globallyAllowed = HostPermissions.Resolve(new HostPermissionSet(AllowVolumeControl: true), new DevicePermissionOverrides());
        var globallyBlocked = HostPermissions.Resolve(new HostPermissionSet(AllowVolumeControl: false), new DevicePermissionOverrides());

        Assert.True(globallyAllowed.AllowVolumeControl);
        Assert.False(globallyBlocked.AllowVolumeControl);
    }

    [Fact]
    public void DeviceAllowOverridesGlobalBlock()
    {
        var effective = HostPermissions.Resolve(
            new HostPermissionSet(AllowPcSleep: false),
            new DevicePermissionOverrides(AllowPcSleep: true));

        Assert.True(effective.AllowPcSleep);
    }

    [Fact]
    public void DeviceBlockOverridesGlobalAllow()
    {
        var effective = HostPermissions.Resolve(
            new HostPermissionSet(AllowPcSleep: true),
            new DevicePermissionOverrides(AllowPcSleep: false));

        Assert.False(effective.AllowPcSleep);
    }

    [Fact]
    public void DeviceVolumeOverrideWinsOverGlobal()
    {
        var allowed = HostPermissions.Resolve(
            new HostPermissionSet(AllowVolumeControl: false),
            new DevicePermissionOverrides(AllowVolumeControl: true));
        var blocked = HostPermissions.Resolve(
            new HostPermissionSet(AllowVolumeControl: true),
            new DevicePermissionOverrides(AllowVolumeControl: false));

        Assert.True(allowed.AllowVolumeControl);
        Assert.False(blocked.AllowVolumeControl);
    }

    [Fact]
    public void RemovedDeviceLosesPermissionOverridesWithPairingRecord()
    {
        using var store = new TempPairingStore();
        var manager = new PairingManager(store.Store);
        var now = DateTimeOffset.UtcNow;
        var token = manager.CreatePairingToken(now);
        manager.Accept("client-a", "Phone", token, null, now);

        var saved = manager.SetDevicePermissionOverrides("client-a", new DevicePermissionOverrides(AllowPcSleep: true, AllowVolumeControl: true));
        var removed = manager.DisconnectDevice("client-a");

        Assert.True(saved);
        Assert.True(removed);
        Assert.Null(manager.GetDevicePermissionOverrides("client-a").AllowPcSleep);
        Assert.Null(manager.GetDevicePermissionOverrides("client-a").AllowVolumeControl);
        Assert.Empty(store.Store.Load());
    }
}
