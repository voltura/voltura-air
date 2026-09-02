using Microsoft.AspNetCore.TestHost;
using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

[Collection(AppPermissionSettingsCollection.Name)]
public sealed class WebHostConnectionTests : WebHostServiceTestBase
{
    [Fact]
    public void AuthenticatedIdleTimeoutAllowsDelayedMobileHealthTimers()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), WebHostService.AuthenticatedInactivityTimeout);
    }

    [Fact]
    public async Task PairDisconnectAcknowledgesDurableRemovalBeforeClosing()
    {
        await using var fixture = await WebHostFixture.StartAsync();
        using var key = new PairingTestKey();
        using var socket = await ConnectAsync(fixture.WebHost);
        var clientId = $"client-{Guid.NewGuid():N}";

        var accepted = await SendAndReceiveAsync(socket, new
        {
            type = "pair.hello",
            clientId,
            deviceName = "Phone",
            pairToken = fixture.Manager.CreatePairingToken(),
            reconnectPublicKey = key.PublicKey
        });
        Assert.Equal("pair.accepted", accepted.GetProperty("type").GetString());

        var acknowledgement = await SendAndReceiveAsync(socket, new { type = "pair.disconnect" });

        ProtocolFrameAssert.Conforms(acknowledgement);
        Assert.Equal("pair.disconnect.accepted", acknowledgement.GetProperty("type").GetString());
        Assert.Equal(WebSocketCloseStatus.NormalClosure, await ReceiveCloseStatusAsync(socket));
        Assert.Empty(fixture.Manager.GetDevices());
        await WaitForConnectionCleanupAsync(fixture.WebHost);
        Assert.Equal(0, fixture.WebHost.ActiveSocketCount);
        Assert.Equal(0, fixture.WebHost.SendGateCount);
    }

    [Fact]
    public async Task RepeatedAuthenticatedConnectionsReleaseSocketsAndSendGates()
    {
        await using var fixture = await WebHostFixture.StartAsync();
        using var key = new PairingTestKey();
        var clientId = $"client-{Guid.NewGuid():N}";
        var token = fixture.Manager.CreatePairingToken();
        var pairedOnce = false;

        for (var iteration = 0; iteration < 20; iteration += 1)
        {
            var accepted = !pairedOnce
                ? await SendHelloAsync(fixture.WebHost, new { type = "pair.hello", clientId, deviceName = "Phone", pairToken = token, reconnectPublicKey = key.PublicKey })
                : await SendReconnectAsync(fixture.WebHost, clientId, "Phone", key);
            pairedOnce = true;

            Assert.Equal("pair.accepted", accepted.GetProperty("type").GetString());
            await WaitForConnectionCleanupAsync(fixture.WebHost);
        }

        Assert.Equal(0, fixture.WebHost.ActiveSocketCount);
        Assert.Equal(0, fixture.WebHost.SendGateCount);
    }

    [Fact]
    public async Task WebSocketAcceptsPairingTokenAndChallengeReconnect()
    {
        using var store = new TempPairingStore();
        using var inputInjector = new FakeInputInjector();
        using var key = new PairingTestKey();
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

            var paired = await SendHelloAsync(webHost, new
            {
                type = "pair.hello",
                clientId,
                deviceName = "Phone",
                pairToken = manager.CreatePairingToken(),
                reconnectPublicKey = key.PublicKey
            });
            var reconnected = await SendReconnectAsync(webHost, clientId, "Phone", key);

            Assert.Equal("pair.accepted", paired.GetProperty("type").GetString());
            Assert.False(paired.TryGetProperty("secret", out _));
            Assert.Equal("pair.accepted", reconnected.GetProperty("type").GetString());
            Assert.Equal(clientId, reconnected.GetProperty("clientId").GetString());
            Assert.False(reconnected.TryGetProperty("secret", out _));
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
            using var key = new PairingTestKey();

            var paired = await SendHelloAsync(fixture.WebHost, new
            {
                type = "pair.hello",
                clientId = $"client-{Guid.NewGuid():N}",
                deviceName = "Phone",
                pairToken = fixture.Manager.CreatePairingToken(),
                reconnectPublicKey = key.PublicKey
            });

            Assert.False(paired.GetProperty("capabilities").GetProperty("gestureDebug").GetBoolean());
        }
        finally
        {
            AppDeveloperSettings.SetEnableGestureDebug(originalGestureDebug);
        }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    public async Task WebSocketAdvertisesEffectiveEnhancedCapabilities(
        bool usesRelay,
        bool directPreferenceEnabled,
        bool expectedEnabled)
    {
        AppNetworkSettings.Save(AppNetworkSettings.Load() with
        {
            TransportMode = usesRelay ? ConnectionTransportMode.Relay : ConnectionTransportMode.DirectLan,
            EnhancedCapabilitiesEnabled = directPreferenceEnabled
        });
        await using var fixture = await WebHostFixture.StartAsync();
        using var key = new PairingTestKey();

        var paired = await SendHelloAsync(fixture.WebHost, new
        {
            type = "pair.hello",
            clientId = $"client-{Guid.NewGuid():N}",
            deviceName = "Phone",
            pairToken = fixture.Manager.CreatePairingToken(),
            reconnectPublicKey = key.PublicKey
        });

        Assert.Equal(
            expectedEnabled,
            paired.GetProperty("capabilities").GetProperty("enhancedCapabilities").GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public async Task WebSocketAdvertisesDeveloperModeWhenEnabled()
    {
        var originalDeveloperMode = AppDeveloperSettings.DeveloperMode();

        try
        {
            AppDeveloperSettings.SetDeveloperMode(true);
            await using var fixture = await WebHostFixture.StartAsync();
            using var key = new PairingTestKey();

            var paired = await SendHelloAsync(fixture.WebHost, new
            {
                type = "pair.hello",
                clientId = $"client-{Guid.NewGuid():N}",
                deviceName = "Phone",
                pairToken = fixture.Manager.CreatePairingToken(),
                reconnectPublicKey = key.PublicKey
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
    public async Task WebSocketUsesLightweightHealthAndExplicitStatusAudioRequests()
    {
        var audio = new FakeAudioController(new AudioState(38, false));
        await using var fixture = await WebHostFixture.StartAsync(audio);
        using var key = new PairingTestKey();
        var clientId = $"client-{Guid.NewGuid():N}";
        using var socket = await ConnectAsync(fixture.WebHost);

        var paired = await SendAndReceiveAsync(socket, new
        {
            type = "pair.hello",
            clientId,
            deviceName = "Phone",
            pairToken = fixture.Manager.CreatePairingToken(),
            reconnectPublicKey = key.PublicKey
        });
        var health = await SendAndReceiveAsync(socket, new { type = "health.ping" });
        var status = await SendAndReceiveAsync(socket, new { type = "status.get" });
        var audioState = await SendAndReceiveAsync(socket, new { type = "audio.get" });

        Assert.Equal("pair.accepted", paired.GetProperty("type").GetString());
        Assert.Equal("health.pong", health.GetProperty("type").GetString());
        Assert.Equal("status", status.GetProperty("type").GetString());
        Assert.True(status.GetProperty("connected").GetBoolean());
        Assert.Equal(AppPointerSettings.GetDefaultPointerSpeed(), status.GetProperty("host").GetProperty("pointerSpeed").GetInt32());
        Assert.True(status.GetProperty("host").GetProperty("showModeButtons").GetBoolean());
        Assert.Equal("audio.state", audioState.GetProperty("type").GetString());
        Assert.Equal(38, audioState.GetProperty("volume").GetInt32());
        Assert.False(audioState.GetProperty("muted").GetBoolean());
        Assert.Equal(1, audio.GetStateCalls);
    }

    [Fact]
    public async Task WebSocketBroadcastsPointerSpeedProfileChangesWithoutClientPolling()
    {
        await using var fixture = await WebHostFixture.StartAsync();
        using var key = new PairingTestKey();
        var clientId = $"client-{Guid.NewGuid():N}";
        using var socket = await ConnectAsync(fixture.WebHost);

        var paired = await SendAndReceiveAsync(socket, new
        {
            type = "pair.hello",
            clientId,
            deviceName = "Phone",
            pairToken = fixture.Manager.CreatePairingToken(),
            reconnectPublicKey = key.PublicKey
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
    public async Task WebSocketAdvertisesAndUpdatesEffectiveScreenSoundQualityWithoutAHealthChange()
    {
        AppScreenViewSettings.SaveSoundQuality(ScreenViewSoundQuality.Standard);
        await using var fixture = await WebHostFixture.StartAsync();
        using var key = new PairingTestKey();
        var clientId = $"client-{Guid.NewGuid():N}";
        using var socket = await ConnectAsync(fixture.WebHost);

        var paired = await SendAndReceiveAsync(socket, new
        {
            type = "pair.hello",
            clientId,
            deviceName = "Phone",
            pairToken = fixture.Manager.CreatePairingToken(),
            reconnectPublicKey = key.PublicKey
        });
        JsonElement initialHost = paired.GetProperty("host");
        Assert.Equal("standard", initialHost.GetProperty("screenSoundQuality").GetString());
        Assert.False(initialHost.GetProperty("screenSoundQualityOverridden").GetBoolean());
        JsonElement systemAudio = paired.GetProperty("capabilities").GetProperty("screenView").GetProperty("systemAudio");
        Assert.Equal(["channels", "codec", "sampleRate"], systemAudio.EnumerateObject().Select(property => property.Name).Order());

        await SendAsync(socket, new
        {
            type = "screen.view.sound-quality.set",
            soundQuality = "low"
        });
        using var lowStatus = JsonDocument.Parse(await ReceiveTextAsync(socket));
        JsonElement lowHost = lowStatus.RootElement.GetProperty("host");
        Assert.Equal("low", lowHost.GetProperty("screenSoundQuality").GetString());
        Assert.True(lowHost.GetProperty("screenSoundQualityOverridden").GetBoolean());

        using var clearMessage = JsonDocument.Parse(
            """{ "type": "screen.view.sound-quality.set", "soundQuality": null }""");
        await SendAsync(socket, clearMessage.RootElement);
        using var inheritedStatus = JsonDocument.Parse(await ReceiveTextAsync(socket));
        JsonElement inheritedHost = inheritedStatus.RootElement.GetProperty("host");
        Assert.Equal("standard", inheritedHost.GetProperty("screenSoundQuality").GetString());
        Assert.False(inheritedHost.GetProperty("screenSoundQualityOverridden").GetBoolean());

        var health = await SendAndReceiveAsync(socket, new { type = "health.ping" });
        Assert.Equal("health.pong", health.GetProperty("type").GetString());
    }

    [Fact]
    public async Task WebSocketRejectsReconnectProofReplayedAgainstAnotherSession()
    {
        using var store = new TempPairingStore();
        using var key = new PairingTestKey();
        var manager = new PairingManager(store.Store);
        WebHostService? webHost = null;

        try
        {
            webHost = new WebHostService(
                manager,
                new InputDispatcher(new FakeInputInjector()),
                isolatedTestMode: true,
                configureWebHost: builder => builder.UseTestServer());
            await webHost.StartAsync();
            var clientId = $"client-{Guid.NewGuid():N}";
            var paired = await SendHelloAsync(webHost, new
            {
                type = "pair.hello",
                clientId,
                deviceName = "Phone",
                pairToken = manager.CreatePairingToken(),
                reconnectPublicKey = key.PublicKey
            });

            using var acceptedSocket = await ConnectAsync(webHost);
            var challenge = await SendAndReceiveAsync(acceptedSocket, new { type = "pair.hello", clientId, deviceName = "Phone" });
            var signature = key.SignReconnectChallenge(clientId, challenge.GetProperty("challenge").GetString()!);
            var firstProof = await SendAndReceiveAsync(acceptedSocket, new { type = "pair.proof", clientId, signature });
            using var replayedSocket = await ConnectAsync(webHost);
            _ = await SendAndReceiveAsync(replayedSocket, new { type = "pair.hello", clientId, deviceName = "Phone" });
            var replayedProof = await SendAndReceiveAsync(replayedSocket, new { type = "pair.proof", clientId, signature });

            Assert.Equal("pair.accepted", paired.GetProperty("type").GetString());
            Assert.Equal("pair.accepted", firstProof.GetProperty("type").GetString());
            Assert.Equal("pair.rejected", replayedProof.GetProperty("type").GetString());
            Assert.Equal("invalid-proof", replayedProof.GetProperty("reason").GetString());
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
    public async Task WebSocketBroadcastsElevatedInputStateWhenForegroundChanges()
    {
        await using var fixture = await WebHostFixture.StartAsync();
        using var key = new PairingTestKey();
        var clientId = $"client-{Guid.NewGuid():N}";
        using var socket = await ConnectAsync(fixture.WebHost);

        _ = await SendAndReceiveAsync(socket, new
        {
            type = "pair.hello",
            clientId,
            deviceName = "Phone",
            pairToken = fixture.Manager.CreatePairingToken(),
            reconnectPublicKey = key.PublicKey
        });

        fixture.WebHost.SetInputBlockedByElevation(true);
        using var blockedTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var blockedStatusText = await ReceiveTextAsync(socket, blockedTimeout.Token);
        using var blockedStatus = JsonDocument.Parse(blockedStatusText);

        Assert.Equal("status", blockedStatus.RootElement.GetProperty("type").GetString());
        Assert.True(blockedStatus.RootElement.GetProperty("host").GetProperty("inputBlockedByElevation").GetBoolean());
    }

    [Fact]
    public async Task StatusBroadcasterDoesNotDependOnConstructionSynchronizationContext()
    {
        using var store = new TempPairingStore();
        using var inputInjector = new FakeInputInjector();
        using var key = new PairingTestKey();
        var manager = new PairingManager(store.Store);
        WebHostService? webHost = null;
        var previousContext = SynchronizationContext.Current;

        try
        {
            SynchronizationContext.SetSynchronizationContext(new NonPumpingSynchronizationContext());
            webHost = new WebHostService(
                manager,
                new InputDispatcher(inputInjector),
                isolatedTestMode: true,
                configureWebHost: builder => builder.UseTestServer());
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }

        try
        {
            await webHost.StartAsync();
            var clientId = $"client-{Guid.NewGuid():N}";
            using var socket = await ConnectAsync(webHost);
            _ = await SendAndReceiveAsync(socket, new
            {
                type = "pair.hello",
                clientId,
                deviceName = "Phone",
                pairToken = manager.CreatePairingToken(),
                reconnectPublicKey = key.PublicKey
            });

            webHost.SetInputBlockedByElevation(true);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var status = JsonDocument.Parse(await ReceiveTextAsync(socket, timeout.Token));

            Assert.Equal("status", status.RootElement.GetProperty("type").GetString());
            Assert.True(status.RootElement.GetProperty("host").GetProperty("inputBlockedByElevation").GetBoolean());
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
    public async Task WebSocketStoresClientPointerSpeedAsDeviceOverride()
    {
        await using var fixture = await WebHostFixture.StartAsync();
        using var key = new PairingTestKey();
        var clientId = $"client-{Guid.NewGuid():N}";
        using var socket = await ConnectAsync(fixture.WebHost);

        var paired = await SendAndReceiveAsync(socket, new
        {
            type = "pair.hello",
            clientId,
            deviceName = "Phone",
            pairToken = fixture.Manager.CreatePairingToken(),
            reconnectPublicKey = key.PublicKey
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
    public async Task WebSocketStoresClientModeButtonVisibilityAsDeviceOverrideAndBroadcastsIt()
    {
        await using var fixture = await WebHostFixture.StartAsync();
        using var key = new PairingTestKey();
        var clientId = $"client-{Guid.NewGuid():N}";
        using var socket = await ConnectAsync(fixture.WebHost);

        var paired = await SendAndReceiveAsync(socket, new
        {
            type = "pair.hello",
            clientId,
            deviceName = "Phone",
            pairToken = fixture.Manager.CreatePairingToken(),
            reconnectPublicKey = key.PublicKey
        });
        await SendAsync(socket, new { type = "appearance.mode-buttons.set", showModeButtons = false });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var pushedStatusText = await ReceiveTextAsync(socket, timeout.Token);
        using var pushedStatus = JsonDocument.Parse(pushedStatusText);

        Assert.Equal("pair.accepted", paired.GetProperty("type").GetString());
        Assert.False(fixture.Manager.GetDeviceShowModeButtons(clientId));
        Assert.False(Assert.Single(fixture.Manager.GetDevices()).ShowModeButtonsOverride);
        Assert.Equal("status", pushedStatus.RootElement.GetProperty("type").GetString());
        Assert.False(pushedStatus.RootElement.GetProperty("host").GetProperty("showModeButtons").GetBoolean());
    }

    [Fact]
    public async Task WebSocketStoresClientControlDepthAsDeviceOverrideAndBroadcastsIt()
    {
        await using var fixture = await WebHostFixture.StartAsync();
        using var key = new PairingTestKey();
        var clientId = $"client-{Guid.NewGuid():N}";
        using var socket = await ConnectAsync(fixture.WebHost);

        var paired = await SendAndReceiveAsync(socket, new
        {
            type = "pair.hello",
            clientId,
            deviceName = "Phone",
            pairToken = fixture.Manager.CreatePairingToken(),
            reconnectPublicKey = key.PublicKey
        });
        Assert.True(paired.GetProperty("host").GetProperty("controlDepth").GetBoolean());

        await SendAsync(socket, new { type = "appearance.control-depth.set", controlDepth = false });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var pushedStatusText = await ReceiveTextAsync(socket, timeout.Token);
        using var pushedStatus = JsonDocument.Parse(pushedStatusText);

        Assert.False(fixture.Manager.GetDeviceControlDepth(clientId));
        Assert.False(Assert.Single(fixture.Manager.GetDevices()).ControlDepthOverride);
        Assert.Equal("status", pushedStatus.RootElement.GetProperty("type").GetString());
        Assert.False(pushedStatus.RootElement.GetProperty("host").GetProperty("controlDepth").GetBoolean());
    }

    [Fact]
    public async Task WebSocketStoresClientAccentColorAsDeviceOverrideAndBroadcastsIt()
    {
        await using var fixture = await WebHostFixture.StartAsync();
        using var key = new PairingTestKey();
        var clientId = $"client-{Guid.NewGuid():N}";
        using var socket = await ConnectAsync(fixture.WebHost);

        var paired = await SendAndReceiveAsync(socket, new
        {
            type = "pair.hello",
            clientId,
            deviceName = "Phone",
            pairToken = fixture.Manager.CreatePairingToken(),
            reconnectPublicKey = key.PublicKey
        });
        Assert.Equal(JsonValueKind.Null, paired.GetProperty("host").GetProperty("accentColor").ValueKind);
        Assert.False(paired.GetProperty("host").GetProperty("accentColorOverridden").GetBoolean());

        await SendAsync(socket, new { type = "appearance.accent-color.set", accentColor = "#5FC8B4" });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var pushedStatusText = await ReceiveTextAsync(socket, timeout.Token);
        using var pushedStatus = JsonDocument.Parse(pushedStatusText);

        Assert.Equal("#5FC8B4", fixture.Manager.GetDeviceAccentColor(clientId));
        Assert.True(fixture.Manager.GetDeviceAccentColorOverridden(clientId));
        var host = pushedStatus.RootElement.GetProperty("host");
        Assert.Equal("#5FC8B4", host.GetProperty("accentColor").GetString());
        Assert.True(host.GetProperty("accentColorOverridden").GetBoolean());
    }

    [Fact]
    public async Task WebSocketBroadcastsGlobalModeButtonVisibilityChanges()
    {
        await using var fixture = await WebHostFixture.StartAsync();
        using var key = new PairingTestKey();
        var clientId = $"client-{Guid.NewGuid():N}";
        using var socket = await ConnectAsync(fixture.WebHost);

        _ = await SendAndReceiveAsync(socket, new
        {
            type = "pair.hello",
            clientId,
            deviceName = "Phone",
            pairToken = fixture.Manager.CreatePairingToken(),
            reconnectPublicKey = key.PublicKey
        });
        try
        {
            AppAppearanceSettings.SetShowModeButtons(false);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            var pushedStatusText = await ReceiveTextAsync(socket, timeout.Token);
            using var pushedStatus = JsonDocument.Parse(pushedStatusText);

            Assert.Equal("status", pushedStatus.RootElement.GetProperty("type").GetString());
            Assert.False(pushedStatus.RootElement.GetProperty("host").GetProperty("showModeButtons").GetBoolean());
        }
        finally
        {
            AppAppearanceSettings.SetShowModeButtons(true);
        }
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

file sealed class NonPumpingSynchronizationContext : SynchronizationContext
{
    public override void Post(SendOrPostCallback callback, object? state)
    {
    }
}
