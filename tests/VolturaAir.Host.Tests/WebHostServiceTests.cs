using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

[Collection(AppPermissionSettingsCollection.Name)]
public sealed class WebHostServiceTests
{
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
                pairToken = token
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
                pairToken = token
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
                pairToken = token
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

    [Fact]
    public async Task WebSocketRejectsMalformedPairHelloAsInvalidMessage()
    {
        await using var fixture = await WebHostFixture.StartAsync();
        var token = fixture.Manager.CreatePairingToken();

        var rejected = await SendHelloAsync(fixture.WebHost, new
        {
            type = "pair.hello",
            deviceName = "Phone",
            pairToken = token
        });

        Assert.Equal("pair.rejected", rejected.GetProperty("type").GetString());
        Assert.Equal("invalid-message", rejected.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task WebSocketRejectsPairHelloWithoutTokenOrSecretAsMissingToken()
    {
        await using var fixture = await WebHostFixture.StartAsync();

        var rejected = await SendHelloAsync(fixture.WebHost, new
        {
            type = "pair.hello",
            clientId = $"client-{Guid.NewGuid():N}",
            deviceName = "Phone"
        });

        Assert.Equal("pair.rejected", rejected.GetProperty("type").GetString());
        Assert.Equal("missing-token", rejected.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task WebSocketRateLimitsRepeatedFailedPairingAttempts()
    {
        await using var fixture = await WebHostFixture.StartAsync();
        using var socket = await ConnectAsync(fixture.WebHost);

        JsonElement response = default;
        for (var attempt = 0; attempt < PairingAttemptRateLimiter.DefaultMaxFailures + 1; attempt++)
        {
            response = await SendAndReceiveAsync(socket, new
            {
                type = "pair.hello",
                clientId = $"client-{Guid.NewGuid():N}",
                deviceName = "Phone",
                pairToken = "wrong-token"
            });
        }

        Assert.Equal("pair.rejected", response.GetProperty("type").GetString());
        Assert.Equal("rate-limited", response.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task WebSocketAllowsValidReconnectWhileFailedPairingAttemptsAreRateLimited()
    {
        await using var fixture = await WebHostFixture.StartAsync();
        var clientId = $"client-{Guid.NewGuid():N}";
        var token = fixture.Manager.CreatePairingToken();
        var paired = await SendHelloAsync(fixture.WebHost, new
        {
            type = "pair.hello",
            clientId,
            deviceName = "Phone",
            pairToken = token
        });
        var secret = paired.GetProperty("secret").GetString();

        using var failedSocket = await ConnectAsync(fixture.WebHost);
        for (var attempt = 0; attempt < PairingAttemptRateLimiter.DefaultMaxFailures; attempt++)
        {
            await SendAndReceiveAsync(failedSocket, new
            {
                type = "pair.hello",
                clientId = $"failed-{Guid.NewGuid():N}",
                deviceName = "Phone",
                pairToken = "wrong-token"
            });
        }

        var reconnected = await SendHelloAsync(fixture.WebHost, new
        {
            type = "pair.hello",
            clientId,
            deviceName = "Phone",
            secret
        });

        Assert.Equal("pair.accepted", paired.GetProperty("type").GetString());
        Assert.Equal("pair.accepted", reconnected.GetProperty("type").GetString());
    }

    [Fact]
    public async Task WebSocketClosesUnknownAuthenticatedMessagesWithoutDispatchingInput()
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

        await SendAsync(socket, new { type = "pointer.teleport", dx = 10, dy = 10 });
        var closeStatus = await ReceiveCloseStatusAsync(socket);

        Assert.Equal("pair.accepted", paired.GetProperty("type").GetString());
        Assert.Equal(WebSocketCloseStatus.PolicyViolation, closeStatus);
        Assert.Empty(fixture.InputInjector.Events);
    }

    [Fact]
    public async Task WebSocketClosesMalformedAuthenticatedMessagesWithoutDispatchingInput()
    {
        await using var fixture = await WebHostFixture.StartAsync();
        var closeEvent = new TaskCompletionSource<ControllerSocketClosedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        var clientId = $"client-{Guid.NewGuid():N}";
        var token = fixture.Manager.CreatePairingToken();
        using var socket = await ConnectAsync(fixture.WebHost);
        fixture.WebHost.ControllerSocketClosed += (_, args) => closeEvent.TrySetResult(args);

        var paired = await SendAndReceiveAsync(socket, new
        {
            type = "pair.hello",
            clientId,
            deviceName = "Phone",
            pairToken = token
        });

        await SendAsync(socket, new { type = "pointer.move", dx = "not-a-number", dy = 10 });
        var closeStatus = await ReceiveCloseStatusAsync(socket);
        var closeNotification = await closeEvent.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.Equal("pair.accepted", paired.GetProperty("type").GetString());
        Assert.Equal(WebSocketCloseStatus.PolicyViolation, closeStatus);
        Assert.Equal(clientId, closeNotification.ClientId);
        Assert.Equal(WebSocketCloseStatus.PolicyViolation, closeNotification.Status);
        Assert.Equal("Invalid message", closeNotification.Reason);
        Assert.Empty(fixture.InputInjector.Events);
    }

    [Fact]
    public async Task WebSocketKeepsAuthenticatedSocketOpenAfterRecoverableInputFailure()
    {
        await using var fixture = await WebHostFixture.StartAsync();
        fixture.InputInjector.Failures.Enqueue(new InputDispatchException(
            "Windows did not accept all input events.",
            "keyboard.special",
            requestedCount: 4,
            acceptedCount: 2,
            win32Error: 5,
            cleanupAttempted: true,
            cleanupSucceeded: true));
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
        var inputError = await SendAndReceiveAsync(socket, new { type = "keyboard.special", key = "Tab", modifiers = new[] { "Alt" }, seq = 7 });
        var inputAck = await SendAndReceiveAsync(socket, new { type = "pointer.move", dx = 3, dy = -2, seq = 8 });

        Assert.Equal("pair.accepted", paired.GetProperty("type").GetString());
        Assert.Equal("input.error", inputError.GetProperty("type").GetString());
        Assert.Equal(7, inputError.GetProperty("seq").GetInt64());
        Assert.Equal("VAIR-INPUT-NATIVE-SEND-FAILED", inputError.GetProperty("code").GetString());
        Assert.Equal("input.ack", inputAck.GetProperty("type").GetString());
        Assert.Equal(8, inputAck.GetProperty("seq").GetInt64());
        Assert.Equal(WebSocketState.Open, socket.State);
        Assert.Equal(new[] { "MoveMouse:3:-2" }, fixture.InputInjector.Events);
    }

    [Fact]
    public void OriginPolicyAllowsNormalLocalAndDevOrigins()
    {
        var originalClientUrl = Environment.GetEnvironmentVariable("VOLTURA_AIR_CLIENT_URL");

        try
        {
            Environment.SetEnvironmentVariable("VOLTURA_AIR_CLIENT_URL", "http://dev.example.test:5173");

            Assert.True(WebHostService.IsAllowedWebSocketOrigin(CreateOriginRequest(null)));
            Assert.True(WebHostService.IsAllowedWebSocketOrigin(CreateOriginRequest("http://192.168.68.51:51395")));
            Assert.True(WebHostService.IsAllowedWebSocketOrigin(CreateOriginRequest("http://192.168.68.20:5173")));
            Assert.True(WebHostService.IsAllowedWebSocketOrigin(CreateOriginRequest("http://localhost:5173")));
            Assert.True(WebHostService.IsAllowedWebSocketOrigin(CreateOriginRequest("http://dev.example.test:5173")));
        }
        finally
        {
            Environment.SetEnvironmentVariable("VOLTURA_AIR_CLIENT_URL", originalClientUrl);
        }
    }

    [Fact]
    public void OriginPolicyRejectsClearlyUnrelatedPublicOrigins()
    {
        Assert.False(WebHostService.IsAllowedWebSocketOrigin(CreateOriginRequest("https://example.com")));
    }

    private static async Task<JsonElement> SendHelloAsync(WebHostService webHost, object payload)
    {
        using var socket = await ConnectAsync(webHost);

        var response = await SendAndReceiveAsync(socket, payload);
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
        return response;
    }

    private static async Task<JsonElement> SendAndReceiveAsync(WebSocket socket, object payload)
    {
        await SendAsync(socket, payload);
        var response = await ReceiveTextAsync(socket);
        using var document = JsonDocument.Parse(response);
        return document.RootElement.Clone();
    }

    private static Task SendAsync(WebSocket socket, object payload)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions.Default);
        return socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }

    private static async Task<string> ReceiveTextAsync(WebSocket socket, CancellationToken cancellationToken = default)
    {
        using var stream = new MemoryStream();
        var buffer = new byte[8192];
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, cancellationToken);
            stream.Write(buffer, 0, result.Count);
        } while (!result.EndOfMessage);

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static async Task<WebSocketCloseStatus?> ReceiveCloseStatusAsync(WebSocket socket)
    {
        var buffer = new byte[8192];
        while (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
        {
            var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return result.CloseStatus;
            }
        }

        return socket.CloseStatus;
    }

    private static Task<WebSocket> ConnectAsync(WebHostService webHost)
    {
        var app = webHost.Application ?? throw new InvalidOperationException("The in-memory web host has not started.");
        return app.GetTestServer().CreateWebSocketClient().ConnectAsync(new Uri("ws://localhost/ws"), CancellationToken.None);
    }

    private static HttpRequest CreateOriginRequest(string? origin)
    {
        var context = new DefaultHttpContext();
        context.Request.Host = new HostString("192.168.68.51", 51395);
        if (origin is not null)
        {
            context.Request.Headers.Origin = origin;
        }

        return context.Request;
    }

    private sealed class WebHostFixture : IAsyncDisposable
    {
        private WebHostFixture(
            TempPairingStore store,
            FakeInputInjector inputInjector,
            PairingManager manager,
            WebHostService webHost)
        {
            Store = store;
            InputInjector = inputInjector;
            Manager = manager;
            WebHost = webHost;
        }

        public TempPairingStore Store { get; }

        public FakeInputInjector InputInjector { get; }

        public PairingManager Manager { get; }

        public WebHostService WebHost { get; }

        public static async Task<WebHostFixture> StartAsync(ISystemAudioController? audioController = null, IRemoteActionExecutor? remoteActionExecutor = null)
        {
            var store = new TempPairingStore();
            var inputInjector = new FakeInputInjector();
            var manager = new PairingManager(store.Store);

            var webHost = new WebHostService(
                manager,
                new InputDispatcher(inputInjector),
                audioController,
                remoteActionExecutor,
                isolatedTestMode: true,
                configureWebHost: builder => builder.UseTestServer());
            await webHost.StartAsync();
            return new WebHostFixture(store, inputInjector, manager, webHost);
        }

        public async ValueTask DisposeAsync()
        {
            await WebHost.StopAsync();
            await WebHost.DisposeAsync();
            Store.Dispose();
            InputInjector.Dispose();
        }
    }

    private sealed class FakeRemoteActionExecutor : IRemoteActionExecutor
    {
        public List<string> Actions { get; } = new();

        public bool TryExecute(string action)
        {
            Actions.Add(action);
            return true;
        }
    }

    private sealed class FakeAudioController : ISystemAudioController
    {
        private AudioState _state;

        public FakeAudioController(AudioState state)
        {
            _state = state;
        }

        public int GetStateCalls { get; private set; }

        public AudioState GetState()
        {
            GetStateCalls++;
            return _state;
        }

        public AudioState ToggleMute()
        {
            _state = _state with { Muted = !_state.Muted };
            return _state;
        }

        public AudioState SetVolume(int volume)
        {
            _state = new AudioState(volume, false);
            return _state;
        }
    }
}
