using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

[Collection(AppPermissionSettingsCollection.Name)]
public sealed class WebHostPresentationTests : WebHostServiceTestBase
{
    [Fact]
    public async Task PresentationIsNotAdvertisedOrExecutableWhileAlphaFeaturesAreDisabled()
    {
        AppDeveloperSettings.SetEnableAlphaFeatures(false);
        await using var fixture = await WebHostFixture.StartAsync();
        var clientId = $"client-{Guid.NewGuid():N}";
        using var socket = await ConnectAsync(fixture.WebHost);
        var paired = await PairAsync(socket, fixture, clientId);

        var result = await SendAndReceiveAsync(socket, new
        {
            type = "presentation.command",
            operationId = "presentation-disabled",
            target = "powerpoint",
            action = "next"
        });

        Assert.False(AppDeveloperSettings.EnableAlphaFeatures());
        Assert.False(paired.GetProperty("capabilities").TryGetProperty("presentation", out _));
        Assert.False(result.GetProperty("succeeded").GetBoolean());
        Assert.Equal("feature-disabled", result.GetProperty("code").GetString());
        Assert.Empty(fixture.InputInjector.Events);
        Assert.Equal(WebSocketState.Open, socket.State);
    }

    [Fact]
    public async Task AlphaSettingBroadcastsPresentationAvailabilityChanges()
    {
        AppDeveloperSettings.SetEnableAlphaFeatures(false);
        await using var fixture = await WebHostFixture.StartAsync();
        var clientId = $"client-{Guid.NewGuid():N}";
        using var socket = await ConnectAsync(fixture.WebHost);
        var paired = await PairAsync(socket, fixture, clientId);
        Assert.False(paired.GetProperty("capabilities").TryGetProperty("presentation", out _));

        AppDeveloperSettings.SetEnableAlphaFeatures(true);
        using var enabledTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        using var enabledStatus = JsonDocument.Parse(await ReceiveTextAsync(socket, enabledTimeout.Token));
        Assert.True(enabledStatus.RootElement.GetProperty("capabilities").GetProperty("presentation").GetProperty("canControl").GetBoolean());

        AppDeveloperSettings.SetEnableAlphaFeatures(false);
        using var disabledTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        using var disabledStatus = JsonDocument.Parse(await ReceiveTextAsync(socket, disabledTimeout.Token));
        Assert.False(disabledStatus.RootElement.GetProperty("capabilities").TryGetProperty("presentation", out _));
    }

    [Fact]
    public async Task PresentationLaserUsesHostOwnedStateAndReturnsItsMatchingResult()
    {
        AppDeveloperSettings.SetEnableAlphaFeatures(true);
        var originalPermissions = AppPermissionSettings.Load();
        try
        {
            AppPermissionSettings.Save(originalPermissions with { AllowPresentationControl = true });
            await using var fixture = await WebHostFixture.StartAsync();
            var clientId = $"client-{Guid.NewGuid():N}";
            using var socket = await ConnectAsync(fixture.WebHost);
            var paired = await PairAsync(socket, fixture, clientId);

            var result = await SendAndReceiveAsync(socket, new
            {
                type = "presentation.command",
                operationId = "presentation-1",
                target = "powerpoint",
                action = "pointer",
                enabled = true
            });

            Assert.True(paired.GetProperty("capabilities").GetProperty("presentation").GetProperty("canControl").GetBoolean());
            Assert.Equal("presentation.command.result", result.GetProperty("type").GetString());
            Assert.Equal("presentation-1", result.GetProperty("operationId").GetString());
            Assert.True(result.GetProperty("succeeded").GetBoolean());
            Assert.True(result.GetProperty("laserPointerActive").GetBoolean());
            Assert.Empty(fixture.InputInjector.Events);
        }
        finally
        {
            AppPermissionSettings.Save(originalPermissions);
        }
    }

    [Fact]
    public async Task PresentationPermissionDenialReturnsFeedbackWithoutInjectingOrClosing()
    {
        AppDeveloperSettings.SetEnableAlphaFeatures(true);
        var originalPermissions = AppPermissionSettings.Load();
        try
        {
            AppPermissionSettings.Save(originalPermissions with { AllowPresentationControl = false });
            await using var fixture = await WebHostFixture.StartAsync();
            var clientId = $"client-{Guid.NewGuid():N}";
            using var socket = await ConnectAsync(fixture.WebHost);
            var paired = await PairAsync(socket, fixture, clientId);

            var denied = await SendAndReceiveAsync(socket, new
            {
                type = "presentation.command",
                operationId = "presentation-2",
                target = "powerpoint",
                action = "next"
            });

            Assert.False(paired.GetProperty("capabilities").GetProperty("presentation").GetProperty("canControl").GetBoolean());
            Assert.False(denied.GetProperty("succeeded").GetBoolean());
            Assert.Equal("permission-denied", denied.GetProperty("code").GetString());
            Assert.Empty(fixture.InputInjector.Events);
            Assert.Equal(WebSocketState.Open, socket.State);
        }
        finally
        {
            AppPermissionSettings.Save(originalPermissions);
        }
    }

