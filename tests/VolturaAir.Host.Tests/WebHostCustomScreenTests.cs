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

    [Fact]
    public async Task KnownApplicationActionUsesOnlyTheSelectedProfile()
    {
        var launches = new FakeAppLaunchService(
            [],
            new AppLaunchExecutionResult(true, "focused", "Focused VLC."));
        await using var fixture = await WebHostFixture.StartAsync(appLaunchService: launches);

        var result = await InvokeAsync(
            fixture,
            new CustomScreenAction("knownApp", ActionId: "vlc"));

        Assert.True(result.GetProperty("succeeded").GetBoolean());
        Assert.Equal(["vlc"], launches.ActionIds);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SingleKnownApplicationDependencyIsRevalidatedBeforeDispatch(bool available)
    {
        var launches = new FakeAppLaunchService(
            [],
            new AppLaunchExecutionResult(true, "focused", "Focused VLC."),
            [new("vlc", "VLC", available)]);
        await using var fixture = await WebHostFixture.StartAsync(appLaunchService: launches);

        var result = await InvokeAsync(
            fixture,
            new CustomScreenAction("shortcut", Key: "Space", Modifiers: []),
            requiredKnownApp: "vlc");

        Assert.Equal(available, result.GetProperty("succeeded").GetBoolean());
        if (available)
        {
            Assert.NotEmpty(fixture.InputInjector.Events);
        }
        else
        {
            Assert.Equal("action-unavailable", result.GetProperty("code").GetString());
            Assert.Equal("This Custom Screen requires VLC on the PC.", result.GetProperty("message").GetString());
            Assert.Empty(fixture.InputInjector.Events);
        }
    }

    [Fact]
    public async Task LaserPointerUsesPresentationPermissionWithoutRemoteInput()
    {
        var originalPermissions = AppPermissionSettings.Load();
        try
        {
            AppPermissionSettings.Save(originalPermissions with
            {
                AllowPresentationControl = true,
                AllowRemoteInput = false
            });
            await using var fixture = await WebHostFixture.StartAsync();

            var result = await InvokeAsync(
                fixture,
                new CustomScreenAction("laserPointer", Color: "green"));

            Assert.True(result.GetProperty("succeeded").GetBoolean());
            Assert.Equal("enabled", result.GetProperty("code").GetString());
        }
        finally
        {
            AppPermissionSettings.Save(originalPermissions);
        }
    }

    [Fact]
    public async Task LaserPointerRequiresPresentationPermission()
    {
        var originalPermissions = AppPermissionSettings.Load();
        try
        {
            AppPermissionSettings.Save(originalPermissions with
            {
                AllowPresentationControl = false,
                AllowRemoteInput = true
            });
            await using var fixture = await WebHostFixture.StartAsync();

            var result = await InvokeAsync(
                fixture,
                new CustomScreenAction("laserPointer", Color: "red"));

            Assert.False(result.GetProperty("succeeded").GetBoolean());
            Assert.Equal("permission-denied", result.GetProperty("code").GetString());
        }
        finally
        {
            AppPermissionSettings.Save(originalPermissions);
        }
    }

    [Fact]
    public async Task EnabledStateIsRejectedForOrdinaryCustomScreenActions()
    {
        await using var fixture = await WebHostFixture.StartAsync();

        var result = await InvokeAsync(
            fixture,
            new CustomScreenAction("builtIn", BuiltIn: "media.playPause"),
            enabled: false);

        Assert.False(result.GetProperty("succeeded").GetBoolean());
        Assert.Equal("invalid-request", result.GetProperty("code").GetString());
        Assert.Empty(fixture.InputInjector.Events);
    }

    [Fact]
    public async Task WebsiteActionUsesTheExistingValidatedUrlBoundary()
    {
        var originalPermissions = AppPermissionSettings.Load();
        var launcher = new RecordingUrlLauncher();
        try
        {
            AppPermissionSettings.Save(originalPermissions with { AllowUrlOpen = true });
            await using var fixture = await WebHostFixture.StartAsync(
                urlOpenService: new UrlOpenService(launcher));

            var result = await InvokeAsync(
                fixture,
                new CustomScreenAction("urlOpen", Url: "https://example.com/screen"));

            Assert.True(result.GetProperty("succeeded").GetBoolean());
            Assert.Equal(new Uri("https://example.com/screen"), Assert.Single(launcher.Opened));
        }
        finally
        {
            AppPermissionSettings.Save(originalPermissions);
        }
    }

    [Fact]
    public async Task HostActionRequiresItsExistingDevicePermission()
    {
        var originalPermissions = AppPermissionSettings.Load();
        var power = new RecordingPowerController();
        try
        {
            AppPermissionSettings.Save(originalPermissions with { AllowDisplayControl = false });
            await using var deniedFixture = await WebHostFixture.StartAsync(powerController: power);
            var denied = await InvokeAsync(
                deniedFixture,
                new CustomScreenAction("hostAction", ActionId: "display.off"));
            Assert.False(denied.GetProperty("succeeded").GetBoolean());
            Assert.Equal("permission-denied", denied.GetProperty("code").GetString());
            Assert.Empty(power.Actions);

            AppPermissionSettings.Save(originalPermissions with { AllowDisplayControl = true });
            await using var allowedFixture = await WebHostFixture.StartAsync(powerController: power);
            var allowed = await InvokeAsync(
                allowedFixture,
                new CustomScreenAction("hostAction", ActionId: "display.off"));
            Assert.True(allowed.GetProperty("succeeded").GetBoolean());
            Assert.Equal([SystemPowerActions.DisplayOff], power.Actions);
        }
        finally
        {
            AppPermissionSettings.Save(originalPermissions);
        }
    }

    private static async Task<System.Text.Json.JsonElement> InvokeAsync(
        WebHostFixture fixture,
        CustomScreenAction action,
        string? requiredKnownApp = null,
        bool? enabled = null)
    {
        var clientId = $"client-{Guid.NewGuid():N}";
        var draft = CustomScreenService.CreateDraft();
        var sourceButton = draft.Sections[0].Buttons[0];
        var buttons = new List<CustomScreenButton>
        {
            sourceButton with
            {
                Presentation = CustomScreenService.RequiresLabelOnlyPresentation(action)
                    ? "label"
                    : sourceButton.Presentation,
                Action = action
            }
        };
        if (requiredKnownApp is not null)
        {
            buttons.Add(sourceButton with
            {
                Id = "button.required-app",
                Name = "Open required app",
                Label = "Open app",
                Action = new CustomScreenAction("knownApp", ActionId: requiredKnownApp)
            });
        }
        draft = draft with
        {
            Sections = [draft.Sections[0] with
            {
                Buttons = buttons
            }]
        };
        Assert.True(fixture.WebHost.CustomScreenService.TrySave(draft, out var saved, out var error), error);
        Assert.True(fixture.WebHost.CustomScreenService.TryAssign(saved.Id, [clientId], out error), error);
        var assigned = fixture.WebHost.CustomScreenService.Find(saved.Id)!;
        var button = Assert.Single(assigned.Sections).Buttons[0];
        using var socket = await ConnectAsync(fixture.WebHost);
        await SendAndReceiveAsync(socket, new
        {
            type = "pair.hello",
            clientId,
            deviceName = "Custom screen action phone",
            pairToken = fixture.Manager.CreatePairingToken(),
            reconnectPublicKey = PairingTestKey.PublicKeyForFreshPairing
        });
        var payload = new System.Text.Json.Nodes.JsonObject
        {
            ["type"] = "custom.screen.invoke",
            ["operationId"] = $"invoke-{Guid.NewGuid():N}",
            ["screenId"] = assigned.Id,
            ["screenRevision"] = assigned.Revision,
            ["buttonId"] = button.Id
        };
        if (enabled is { } requestedState)
        {
            payload["enabled"] = requestedState;
        }

        await SendAsync(socket, payload);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (true)
        {
            using var response = System.Text.Json.JsonDocument.Parse(
                await ReceiveTextAsync(socket, timeout.Token));
            if (response.RootElement.GetProperty("type").GetString() ==
                "custom.screen.invoke.result")
            {
                return response.RootElement.Clone();
            }
        }
    }

    private sealed class RecordingUrlLauncher : IUrlShellLauncher
    {
        public List<Uri> Opened { get; } = [];

        public void Open(Uri uri) => Opened.Add(uri);
    }

    private sealed class RecordingPowerController : ISystemPowerController
    {
        public List<string> Actions { get; } = [];

        public SystemPowerExecutionResult TryExecute(string action)
        {
            Actions.Add(action);
            return SystemPowerExecutionResult.Success;
        }

        public bool IsActionAvailable(string action) => true;

        public bool DismissBlackoutIfActive() => false;
    }
}
