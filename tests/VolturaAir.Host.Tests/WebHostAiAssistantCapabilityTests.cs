using System.Text.Json;
using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

[Collection(AppPermissionSettingsCollection.Name)]
public sealed class WebHostAiAssistantCapabilityTests : WebHostServiceTestBase
{
    [Fact]
    public async Task OnlyMyDeviceProfileReceivesAssistantPermission()
    {
        await using var fixture = await WebHostFixture.StartAsync();
        const string clientId = "assistant-phone";
        using var socket = await ConnectAsync(fixture.WebHost);
        JsonElement paired = await SendAndReceiveAsync(socket, new
        {
            type = "pair.hello",
            clientId,
            deviceName = "Private Phone",
            pairToken = fixture.Manager.CreatePairingToken(),
            reconnectPublicKey = PairingTestKey.PublicKeyForFreshPairing
        });

        Assert.True(paired.GetProperty("capabilities").GetProperty("aiAssistant").GetProperty("permissionGranted").GetBoolean());

        Assert.True(fixture.Manager.SetDeviceAccessProfile(clientId, DeviceAccessProfile.RemoteControls));
        using var remoteStatus = JsonDocument.Parse(await ReceiveTextAsync(socket));
        Assert.False(remoteStatus.RootElement.GetProperty("capabilities").GetProperty("aiAssistant").GetProperty("permissionGranted").GetBoolean());
        Assert.False(remoteStatus.RootElement.GetProperty("capabilities").GetProperty("aiAssistant").GetProperty("canUse").GetBoolean());

        Assert.True(fixture.Manager.SetDeviceAccessProfile(clientId, DeviceAccessProfile.Custom));
        using var customStatus = JsonDocument.Parse(await ReceiveTextAsync(socket));
        Assert.False(customStatus.RootElement.GetProperty("capabilities").GetProperty("aiAssistant").GetProperty("permissionGranted").GetBoolean());

        Assert.True(fixture.Manager.SetDeviceAccessProfile(clientId, DeviceAccessProfile.MyDevice));
        using var myDeviceStatus = JsonDocument.Parse(await ReceiveTextAsync(socket));
        Assert.True(myDeviceStatus.RootElement.GetProperty("capabilities").GetProperty("aiAssistant").GetProperty("permissionGranted").GetBoolean());
    }
}
