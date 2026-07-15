using Microsoft.AspNetCore.TestHost;
using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

[Collection(AppPermissionSettingsCollection.Name)]
public sealed class WebHostConnectionTests : WebHostServiceTestBase
{
    [Fact]
    public async Task RepeatedAuthenticatedConnectionsReleaseSocketsAndSendGates()
    {
        await using var fixture = await WebHostFixture.StartAsync();
        var clientId = $"client-{Guid.NewGuid():N}";
        var token = fixture.Manager.CreatePairingToken();
        string? secret = null;

        for (var iteration = 0; iteration < 20; iteration += 1)
        {
            using var socket = await ConnectAsync(fixture.WebHost);
            var accepted = secret is null
                ? await SendAndReceiveAsync(socket, new { type = "pair.hello", clientId, deviceName = "Phone", pairToken = token })
                : await SendAndReceiveAsync(socket, new { type = "pair.hello", clientId, deviceName = "Phone", secret });
            secret ??= accepted.GetProperty("secret").GetString();

            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
            await WaitForConnectionCleanupAsync(fixture.WebHost);
        }

        Assert.False(string.IsNullOrWhiteSpace(secret));
        Assert.Equal(0, fixture.WebHost.ActiveSocketCount);
        Assert.Equal(0, fixture.WebHost.SendGateCount);
    }

    [Fact]
    public async Task WebSocketAcceptsPairingTokenAndStoredSecretReconnect()
    {
        using var store = new TempPairingStore();
        using var inputInjector = new FakeInputInjector();
        var manager = new PairingManager(store.Store);
        WebHostService? webHost = null;

        try
        {
            webHost = new WebHostService(
                manager,
                new InputDispatcher(inputInjector),
                isolatedTestMode: true,
                configureWebHost: builder => builder.UseTestServer());
            await webHost.StartAsync();
            var clientId = $"client-{Guid.NewGuid():N}";
            var token = manager.CreatePairingToken();

            var paired = await SendHelloAsync(webHost, new
            {
                type = "pair.hello",
                clientId,
                deviceName = "Phone",
                pairToken = token
            });
            var secret = paired.GetProperty("secret").GetString();

            var reconnected = await SendHelloAsync(webHost, new
            {
                type = "pair.hello",
                clientId,
                deviceName = "Phone",
                secret
            });

            Assert.Equal("pair.accepted", paired.GetProperty("type").GetString());
            Assert.False(string.IsNullOrWhiteSpace(secret));
            Assert.Equal("pair.accepted", reconnected.GetProperty("type").GetString());
            Assert.Equal(clientId, reconnected.GetProperty("clientId").GetString());
        }
        finally
        {
            if (webHost is not null)
            {
                await webHost.StopAsync();
                await webHost.DisposeAsync();
            }
        }
    }

    [Fact]
    public async Task WebSocketDoesNotAdvertiseGestureDebugByDefault()
    {
        var originalGestureDebug = AppDeveloperSettings.EnableGestureDebug();

        try
        {
            AppDeveloperSettings.SetEnableGestureDebug(false);
            await using var fixture = await WebHostFixture.StartAsync();
            var token = fixture.Manager.CreatePairingToken();

            var paired = await SendHelloAsync(fixture.WebHost, new
            {
                type = "pair.hello",
                clientId = $"client-{Guid.NewGuid():N}",
                deviceName = "Phone",
                pairToken = token
            });

            Assert.False(paired.GetProperty("capabilities").GetProperty("gestureDebug").GetBoolean());
        }
        finally
        {
            AppDeveloperSettings.SetEnableGestureDebug(originalGestureDebug);
        }
    }

