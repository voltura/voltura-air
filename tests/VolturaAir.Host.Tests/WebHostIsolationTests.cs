using Microsoft.AspNetCore.TestHost;
using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

[Collection(AppPermissionSettingsCollection.Name)]
public sealed class WebHostIsolationTests : IsolatedHostSettingsTest
{
    [Fact]
    public async Task IsolatedTestModeUsesLoopbackWithoutChangingNetworkSettings()
    {
        var settingsBefore = AppNetworkSettings.Load();
        using var store = new TempPairingStore();
        using var inputInjector = new FakeInputInjector();
        var manager = new PairingManager(store.Store);
        await using var webHost = new WebHostService(
            manager,
            new InputDispatcher(inputInjector),
            isolatedTestMode: true);

        Assert.Equal("127.0.0.1", webHost.ListenAddress);
        Assert.Equal("127.0.0.1", webHost.AdvertisedHostAddress);
        Assert.Equal("Loopback (isolated test)", webHost.SelectedAdapterName);
        Assert.Equal(settingsBefore, AppNetworkSettings.Load());
    }

    [Fact]
    public async Task InMemoryTestServerDoesNotReserveOrInspectAHostPort()
    {
        var settingsBefore = AppNetworkSettings.Load();
        using var store = new TempPairingStore();
        using var inputInjector = new FakeInputInjector();
        var manager = new PairingManager(store.Store);
        await using var webHost = new WebHostService(
            manager,
            new InputDispatcher(inputInjector),
            isolatedTestMode: true,
            configureWebHost: builder => builder.UseTestServer());

        Assert.Equal(PortSelector.PreferredPort, webHost.Port);
        Assert.Equal(settingsBefore, AppNetworkSettings.Load());
    }
}
