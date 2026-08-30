using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed class HostPermissionsTests
{
    [Fact]
    public void MyDeviceAllowsEveryCatalogPermission()
    {
        Assert.Equal(23, DeviceAccessProfiles.Permissions.Count);
        Assert.All(
            DeviceAccessProfiles.Permissions,
            permission => Assert.True(permission.Read(DeviceAccessProfiles.MyDevice), permission.PersistedKey));
    }

    [Fact]
    public void RemoteControlsUsesExactWhitelistAndBlocksComplement()
    {
        var expected = new HashSet<DevicePermissionKind>
        {
            DevicePermissionKind.RemoteInput,
            DevicePermissionKind.VolumeControl,
            DevicePermissionKind.PresentationControl,
            DevicePermissionKind.RemoteAppLaunch,
            DevicePermissionKind.AppsControl,
            DevicePermissionKind.PcLock,
            DevicePermissionKind.BlackoutDisplay,
            DevicePermissionKind.ScreenSaver
        };

        foreach (var permission in DeviceAccessProfiles.Permissions)
        {
            Assert.Equal(
                expected.Contains(permission.Kind),
                permission.Read(DeviceAccessProfiles.RemoteControls));
        }

        Assert.Equal(expected.Count, DeviceAccessProfiles.Permissions.Count(permission => permission.RemoteControlsAllowed));
        Assert.True(DeviceAccessProfiles.MyDevice.AllowDiagnostics);
        Assert.False(DeviceAccessProfiles.RemoteControls.AllowDiagnostics);
        Assert.True(DeviceAccessProfiles.MyDevice.AllowTerminal);
        Assert.False(DeviceAccessProfiles.RemoteControls.AllowTerminal);
        Assert.True(DeviceAccessProfiles.RemoteControls.AllowAppsControl);
    }

    [Fact]
    public void ProtectedFileFilteringIsOutsideProfiles()
    {
        Assert.DoesNotContain(
            DeviceAccessProfiles.Permissions,
            permission => permission.PersistedKey == "hideProtectedFileSystemItems");
        Assert.True(DeviceAccessProfiles.MyDevice.HideProtectedFileSystemItems);
        Assert.True(DeviceAccessProfiles.RemoteControls.HideProtectedFileSystemItems);

        var effective = HostPermissions.Resolve(
            DeviceAccessProfile.RemoteControls,
            new DevicePermissionOverrides(HideProtectedFileSystemItems: false),
            HostPermissions.DefaultGlobal);

        Assert.False(effective.HideProtectedFileSystemItems);
        Assert.True(effective.AllowRemoteInput);
    }

    [Fact]
    public void HostWindowAndTrayControlIsNotAProfilePermission()
    {
        Assert.Null(typeof(HostPermissionSet).GetProperty("AllowClientControl"));
        Assert.Null(typeof(DevicePermissionOverrides).GetProperty("AllowClientControl"));
        Assert.DoesNotContain(
            DeviceAccessProfiles.Permissions,
            permission => permission.PersistedKey.Contains("clientControl", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MissingCustomValueFailsEntireMatrixClosed()
    {
        var effective = HostPermissions.Resolve(
            DeviceAccessProfile.Custom,
            new DevicePermissionOverrides(AllowRemoteInput: true),
            HostPermissions.DefaultGlobal);

        Assert.All(
            DeviceAccessProfiles.Permissions,
            permission => Assert.False(permission.Read(effective), permission.PersistedKey));
    }

    [Fact]
    public void UnknownProfileAndPermissionFailClosed()
    {
        var effective = HostPermissions.Resolve(
            DeviceAccessProfile.Invalid,
            DeviceAccessProfiles.ToCompleteOverrides(DeviceAccessProfiles.MyDevice),
            HostPermissions.DefaultGlobal);

        Assert.All(
            DeviceAccessProfiles.Permissions,
            permission => Assert.False(permission.Read(effective), permission.PersistedKey));
        Assert.False(DeviceAccessProfiles.Read(effective, (DevicePermissionKind)int.MaxValue));
    }

    [Fact]
    public void LegacyResolverPreservesGlobalAndPerDevicePrecedence()
    {
        var global = HostPermissions.DefaultGlobal with
        {
            AllowRemoteInput = false,
            AllowPcSleep = true,
            HideProtectedFileSystemItems = true
        };
        var effective = HostPermissions.ResolveLegacy(
            global,
            new DevicePermissionOverrides(
                AllowRemoteInput: true,
                AllowPcSleep: false,
                HideProtectedFileSystemItems: false));

        Assert.True(effective.AllowRemoteInput);
        Assert.False(effective.AllowPcSleep);
        Assert.False(effective.HideProtectedFileSystemItems);
    }

    [Fact]
    public void EditingBuiltInMaterializesCustomAndApplyingBuiltInClearsValues()
    {
        using var store = new TempPairingStore();
        using var key = new PairingTestKey();
        var manager = new PairingManager(store.Store);
        Assert.True(manager.AcceptPairing(
            "client-a",
            "Phone",
            manager.CreatePairingToken(),
            reconnectPublicKey: key.PublicKey).Accepted);

        Assert.True(manager.SetDevicePermission("client-a", DevicePermissionKind.FileBrowsing, false));
        Assert.True(manager.SetDeviceProtectedFileFilterOverride("client-a", false));
        Assert.Equal(DeviceAccessProfile.Custom, manager.GetDeviceAccessProfile("client-a"));
        var custom = Assert.Single(store.Store.Load());
        Assert.Equal(DeviceAccessProfile.Custom, custom.AccessProfile);
        Assert.All(
            DeviceAccessProfiles.Permissions,
            permission => Assert.NotNull(permission.ReadOverride(custom.PermissionOverrides!)));

        Assert.True(manager.SetDeviceAccessProfile("client-a", DeviceAccessProfile.RemoteControls));
        var builtIn = Assert.Single(store.Store.Load());
        Assert.Equal(DeviceAccessProfile.RemoteControls, builtIn.AccessProfile);
        Assert.All(
            DeviceAccessProfiles.Permissions,
            permission => Assert.Null(permission.ReadOverride(builtIn.PermissionOverrides!)));
        Assert.False(builtIn.PermissionOverrides!.HideProtectedFileSystemItems);
    }

    [Fact]
    public void SelectingCustomFromBuiltInKeepsEffectiveMatrix()
    {
        using var store = new TempPairingStore();
        using var key = new PairingTestKey();
        var manager = new PairingManager(store.Store);
        manager.AcceptPairing(
            "client-a",
            "Phone",
            manager.CreatePairingToken(),
            reconnectPublicKey: key.PublicKey);
        manager.SetDeviceAccessProfile("client-a", DeviceAccessProfile.RemoteControls);
        var before = manager.GetEffectivePermissions("client-a", AppPermissionSettings.Load());

        Assert.True(manager.SetDeviceAccessProfile("client-a", DeviceAccessProfile.Custom));

        Assert.Equal(before, manager.GetEffectivePermissions("client-a", AppPermissionSettings.Load()));
        Assert.Equal(DeviceAccessProfile.Custom, manager.GetDeviceAccessProfile("client-a"));
    }
}