    [Fact]
    public async Task WebSocketAdvertisesDeveloperModeWhenEnabled()
    {
        var originalDeveloperMode = AppDeveloperSettings.DeveloperMode();

        try
        {
            AppDeveloperSettings.SetDeveloperMode(true);
            await using var fixture = await WebHostFixture.StartAsync();
            var token = fixture.Manager.CreatePairingToken();

            var paired = await SendHelloAsync(fixture.WebHost, new
            {
                type = "pair.hello",
                clientId = $"client-{Guid.NewGuid():N}",
                deviceName = "Phone",
                pairToken = token
            });

            var host = paired.GetProperty("host");
            Assert.True(host.GetProperty("developerMode").GetBoolean());
            Assert.False(string.IsNullOrWhiteSpace(host.GetProperty("developerSessionId").GetString()));
        }
        finally
        {
            AppDeveloperSettings.SetDeveloperMode(originalDeveloperMode);
        }
    }

    [Fact]
    public async Task WebSocketAdvertisesDefaultRemoteMode()
    {
        var originalRemoteMode = AppRemoteSettings.GetDefaultRemoteMode();

        try
        {
            AppRemoteSettings.SetDefaultRemoteMode(AppRemoteMode.Kodi);
            await using var fixture = await WebHostFixture.StartAsync();
            var token = fixture.Manager.CreatePairingToken();

            var paired = await SendHelloAsync(fixture.WebHost, new
            {
                type = "pair.hello",
                clientId = $"client-{Guid.NewGuid():N}",
                deviceName = "Phone",
                pairToken = token
            });

            Assert.Equal("kodi", paired.GetProperty("host").GetProperty("defaultRemoteMode").GetString());
        }
        finally
        {
            AppRemoteSettings.SetDefaultRemoteMode(originalRemoteMode);
        }
    }

    [Fact]
    public async Task WebSocketUsesLightweightHealthAndExplicitStatusAudioRequests()
    {
        var audio = new FakeAudioController(new AudioState(38, false));
        await using var fixture = await WebHostFixture.StartAsync(audio);
        var clientId = $"client-{Guid.NewGuid():N}";
        var token = fixture.Manager.CreatePairingToken();
        using var socket = await ConnectAsync(fixture.WebHost);

        var paired = await SendAndReceiveAsync(socket, new
        {
            type = "pair.hello",
            clientId,
            deviceName = "Phone",
            pairToken = token
        });
        var health = await SendAndReceiveAsync(socket, new { type = "health.ping" });
        var status = await SendAndReceiveAsync(socket, new { type = "status.get" });
        var audioState = await SendAndReceiveAsync(socket, new { type = "audio.get" });
        var expectedPointerSpeed = AppPointerSettings.GetDefaultPointerSpeed();

        Assert.Equal("pair.accepted", paired.GetProperty("type").GetString());
        Assert.Equal("health.pong", health.GetProperty("type").GetString());
        Assert.Equal("status", status.GetProperty("type").GetString());
        Assert.True(status.GetProperty("connected").GetBoolean());
        Assert.Equal(expectedPointerSpeed, status.GetProperty("host").GetProperty("pointerSpeed").GetInt32());
        Assert.Equal("audio.state", audioState.GetProperty("type").GetString());
        Assert.Equal(38, audioState.GetProperty("volume").GetInt32());
        Assert.False(audioState.GetProperty("muted").GetBoolean());
        Assert.Equal(1, audio.GetStateCalls);
    }

    [Fact]
    public async Task WebSocketBroadcastsPointerSpeedProfileChangesWithoutClientPolling()
    {
        await using var fixture = await WebHostFixture.StartAsync();
        var clientId = $"client-{Guid.NewGuid():N}";
        var token = fixture.Manager.CreatePairingToken();
        using var socket = await ConnectAsync(fixture.WebHost);

        var paired = await SendAndReceiveAsync(socket, new
        {
            type = "pair.hello",
            clientId,
            deviceName = "Phone",
            pairToken = token
        });

        fixture.Manager.SetDevicePointerSpeedOverride(clientId, 65);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var pushedStatusText = await ReceiveTextAsync(socket, timeout.Token);
        using var pushedStatus = JsonDocument.Parse(pushedStatusText);

        Assert.Equal("pair.accepted", paired.GetProperty("type").GetString());
        Assert.Equal("status", pushedStatus.RootElement.GetProperty("type").GetString());
        Assert.Equal(65, pushedStatus.RootElement.GetProperty("host").GetProperty("pointerSpeed").GetInt32());
    }

