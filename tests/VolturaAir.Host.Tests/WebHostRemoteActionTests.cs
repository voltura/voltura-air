using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

[Collection(AppPermissionSettingsCollection.Name)]
public sealed class WebHostRemoteActionTests : WebHostServiceTestBase
{
    [Fact]
    public async Task RemoteInputRemainsResponsiveWhileKodiLaunchIsInProgress()
    {
        var originalPermissions = AppPermissionSettings.Load();
        var remoteActions = new BlockingRemoteActionExecutor();

        try
        {
            AppPermissionSettings.Save(originalPermissions with { AllowRemoteAppLaunch = true });
            await using var fixture = await WebHostFixture.StartAsync(remoteActionExecutor: remoteActions);
            var clientId = $"client-{Guid.NewGuid():N}";
            var token = fixture.Manager.CreatePairingToken();
            using var socket = await ConnectAsync(fixture.WebHost);

            await SendAndReceiveAsync(socket, new
            {
                type = "pair.hello",
                clientId,
                deviceName = "Phone",
                pairToken = token,
                reconnectPublicKey = PairingTestKey.PublicKeyForFreshPairing
            });
            await SendAsync(socket, new
            {
                type = "remote.launch",
                action = RemoteLaunchActions.StartOrActivateKodi
            });
            await remoteActions.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));

            await SendAsync(socket, new
            {
                type = "remote.launch",
                action = RemoteLaunchActions.StartOrActivateKodi
            });
            var inputAck = await SendAndReceiveAsync(socket, new
            {
                type = "keyboard.special",
                key = "ArrowDown",
                seq = 41,
                inputContext = "media-controls"
            });

            Assert.Equal("input.ack", inputAck.GetProperty("type").GetString());
            Assert.Equal(41, inputAck.GetProperty("seq").GetInt32());
            Assert.Equal(1, remoteActions.CallCount);
        }
        finally
        {
            remoteActions.Release.TrySetResult(true);
            AppPermissionSettings.Save(originalPermissions);
        }
    }

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
            Assert.True(fixture.Manager.SetDevicePermission(clientId, DevicePermissionKind.RemoteAppLaunch, false));
            using (var permissions = JsonDocument.Parse(await ReceiveTextAsync(socket)))
            {
                Assert.False(permissions.RootElement.GetProperty("capabilities").GetProperty("remoteLaunch").GetBoolean());
            }
            await SendAsync(socket, new { type = "remote.launch", action = "openYoutube" });
            var status = await SendAndReceiveAsync(socket, new { type = "status.get" });

            Assert.Equal("pair.accepted", paired.GetProperty("type").GetString());
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

    private sealed class BlockingRemoteActionExecutor : IRemoteActionExecutor
    {
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<bool> TryExecuteAsync(string action, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            Started.TrySetResult();
            return await Release.Task.WaitAsync(cancellationToken);
        }
    }
}
