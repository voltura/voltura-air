using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

[Collection(AppPermissionSettingsCollection.Name)]
public sealed class WebHostCustomScreenTests : WebHostServiceTestBase
{
    [Fact]
    public async Task GraduatedCustomScreenIsAdvertisedFetchedAndInvoked()
    {
        await using var fixture = await WebHostFixture.StartAsync();
        var clientId = $"client-{Guid.NewGuid():N}";
        var service = fixture.WebHost.CustomScreenService;
        Assert.True(
            service.TrySave(CustomScreenService.CreateDraft(), out var saved, out var saveError),
            saveError);
        Assert.True(service.TryAssign(saved.Id, [clientId], out var assignError), assignError);
        var assigned = Assert.IsType<CustomScreenDefinition>(service.Find(saved.Id));
        var button = assigned.Sections.SelectMany(section => section.Buttons).First();

        using var socket = await ConnectAsync(fixture.WebHost);
        var paired = await SendAndReceiveAsync(socket, new
        {
            type = "pair.hello",
            clientId,
            deviceName = "Custom screen phone",
            pairToken = fixture.Manager.CreatePairingToken(),
            reconnectPublicKey = PairingTestKey.PublicKeyForFreshPairing
        });

        var capability = paired.GetProperty("capabilities").GetProperty("customScreens");
        Assert.Equal(assigned.Id, Assert.Single(capability.GetProperty("screens").EnumerateArray()).GetProperty("id").GetString());

        var fetched = await SendAndReceiveAsync(socket, new
        {
            type = "custom.screen.get",
            operationId = "custom-get-1",
            screenId = assigned.Id
        });
        Assert.True(fetched.GetProperty("succeeded").GetBoolean());
        Assert.Equal(assigned.Id, fetched.GetProperty("screen").GetProperty("id").GetString());

        var invoked = await SendAndReceiveAsync(socket, new
        {
            type = "custom.screen.invoke",
            operationId = "custom-invoke-1",
            screenId = assigned.Id,
            screenRevision = assigned.Revision,
            buttonId = button.Id
        });
        Assert.True(invoked.GetProperty("succeeded").GetBoolean());
        Assert.NotEmpty(fixture.InputInjector.Events);
        Assert.Equal(WebSocketState.Open, socket.State);
    }
}
