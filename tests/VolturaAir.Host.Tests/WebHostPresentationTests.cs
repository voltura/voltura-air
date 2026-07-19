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
    public async Task PresentationTapRunsReviewedShortcutAndReturnsItsMatchingResult()
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
                action = "pointer"
            });

            Assert.True(paired.GetProperty("capabilities").GetProperty("presentation").GetProperty("canControl").GetBoolean());
            Assert.Equal("presentation.command.result", result.GetProperty("type").GetString());
            Assert.Equal("presentation-1", result.GetProperty("operationId").GetString());
            Assert.True(result.GetProperty("succeeded").GetBoolean());
            Assert.Equal(new[] { "SpecialKey:L:Control" }, fixture.InputInjector.Events);
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
    public async Task UnsupportedTargetActionReturnsFeedbackWithoutInjection()
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

            var result = await SendAndReceiveAsync(socket, new
            {
                type = "presentation.command",
                operationId = "presentation-5",
                target = "pdf",
                action = "pointer"
            });

            Assert.False(result.GetProperty("succeeded").GetBoolean());
            Assert.Equal("unsupported-action", result.GetProperty("code").GetString());
            Assert.Empty(fixture.InputInjector.Events);
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
