using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed class ScreenViewTrayPermissionTests
{
    [Fact]
    public void BlockScreenViewingPermissionPersistsDeviceOverrideWithoutChangingOtherPermissions()
    {
        using var store = new TempPairingStore();
        using var key = new PairingTestKey();
        store.Store.Save([
            new PairingRecord(
                "client-a",
                key.PublicKey,
                "Phone",
                PermissionOverrides: new DevicePermissionOverrides(
                    AllowRemoteInput: true,
                    AllowScreenViewing: true))
        ]);
        var manager = new PairingManager(store.Store);

        var changed = WpfTrayApplicationContext.BlockScreenViewingPermission(manager, "client-a");

        Assert.True(changed);
        var overrides = manager.GetDevicePermissionOverrides("client-a");
        Assert.False(overrides.AllowScreenViewing);
        Assert.True(overrides.AllowRemoteInput);
        Assert.False(Assert.Single(store.Store.Load()).PermissionOverrides?.AllowScreenViewing);
    }
}
