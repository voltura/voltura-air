using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed class HostPermissionsTests
{
    [Fact]
    public void GlobalDefaultBlocksSleep()
    {
        Assert.False(HostPermissions.DefaultGlobal.AllowPcSleep);
        Assert.True(HostPermissions.DefaultGlobal.AllowVolumeControl);
        Assert.True(HostPermissions.DefaultGlobal.AllowRemoteAppLaunch);
        Assert.False(HostPermissions.DefaultGlobal.AllowUrlOpen);
        Assert.True(HostPermissions.DefaultGlobal.AllowPcLock);
        Assert.True(HostPermissions.DefaultGlobal.AllowBlackoutDisplay);
        Assert.False(HostPermissions.DefaultGlobal.AllowDisplayOff);
        Assert.True(HostPermissions.DefaultGlobal.AllowScreenSaver);
        Assert.False(HostPermissions.DefaultGlobal.AllowAwakeControl);
        Assert.False(HostPermissions.DefaultGlobal.AllowSignOut);
        Assert.False(HostPermissions.DefaultGlobal.AllowRestart);
        Assert.False(HostPermissions.DefaultGlobal.AllowShutdown);
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
    public void DeviceInheritsGlobalRemoteAppLaunchByDefault()
    {
        var globallyAllowed = HostPermissions.Resolve(new HostPermissionSet(AllowRemoteAppLaunch: true), new DevicePermissionOverrides());
        var globallyBlocked = HostPermissions.Resolve(new HostPermissionSet(AllowRemoteAppLaunch: false), new DevicePermissionOverrides());

        Assert.True(globallyAllowed.AllowRemoteAppLaunch);
        Assert.False(globallyBlocked.AllowRemoteAppLaunch);
    }

    [Fact]
    public void DeviceRemoteAppLaunchOverrideWinsOverGlobal()
    {
        var allowed = HostPermissions.Resolve(
            new HostPermissionSet(AllowRemoteAppLaunch: false),
            new DevicePermissionOverrides(AllowRemoteAppLaunch: true));
        var blocked = HostPermissions.Resolve(
            new HostPermissionSet(AllowRemoteAppLaunch: true),
            new DevicePermissionOverrides(AllowRemoteAppLaunch: false));

        Assert.True(allowed.AllowRemoteAppLaunch);
        Assert.False(blocked.AllowRemoteAppLaunch);
    }

    [Fact]
    public void DeviceUrlOpenOverrideWinsOverGlobal()
    {
        var allowed = HostPermissions.Resolve(
            new HostPermissionSet(AllowUrlOpen: false),
            new DevicePermissionOverrides(AllowUrlOpen: true));
        var blocked = HostPermissions.Resolve(
            new HostPermissionSet(AllowUrlOpen: true),
            new DevicePermissionOverrides(AllowUrlOpen: false));

        Assert.True(allowed.AllowUrlOpen);
        Assert.False(blocked.AllowUrlOpen);
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
    public void DeviceAwakeOverrideWinsOverGlobal()
    {
        var allowed = HostPermissions.Resolve(
            new HostPermissionSet(AllowAwakeControl: false),
            new DevicePermissionOverrides(AllowAwakeControl: true));
        var blocked = HostPermissions.Resolve(
            new HostPermissionSet(AllowAwakeControl: true),
            new DevicePermissionOverrides(AllowAwakeControl: false));

        Assert.True(allowed.AllowAwakeControl);
        Assert.False(blocked.AllowAwakeControl);
    }

    [Fact]
    public void DevicePowerOverridesWinOverEachGlobalPermission()
    {
        var global = new HostPermissionSet(
            AllowPcLock: false,
            AllowBlackoutDisplay: false,
            AllowDisplayOff: true,
            AllowScreenSaver: false,
            AllowSignOut: false,
            AllowRestart: true,
            AllowShutdown: false);
        var overrides = new DevicePermissionOverrides(
            AllowPcLock: true,
            AllowBlackoutDisplay: true,
            AllowDisplayOff: false,
            AllowScreenSaver: true,
            AllowAwakeControl: true,
            AllowSignOut: true,
            AllowRestart: false,
            AllowShutdown: true);

        var effective = HostPermissions.Resolve(global, overrides);

        Assert.True(effective.AllowPcLock);
        Assert.True(effective.AllowBlackoutDisplay);
        Assert.False(effective.AllowDisplayOff);
        Assert.True(effective.AllowScreenSaver);
        Assert.True(effective.AllowSignOut);
        Assert.False(effective.AllowRestart);
        Assert.True(effective.AllowShutdown);
    }

    [Fact]
    public void RemovedDeviceLosesPermissionOverridesWithPairingRecord()
    {
        using var store = new TempPairingStore();
        var manager = new PairingManager(store.Store);
        var now = DateTimeOffset.UtcNow;
        var token = manager.CreatePairingToken(now);
        manager.Accept("client-a", "Phone", token, null, now);

        var saved = manager.SetDevicePermissionOverrides("client-a", new DevicePermissionOverrides(
            AllowPcSleep: true,
            AllowVolumeControl: true,
            AllowRemoteAppLaunch: true,
            AllowUrlOpen: true,
            AllowPcLock: true,
            AllowBlackoutDisplay: true,
            AllowDisplayOff: true,
            AllowScreenSaver: true,
            AllowAwakeControl: true,
            AllowSignOut: true,
            AllowRestart: true,
            AllowShutdown: true));
        var reloaded = new PairingManager(store.Store).GetDevicePermissionOverrides("client-a");
        var removed = manager.DisconnectDevice("client-a");

        Assert.True(saved);
        Assert.True(reloaded.AllowRemoteAppLaunch);
        Assert.True(reloaded.AllowUrlOpen);
        Assert.True(reloaded.AllowPcLock);
        Assert.True(reloaded.AllowBlackoutDisplay);
        Assert.True(reloaded.AllowDisplayOff);
        Assert.True(reloaded.AllowScreenSaver);
        Assert.True(reloaded.AllowAwakeControl);
        Assert.True(reloaded.AllowSignOut);
        Assert.True(reloaded.AllowRestart);
        Assert.True(reloaded.AllowShutdown);
        Assert.True(removed);
        Assert.Null(manager.GetDevicePermissionOverrides("client-a").AllowPcSleep);
        Assert.Null(manager.GetDevicePermissionOverrides("client-a").AllowVolumeControl);
        Assert.Null(manager.GetDevicePermissionOverrides("client-a").AllowRemoteAppLaunch);
        Assert.Null(manager.GetDevicePermissionOverrides("client-a").AllowUrlOpen);
        Assert.Null(manager.GetDevicePermissionOverrides("client-a").AllowPcLock);
        Assert.Null(manager.GetDevicePermissionOverrides("client-a").AllowBlackoutDisplay);
        Assert.Null(manager.GetDevicePermissionOverrides("client-a").AllowDisplayOff);
        Assert.Null(manager.GetDevicePermissionOverrides("client-a").AllowScreenSaver);
        Assert.Null(manager.GetDevicePermissionOverrides("client-a").AllowAwakeControl);
        Assert.Null(manager.GetDevicePermissionOverrides("client-a").AllowSignOut);
        Assert.Null(manager.GetDevicePermissionOverrides("client-a").AllowRestart);
        Assert.Null(manager.GetDevicePermissionOverrides("client-a").AllowShutdown);
        Assert.Empty(store.Store.Load());
    }
}