    [Fact]
    public async Task PresentationNativeFailureReturnsFeedbackAndNextTapStillWorks()
    {
        AppDeveloperSettings.SetEnableAlphaFeatures(true);
        var originalPermissions = AppPermissionSettings.Load();
        try
        {
            AppPermissionSettings.Save(originalPermissions with { AllowPresentationControl = true });
            await using var fixture = await WebHostFixture.StartAsync();
            fixture.InputInjector.Failures.Enqueue(new InvalidOperationException("Configured native failure."));
            var clientId = $"client-{Guid.NewGuid():N}";
            using var socket = await ConnectAsync(fixture.WebHost);
            _ = await PairAsync(socket, fixture, clientId);

            var failed = await SendAndReceiveAsync(socket, new
            {
                type = "presentation.command",
                operationId = "presentation-3",
                target = "google-slides",
                action = "next"
            });
            var recovered = await SendAndReceiveAsync(socket, new
            {
                type = "presentation.command",
                operationId = "presentation-4",
                target = "google-slides",
                action = "previous"
            });

            Assert.False(failed.GetProperty("succeeded").GetBoolean());
            Assert.Equal("input-failed", failed.GetProperty("code").GetString());
            Assert.True(recovered.GetProperty("succeeded").GetBoolean());
            Assert.Equal(new[] { "SpecialKey:ArrowLeft:" }, fixture.InputInjector.Events);
            Assert.Equal(WebSocketState.Open, socket.State);
        }
        finally
        {
            AppPermissionSettings.Save(originalPermissions);
        }
    }

    [Fact]
    public async Task LaserPointerIsAvailableForPdfAndCanBeExplicitlyDisabled()
    {
        AppDeveloperSettings.SetEnableAlphaFeatures(true);
        var originalPermissions = AppPermissionSettings.Load();
        try
        {
            AppPermissionSettings.Save(originalPermissions with { AllowPresentationControl = true });
            await using var fixture = await WebHostFixture.StartAsync();
            var clientId = $"client-{Guid.NewGuid():N}";
            using var socket = await ConnectAsync(fixture.WebHost);
            _ = await PairAsync(socket, fixture, clientId);

            var enabled = await SendAndReceiveAsync(socket, new
            {
                type = "presentation.command",
                operationId = "presentation-5",
                target = "pdf",
                action = "pointer",
                enabled = true
            });
            var disabled = await SendAndReceiveAsync(socket, new
            {
                type = "presentation.command",
                operationId = "presentation-6",
                target = "pdf",
                action = "pointer",
                enabled = false
            });

            Assert.True(enabled.GetProperty("succeeded").GetBoolean());
            Assert.True(enabled.GetProperty("laserPointerActive").GetBoolean());
            Assert.True(disabled.GetProperty("succeeded").GetBoolean());
            Assert.False(disabled.GetProperty("laserPointerActive").GetBoolean());
            Assert.Empty(fixture.InputInjector.Events);
        }
        finally
        {
            AppPermissionSettings.Save(originalPermissions);
        }
    }

    [Fact]
    public async Task RevokingPermissionRestoresLaserAndCleanupRemainsAllowed()
    {
        AppDeveloperSettings.SetEnableAlphaFeatures(true);
        var originalPermissions = AppPermissionSettings.Load();
        try
        {
            AppPermissionSettings.Save(originalPermissions with { AllowPresentationControl = true });
            await using var fixture = await WebHostFixture.StartAsync();
            var clientId = $"client-{Guid.NewGuid():N}";
            using var socket = await ConnectAsync(fixture.WebHost);
            _ = await PairAsync(socket, fixture, clientId);
            _ = await SendAndReceiveAsync(socket, new
            {
                type = "presentation.command",
                operationId = "presentation-enable-before-revoke",
                target = "powerpoint",
                action = "pointer",
                enabled = true
            });

            AppPermissionSettings.Save(originalPermissions with { AllowPresentationControl = false });
            using var statusTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            JsonDocument? revokedStatus = null;
            for (var attempt = 0; attempt < 3 && revokedStatus is null; attempt++)
            {
                var candidate = JsonDocument.Parse(await ReceiveTextAsync(socket, statusTimeout.Token));
                var presentation = candidate.RootElement.GetProperty("capabilities").GetProperty("presentation");
                if (!presentation.GetProperty("canControl").GetBoolean())
                {
                    revokedStatus = candidate;
                }
                else
                {
                    candidate.Dispose();
                }
            }

            using (revokedStatus)
            {
                Assert.NotNull(revokedStatus);
                Assert.False(revokedStatus.RootElement.GetProperty("capabilities").GetProperty("presentation").GetProperty("laserPointerActive").GetBoolean());
            }

            var cleanup = await SendAndReceiveAsync(socket, new
            {
                type = "presentation.command",
                operationId = "presentation-cleanup-after-revoke",
                target = "powerpoint",
                action = "pointer",
                enabled = false
            });

            Assert.True(cleanup.GetProperty("succeeded").GetBoolean());
            Assert.False(cleanup.GetProperty("laserPointerActive").GetBoolean());
        }
        finally
        {
            AppPermissionSettings.Save(originalPermissions);
        }
    }

    private static async Task<JsonElement> PairAsync(WebSocket socket, WebHostFixture fixture, string clientId)
    {
        var token = fixture.Manager.CreatePairingToken();
        return await SendAndReceiveAsync(socket, new
        {
            type = "pair.hello",
            clientId,
            deviceName = "Presenter phone",
            pairToken = token,
            reconnectPublicKey = PairingTestKey.PublicKeyForFreshPairing
        });
    }
}