    [Fact]
    public async Task WebSocketBroadcastsElevatedInputStateWhenForegroundChanges()
    {
        await using var fixture = await WebHostFixture.StartAsync();
        var clientId = $"client-{Guid.NewGuid():N}";
        var token = fixture.Manager.CreatePairingToken();
        using var socket = await ConnectAsync(fixture.WebHost);

        _ = await SendAndReceiveAsync(socket, new
        {
            type = "pair.hello",
            clientId,
            deviceName = "Phone",
            pairToken = token
        });

        fixture.WebHost.SetInputBlockedByElevation(true);
        using var blockedTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var blockedStatusText = await ReceiveTextAsync(socket, blockedTimeout.Token);
        using var blockedStatus = JsonDocument.Parse(blockedStatusText);

        Assert.Equal("status", blockedStatus.RootElement.GetProperty("type").GetString());
        Assert.True(blockedStatus.RootElement.GetProperty("host").GetProperty("inputBlockedByElevation").GetBoolean());

        fixture.WebHost.SetInputBlockedByElevation(false);
        using var restoredTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var restoredStatusText = await ReceiveTextAsync(socket, restoredTimeout.Token);
        using var restoredStatus = JsonDocument.Parse(restoredStatusText);

        Assert.False(restoredStatus.RootElement.GetProperty("host").GetProperty("inputBlockedByElevation").GetBoolean());
    }

    [Fact]
    public async Task WebSocketStoresClientPointerSpeedAsDeviceOverride()
    {
        await using var fixture = await WebHostFixture.StartAsync();
        var clientId = $"client-{Guid.NewGuid():N}";
        var token = fixture.Manager.CreatePairingToken();
        using var socket = await ConnectAsync(fixture.WebHost);

        var paired = await SendAndReceiveAsync(socket, new
        {
            type = "pair.hello",
            clientId,
            deviceName = "Phone",
            pairToken = token
        });
        await SendAsync(socket, new { type = "pointer.speed.set", pointerSpeed = 45 });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var pushedStatusText = await ReceiveTextAsync(socket, timeout.Token);
        using var pushedStatus = JsonDocument.Parse(pushedStatusText);

        Assert.Equal("pair.accepted", paired.GetProperty("type").GetString());
        Assert.Equal(45, fixture.Manager.GetDevicePointerSpeed(clientId));
        Assert.Equal(45, Assert.Single(fixture.Manager.GetDevices()).PointerSpeedOverride);
        Assert.Equal("status", pushedStatus.RootElement.GetProperty("type").GetString());
        Assert.Equal(45, pushedStatus.RootElement.GetProperty("host").GetProperty("pointerSpeed").GetInt32());
    }

    private sealed class RecordingAppLog : IAppLog
    {
        public event EventHandler? Changed;

        public List<AppLogEntry> Entries { get; } = [];

        public string LogDirectory => string.Empty;

        public void Write(AppLogEntry entry)
        {
            Entries.Add(entry);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public AppLogReadResult Read(AppLogQuery query) => new(true, []);

        public AppLogDeleteResult DeleteAll() => new(true, 0);
    }

    private static async Task WaitForConnectionCleanupAsync(WebHostService webHost)
    {
        var timeout = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (webHost.ActiveSocketCount != 0 || webHost.SendGateCount != 0)
        {
            Assert.True(DateTime.UtcNow < timeout, "The WebSocket registry did not release the closed connection.");
            await Task.Delay(10);
        }
    }
}
