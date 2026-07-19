using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

[Collection(AppPermissionSettingsCollection.Name)]
public sealed class WebHostRemoteActionTests : WebHostServiceTestBase
{
    [Fact]
    public async Task WebSocketExecutesRemoteLaunchActionWhenGloballyAllowed()
    {
        var originalPermissions = AppPermissionSettings.Load();
        var remoteActions = new FakeRemoteActionExecutor();

        try
        {
            AppPermissionSettings.Save(originalPermissions with { AllowRemoteAppLaunch = true });
            await using var fixture = await WebHostFixture.StartAsync(remoteActionExecutor: remoteActions);
            var clientId = $"client-{Guid.NewGuid():N}";
            var token = fixture.Manager.CreatePairingToken();
            using var socket = await ConnectAsync(fixture.WebHost);

            var paired = await SendAndReceiveAsync(socket, new
            {
                type = "pair.hello",
                clientId,
                deviceName = "Phone",
                pairToken = token,
                reconnectPublicKey = PairingTestKey.PublicKeyForFreshPairing
            });
            await SendAsync(socket, new { type = "remote.launch", action = "openYoutube" });
            var status = await SendAndReceiveAsync(socket, new { type = "status.get" });

            Assert.Equal("pair.accepted", paired.GetProperty("type").GetString());
            Assert.True(paired.GetProperty("capabilities").GetProperty("remoteLaunch").GetBoolean());
            Assert.Equal("status", status.GetProperty("type").GetString());
            Assert.Equal(new[] { "openYoutube" }, remoteActions.Actions);
        }
        finally
        {
            AppPermissionSettings.Save(originalPermissions);
        }
    }

    [Fact]
    public async Task WebSocketBlocksRemoteLaunchActionWhenGloballyDisabled()
    {
        var originalPermissions = AppPermissionSettings.Load();
        var remoteActions = new FakeRemoteActionExecutor();

        try
        {
            AppPermissionSettings.Save(originalPermissions with { AllowRemoteAppLaunch = false });
            await using var fixture = await WebHostFixture.StartAsync(remoteActionExecutor: remoteActions);
            var clientId = $"client-{Guid.NewGuid():N}";
            var token = fixture.Manager.CreatePairingToken();
            using var socket = await ConnectAsync(fixture.WebHost);

            var paired = await SendAndReceiveAsync(socket, new
            {
                type = "pair.hello",
                clientId,
                deviceName = "Phone",
                pairToken = token,
                reconnectPublicKey = PairingTestKey.PublicKeyForFreshPairing
            });
            await SendAsync(socket, new { type = "remote.launch", action = "openYoutube" });
            var status = await SendAndReceiveAsync(socket, new { type = "status.get" });

            Assert.Equal("pair.accepted", paired.GetProperty("type").GetString());
            Assert.False(paired.GetProperty("capabilities").GetProperty("remoteLaunch").GetBoolean());
            Assert.Equal("status", status.GetProperty("type").GetString());
            Assert.Empty(remoteActions.Actions);
        }
        finally
        {
            AppPermissionSettings.Save(originalPermissions);
        }
    }

    [Fact]
    public async Task WebSocketRejectsUnsupportedRemoteLaunchActions()
    {
        var originalPermissions = AppPermissionSettings.Load();
        var remoteActions = new FakeRemoteActionExecutor();

        try
        {
            AppPermissionSettings.Save(originalPermissions with { AllowRemoteAppLaunch = true });
            await using var fixture = await WebHostFixture.StartAsync(remoteActionExecutor: remoteActions);
            var clientId = $"client-{Guid.NewGuid():N}";
            var token = fixture.Manager.CreatePairingToken();
            using var socket = await ConnectAsync(fixture.WebHost);

            var paired = await SendAndReceiveAsync(socket, new
            {
                type = "pair.hello",
                clientId,
                deviceName = "Phone",
                pairToken = token,
                reconnectPublicKey = PairingTestKey.PublicKeyForFreshPairing
            });
            await SendAsync(socket, new { type = "remote.launch", action = "cmd.exe" });
            var closeStatus = await ReceiveCloseStatusAsync(socket);

            Assert.Equal("pair.accepted", paired.GetProperty("type").GetString());
            Assert.Equal(WebSocketCloseStatus.PolicyViolation, closeStatus);
            Assert.Empty(remoteActions.Actions);
        }
        finally
        {
            AppPermissionSettings.Save(originalPermissions);
        }
    }
}
