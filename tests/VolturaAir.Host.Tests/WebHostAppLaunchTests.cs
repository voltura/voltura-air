using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

[Collection(AppPermissionSettingsCollection.Name)]
public sealed class WebHostAppLaunchTests : WebHostServiceTestBase
{
    [Fact]
    public async Task WebSocketAdvertisesAndExecutesApprovedConfiguredAppLaunch()
    {
        var originalPermissions = AppPermissionSettings.Load();
        var appLaunch = new FakeAppLaunchService(
            [new AppLaunchActionSummary("preset.browser", "Browser", "browser")],
            new AppLaunchExecutionResult(true, "started", "Started Browser."));

        try
        {
            AppPermissionSettings.Save(originalPermissions with { AllowRemoteAppLaunch = true });
            await using var fixture = await WebHostFixture.StartAsync(appLaunchService: appLaunch);
            var clientId = $"client-{Guid.NewGuid():N}";
            using var socket = await ConnectAsync(fixture.WebHost);

            var paired = await SendAndReceiveAsync(socket, new
            {
                type = "pair.hello",
                clientId,
                deviceName = "Phone",
                pairToken = fixture.Manager.CreatePairingToken()
            });
            var advertised = Assert.Single(paired.GetProperty("host").GetProperty("appLaunchActions").EnumerateArray());
            var result = await SendAndReceiveAsync(socket, new { type = "app.launch", operationId = "op-app-1", actionId = "preset.browser" });

            Assert.Equal("preset.browser", advertised.GetProperty("id").GetString());
            Assert.Equal("Browser", advertised.GetProperty("label").GetString());
            Assert.Equal("browser", advertised.GetProperty("kind").GetString());
            Assert.Equal(new[] { "preset.browser" }, appLaunch.ActionIds);
            Assert.Equal("app.launch.result", result.GetProperty("type").GetString());
            Assert.Equal("op-app-1", result.GetProperty("operationId").GetString());
            Assert.True(result.GetProperty("succeeded").GetBoolean());
            Assert.Equal("started", result.GetProperty("code").GetString());
        }
        finally
        {
            AppPermissionSettings.Save(originalPermissions);
        }
    }

    [Fact]
    public async Task DeviceOverrideCanBlockConfiguredAppLaunchAllowedGlobally()
    {
        var originalPermissions = AppPermissionSettings.Load();
        var appLaunch = new FakeAppLaunchService(
            [new AppLaunchActionSummary("preset.browser", "Browser", "browser")],
            new AppLaunchExecutionResult(true, "started", "Started Browser."));

        try
        {
            AppPermissionSettings.Save(originalPermissions with { AllowRemoteAppLaunch = true });
            await using var fixture = await WebHostFixture.StartAsync(appLaunchService: appLaunch);
            var clientId = $"client-{Guid.NewGuid():N}";
            using var socket = await ConnectAsync(fixture.WebHost);
            await SendAndReceiveAsync(socket, new
            {
                type = "pair.hello",
                clientId,
                deviceName = "Phone",
                pairToken = fixture.Manager.CreatePairingToken()
            });

            Assert.True(fixture.Manager.SetDevicePermissionOverrides(
                clientId,
                new DevicePermissionOverrides(AllowRemoteAppLaunch: false)));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var pushedStatus = JsonDocument.Parse(await ReceiveTextAsync(socket, timeout.Token));
            var result = await SendAndReceiveAsync(socket, new { type = "app.launch", operationId = "op-app-2", actionId = "preset.browser" });

            Assert.Empty(pushedStatus.RootElement.GetProperty("host").GetProperty("appLaunchActions").EnumerateArray());
            Assert.Empty(appLaunch.ActionIds);
            Assert.False(result.GetProperty("succeeded").GetBoolean());
            Assert.Equal("permission-denied", result.GetProperty("code").GetString());
        }
        finally
        {
            AppPermissionSettings.Save(originalPermissions);
        }
    }

    [Fact]
    public async Task DeviceOverrideCanAllowConfiguredAppLaunchBlockedGlobally()
    {
        var originalPermissions = AppPermissionSettings.Load();
        var appLaunch = new FakeAppLaunchService(
            [new AppLaunchActionSummary("preset.browser", "Browser", "browser")],
            new AppLaunchExecutionResult(true, "started", "Started Browser."));

        try
        {
            AppPermissionSettings.Save(originalPermissions with { AllowRemoteAppLaunch = false });
            await using var fixture = await WebHostFixture.StartAsync(appLaunchService: appLaunch);
            var clientId = $"client-{Guid.NewGuid():N}";
            using var socket = await ConnectAsync(fixture.WebHost);
            await SendAndReceiveAsync(socket, new
            {
                type = "pair.hello",
                clientId,
                deviceName = "Phone",
                pairToken = fixture.Manager.CreatePairingToken()
            });

            Assert.True(fixture.Manager.SetDevicePermissionOverrides(
                clientId,
                new DevicePermissionOverrides(AllowRemoteAppLaunch: true)));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var pushedStatus = JsonDocument.Parse(await ReceiveTextAsync(socket, timeout.Token));
            var result = await SendAndReceiveAsync(socket, new { type = "app.launch", operationId = "op-app-3", actionId = "preset.browser" });

            Assert.Single(pushedStatus.RootElement.GetProperty("host").GetProperty("appLaunchActions").EnumerateArray());
            Assert.Equal(new[] { "preset.browser" }, appLaunch.ActionIds);
            Assert.True(result.GetProperty("succeeded").GetBoolean());
        }
        finally
        {
            AppPermissionSettings.Save(originalPermissions);
        }
    }

    [Fact]
    public async Task WebSocketRejectsMalformedConfiguredAppLaunchId()
    {
        await using var fixture = await WebHostFixture.StartAsync(appLaunchService: new FakeAppLaunchService([], new(false, "not-configured", "Missing")));
        using var socket = await ConnectAsync(fixture.WebHost);
        await SendAndReceiveAsync(socket, new
        {
            type = "pair.hello",
            clientId = $"client-{Guid.NewGuid():N}",
            deviceName = "Phone",
            pairToken = fixture.Manager.CreatePairingToken()
        });

        await SendAsync(socket, new { type = "app.launch", operationId = "op-app-4", actionId = "..\\cmd.exe /c calc" });
        var closeStatus = await ReceiveCloseStatusAsync(socket);

        Assert.Equal(WebSocketCloseStatus.PolicyViolation, closeStatus);
    }
}
