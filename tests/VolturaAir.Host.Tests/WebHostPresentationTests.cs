using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

[Collection(AppPermissionSettingsCollection.Name)]
public sealed class WebHostPresentationTests : WebHostServiceTestBase
{
    [Fact]
    public void ProductionCompositionCreatesPowerPointAutomation()
    {
        var factoryCalled = false;
        var expected = CreatePresentingPowerPoint();

        var automation = WebHostService.ResolvePowerPointAutomation(
            supplied: null,
            isolatedTestMode: false,
            createActive: () =>
            {
                factoryCalled = true;
                return expected;
            },
            out var ownsPowerPoint);

        Assert.Same(expected, automation);
        Assert.True(factoryCalled);
        Assert.True(ownsPowerPoint);
    }

    [Theory]
    [InlineData("next", null, null)]
    [InlineData("previous", null, null)]
    [InlineData("first", null, null)]
    [InlineData("last", null, null)]
    [InlineData("goto", 7, null)]
    [InlineData("black", null, null)]
    [InlineData("white", null, null)]
    [InlineData("pause", null, true)]
    [InlineData("end", null, null)]
    [InlineData("start", null, null)]
    [InlineData("start-current", null, null)]
    [InlineData("activate", null, null)]
    public async Task PowerPointCommandsUseVerifiedAutomationWithoutInput(
        string action,
        int? slideNumber,
        bool? enabled)
    {
        AppDeveloperSettings.SetEnableAlphaFeatures(true);
        var originalPermissions = AppPermissionSettings.Load();
        try
        {
            AppPermissionSettings.Save(originalPermissions with { AllowPresentationControl = true });
            var automation = CreatePresentingPowerPoint();
            var appLog = new RecordingAppLog();
            await using var fixture = await WebHostFixture.StartAsync(
                appLog: appLog,
                powerPointAutomation: automation);
            using var socket = await ConnectAsync(fixture.WebHost);
            _ = await PairAsync(socket, fixture, $"client-{Guid.NewGuid():N}");
            var message = new Dictionary<string, object?>
            {
                ["type"] = "presentation.command",
                ["operationId"] = $"command-{action}",
                ["target"] = "powerpoint",
                ["action"] = action,
                ["runtimePresentationId"] = "presentation-a"
            };
            if (slideNumber is not null)
            {
                message["slideNumber"] = slideNumber;
            }

            if (enabled is not null)
            {
                message["enabled"] = enabled;
            }

            var result = await SendPresentationResultAsync(socket, message);

            Assert.True(result.GetProperty("succeeded").GetBoolean());
            Assert.Empty(fixture.InputInjector.Events);
            var command = Assert.Single(automation.Commands);
            Assert.Equal(
                action switch
                {
                    "start" => "first",
                    "start-current" => "activate",
                    _ => action
                },
                command.Action);
            Assert.Equal(slideNumber, command.SlideNumber);
            Assert.Equal(enabled, command.Enabled);
            var logEntry = Assert.Single(
                appLog.Entries,
                entry => entry.Event == "command_outcome");
            Assert.Equal("command_outcome", logEntry.Event);
            Assert.Equal("presentation.command", logEntry.MessageType);
            Assert.Equal($"powerpoint:{action}", logEntry.Action);
            Assert.Equal("executed", logEntry.Outcome);
        }
        finally
        {
            AppPermissionSettings.Save(originalPermissions);
        }
    }

    [Fact]
    public async Task StartingTheAlreadyTrackedPresentationNavigatesWithoutRejectingTheSession()
    {
        AppDeveloperSettings.SetEnableAlphaFeatures(true);
        var originalPermissions = AppPermissionSettings.Load();
        try
        {
            AppPermissionSettings.Save(originalPermissions with { AllowPresentationControl = true });
            var automation = CreatePresentingPowerPoint();
            await using var fixture = await WebHostFixture.StartAsync(
                powerPointAutomation: automation);
            var clientId = $"client-{Guid.NewGuid():N}";
            using var socket = await ConnectAsync(fixture.WebHost);
            _ = await PairAsync(socket, fixture, clientId);

            var first = await SendPresentationResultAsync(socket, new
            {
                type = "presentation.command",
                operationId = "start-first",
                target = "powerpoint",
                action = "start",
                runtimePresentationId = "presentation-a"
            });
            var second = await SendPresentationResultAsync(socket, new
            {
                type = "presentation.command",
                operationId = "start-again",
                target = "powerpoint",
                action = "start",
                runtimePresentationId = "presentation-a"
            });

            Assert.True(first.GetProperty("succeeded").GetBoolean());
            Assert.True(second.GetProperty("succeeded").GetBoolean());
            Assert.Equal(
                ["first", "first"],
                automation.Commands.Select(command => command.Action));
        }
        finally
        {
            AppPermissionSettings.Save(originalPermissions);
        }
    }

    [Theory]
    [InlineData("black")]
    [InlineData("white")]
    public async Task ReadyPowerPointBlankCommandsUseVolturaOverlayWithoutAutomationOrInput(
        string action)
    {
        AppDeveloperSettings.SetEnableAlphaFeatures(true);
        var originalPermissions = AppPermissionSettings.Load();
        try
        {
            AppPermissionSettings.Save(originalPermissions with { AllowPresentationControl = true });
            var automation = CreateReadyPowerPoint();
            var blankOverlay = new FakePresentationBlankPowerController();
            await using var fixture = await WebHostFixture.StartAsync(
                powerPointAutomation: automation,
                powerController: blankOverlay);
            using var socket = await ConnectAsync(fixture.WebHost);
            _ = await PairAsync(socket, fixture, $"client-{Guid.NewGuid():N}");

            var result = await SendPresentationResultAsync(socket, new
            {
                type = "presentation.command",
                operationId = $"ready-{action}",
                target = "powerpoint",
                action,
                runtimePresentationId = "presentation-a"
            });

            Assert.True(result.GetProperty("succeeded").GetBoolean());
            Assert.Equal(action == "white", blankOverlay.LastWhite);
            Assert.True(blankOverlay.IsBlank);
            Assert.Equal(
                action,
                result.GetProperty("presentation").GetProperty("slideShowState").GetString());
            Assert.Empty(automation.Commands);
            Assert.Empty(fixture.InputInjector.Events);
        }
        finally
        {
            AppPermissionSettings.Save(originalPermissions);
        }
    }

    [Fact]
    public async Task ReadyPowerPointBlankFailureIsExplicitAndDoesNotUseAutomationOrInput()
    {
        AppDeveloperSettings.SetEnableAlphaFeatures(true);
        var originalPermissions = AppPermissionSettings.Load();
        try
        {
            AppPermissionSettings.Save(originalPermissions with { AllowPresentationControl = true });
            var automation = CreateReadyPowerPoint();
            var blankOverlay = new FakePresentationBlankPowerController
            {
                ShowSucceeds = false
            };
            await using var fixture = await WebHostFixture.StartAsync(
                powerPointAutomation: automation,
                powerController: blankOverlay);
            using var socket = await ConnectAsync(fixture.WebHost);
            _ = await PairAsync(socket, fixture, $"client-{Guid.NewGuid():N}");

            var result = await SendPresentationResultAsync(socket, new
            {
                type = "presentation.command",
                operationId = "ready-white-failure",
                target = "powerpoint",
                action = "white",
                runtimePresentationId = "presentation-a"
            });

            Assert.False(result.GetProperty("succeeded").GetBoolean());
            Assert.Equal("presentation-blank-failed", result.GetProperty("code").GetString());
            Assert.Empty(automation.Commands);
            Assert.Empty(fixture.InputInjector.Events);
        }
        finally
        {
            AppPermissionSettings.Save(originalPermissions);
        }
    }

    [Fact]
    public async Task ReadyPowerPointGotoValidatesThenStartsAndNavigatesWithoutInput()
    {
        AppDeveloperSettings.SetEnableAlphaFeatures(true);
        var originalPermissions = AppPermissionSettings.Load();
        try
        {
            AppPermissionSettings.Save(originalPermissions with { AllowPresentationControl = true });
            var automation = CreateReadyPowerPoint();
            var presenting = automation.Snapshot.Presentations[0] with
            {
                IsPresenting = true,
                CurrentSlideIndex = 8,
                CurrentShowPosition = 8,
                SlideShowState = "running"
            };
            automation.ExecuteHandler = command => new(
                true,
                null,
                "Done.",
                new(PowerPointDiscoveryState.Ready, [presenting]),
                presenting);
            await using var fixture = await WebHostFixture.StartAsync(
                powerPointAutomation: automation);
            using var socket = await ConnectAsync(fixture.WebHost);
            _ = await PairAsync(socket, fixture, $"client-{Guid.NewGuid():N}");

            var result = await SendPresentationResultAsync(socket, new
            {
                type = "presentation.command",
                operationId = "ready-goto",
                target = "powerpoint",
                action = "goto",
                runtimePresentationId = "presentation-a",
                slideNumber = 8
            });

            Assert.True(result.GetProperty("succeeded").GetBoolean());
            Assert.Equal(
                ["start", "goto"],
                automation.Commands.Select(command => command.Action));
            Assert.Equal(8, automation.Commands[1].SlideNumber);
            Assert.Empty(fixture.InputInjector.Events);
        }
        finally
        {
            AppPermissionSettings.Save(originalPermissions);
        }
    }

    [Theory]
    [InlineData("previous", 2)]
    [InlineData("next", 4)]
    public async Task ReadyPowerPointNavigationStartsFromTheEditorSlideThenMoves(
        string action,
        int expectedSlide)
    {
        AppDeveloperSettings.SetEnableAlphaFeatures(true);
        var originalPermissions = AppPermissionSettings.Load();
        try
        {
            AppPermissionSettings.Save(originalPermissions with { AllowPresentationControl = true });
            var automation = CreateReadyPowerPoint();
            var ready = automation.Snapshot.Presentations[0] with
            {
                CurrentSlideIndex = 3
            };
            automation.Publish(new(PowerPointDiscoveryState.Ready, [ready]));
            var started = ready with
            {
                IsPresenting = true,
                CurrentShowPosition = 3,
                SlideShowState = "running"
            };
            var navigated = started with
            {
                CurrentSlideIndex = expectedSlide,
                CurrentShowPosition = expectedSlide
            };
            automation.ExecuteHandler = command => command.Action == "start-current"
                ? new(true, null, "Started.", new(PowerPointDiscoveryState.Ready, [started]), started)
                : new(true, null, "Navigated.", new(PowerPointDiscoveryState.Ready, [navigated]), navigated);
            await using var fixture = await WebHostFixture.StartAsync(
                powerPointAutomation: automation);
            using var socket = await ConnectAsync(fixture.WebHost);
            _ = await PairAsync(socket, fixture, $"client-{Guid.NewGuid():N}");

            var result = await SendPresentationResultAsync(socket, new
            {
                type = "presentation.command",
                operationId = $"ready-{action}",
                target = "powerpoint",
                action,
                runtimePresentationId = "presentation-a"
            });

            Assert.True(result.GetProperty("succeeded").GetBoolean());
            Assert.Equal(
                ["start-current", action],
                automation.Commands.Select(command => command.Action));
            Assert.Equal(
                expectedSlide,
                result.GetProperty("presentation").GetProperty("currentSlideIndex").GetInt32());
            Assert.Empty(fixture.InputInjector.Events);
        }
        finally
        {
            AppPermissionSettings.Save(originalPermissions);
        }
    }

    [Fact]
    public async Task ReadyPowerPointNavigationDoesNotGuessWhenEditorSlideIsUnavailable()
    {
        AppDeveloperSettings.SetEnableAlphaFeatures(true);
        var originalPermissions = AppPermissionSettings.Load();
        try
        {
            AppPermissionSettings.Save(originalPermissions with { AllowPresentationControl = true });
            var automation = CreateReadyPowerPoint();
            await using var fixture = await WebHostFixture.StartAsync(
                powerPointAutomation: automation);
            using var socket = await ConnectAsync(fixture.WebHost);
            _ = await PairAsync(socket, fixture, $"client-{Guid.NewGuid():N}");

            var result = await SendPresentationResultAsync(socket, new
            {
                type = "presentation.command",
                operationId = "ready-next-without-editor-slide",
                target = "powerpoint",
                action = "next",
                runtimePresentationId = "presentation-a"
            });

            Assert.False(result.GetProperty("succeeded").GetBoolean());
            Assert.Equal(
                "powerpoint-current-slide-unavailable",
                result.GetProperty("code").GetString());
            Assert.Empty(automation.Commands);
            Assert.Empty(fixture.InputInjector.Events);
        }
        finally
        {
            AppPermissionSettings.Save(originalPermissions);
        }
    }

    [Fact]
    public async Task StartingReadyPowerPointDismissesVolturaBlankOverlay()
    {
        AppDeveloperSettings.SetEnableAlphaFeatures(true);
        var originalPermissions = AppPermissionSettings.Load();
        try
        {
            AppPermissionSettings.Save(originalPermissions with { AllowPresentationControl = true });
            var automation = CreateReadyPowerPoint();
            var blankOverlay = new FakePresentationBlankPowerController();
            var presenting = automation.Snapshot.Presentations[0] with
            {
                IsPresenting = true,
                CurrentSlideIndex = 1,
                CurrentShowPosition = 1,
                SlideShowState = "running"
            };
            automation.ExecuteHandler = _ => new(
                true,
                null,
                "Done.",
                new(PowerPointDiscoveryState.Ready, [presenting]),
                presenting);
            await using var fixture = await WebHostFixture.StartAsync(
                powerPointAutomation: automation,
                powerController: blankOverlay);
            using var socket = await ConnectAsync(fixture.WebHost);
            _ = await PairAsync(socket, fixture, $"client-{Guid.NewGuid():N}");
            _ = await SendPresentationResultAsync(socket, new
            {
                type = "presentation.command",
                operationId = "ready-black-before-start",
                target = "powerpoint",
                action = "black",
                runtimePresentationId = "presentation-a"
            });
            Assert.True(blankOverlay.IsBlank);

            var started = await SendPresentationResultAsync(socket, new
            {
                type = "presentation.command",
                operationId = "ready-start-after-black",
                target = "powerpoint",
                action = "start",
                runtimePresentationId = "presentation-a"
            });

            Assert.True(started.GetProperty("succeeded").GetBoolean());
            Assert.False(blankOverlay.IsBlank);
            Assert.Equal("start", Assert.Single(automation.Commands).Action);
            Assert.Empty(fixture.InputInjector.Events);
        }
        finally
        {
            AppPermissionSettings.Save(originalPermissions);
        }
    }

    [Fact]
    public async Task StartingSamePausedPresentationResumesWithoutSaveOrDiscard()
    {
        AppDeveloperSettings.SetEnableAlphaFeatures(true);
        var originalPermissions = AppPermissionSettings.Load();
        try
        {
            AppPermissionSettings.Save(originalPermissions with { AllowPresentationControl = true });
            var automation = CreateReadyPowerPoint();
            var ready = automation.Snapshot.Presentations[0];
            var presenting = ready with
            {
                IsPresenting = true,
                CurrentSlideIndex = 1,
                CurrentShowPosition = 1,
                SlideShowState = "running"
            };
            automation.ExecuteHandler = _ => new(
                true,
                null,
                "Done.",
                new(PowerPointDiscoveryState.Ready, [presenting]),
                presenting);
            await using var fixture = await WebHostFixture.StartAsync(
                powerPointAutomation: automation);
            using var socket = await ConnectAsync(fixture.WebHost);
            _ = await PairAsync(socket, fixture, $"client-{Guid.NewGuid():N}");

            var first = await SendPresentationResultAsync(socket, new
            {
                type = "presentation.command",
                operationId = "session-start-before-pause",
                target = "powerpoint",
                action = "start",
                runtimePresentationId = "presentation-a"
            });
            Assert.True(first.GetProperty("succeeded").GetBoolean());
            automation.Publish(new(PowerPointDiscoveryState.Ready, [ready]));

            var resumed = await SendPresentationResultAsync(socket, new
            {
                type = "presentation.command",
                operationId = "session-resume-same",
                target = "powerpoint",
                action = "start",
                runtimePresentationId = "presentation-a"
            });

            Assert.True(resumed.GetProperty("succeeded").GetBoolean());
            Assert.Equal(
                ["start", "start"],
                automation.Commands.Select(command => command.Action));
            Assert.Empty(fixture.InputInjector.Events);
        }
        finally
        {
            AppPermissionSettings.Save(originalPermissions);
        }
    }

    [Fact]
    public async Task PausedSessionBlocksDifferentPresentationBeforeAutomation()
    {
        AppDeveloperSettings.SetEnableAlphaFeatures(true);
        var originalPermissions = AppPermissionSettings.Load();
        try
        {
            AppPermissionSettings.Save(originalPermissions with { AllowPresentationControl = true });
            var automation = CreateReadyPowerPoint();
            var ready = automation.Snapshot.Presentations[0];
            var presenting = ready with
            {
                IsPresenting = true,
                CurrentSlideIndex = 1,
                CurrentShowPosition = 1,
                SlideShowState = "running"
            };
            automation.ExecuteHandler = _ => new(
                true,
                null,
                "Done.",
                new(PowerPointDiscoveryState.Ready, [presenting]),
                presenting);
            await using var fixture = await WebHostFixture.StartAsync(
                powerPointAutomation: automation);
            using var socket = await ConnectAsync(fixture.WebHost);
            _ = await PairAsync(socket, fixture, $"client-{Guid.NewGuid():N}");
            _ = await SendPresentationResultAsync(socket, new
            {
                type = "presentation.command",
                operationId = "different-session-start",
                target = "powerpoint",
                action = "start",
                runtimePresentationId = "presentation-a"
            });
            var different = ready with
            {
                RuntimePresentationId = "presentation-b",
                Name = "Different deck.pptx",
                SourcePath = @"C:\Presentations\Different deck.pptx"
            };
            automation.Publish(new(PowerPointDiscoveryState.Ready, [different]));

            var rejected = await SendPresentationResultAsync(socket, new
            {
                type = "presentation.command",
                operationId = "different-session-rejected",
                target = "powerpoint",
                action = "start",
                runtimePresentationId = "presentation-b"
            });

            Assert.False(rejected.GetProperty("succeeded").GetBoolean());
            Assert.Equal("session-active", rejected.GetProperty("code").GetString());
            Assert.Equal("start", Assert.Single(automation.Commands).Action);
            Assert.Empty(fixture.InputInjector.Events);
        }
        finally
        {
            AppPermissionSettings.Save(originalPermissions);
        }
    }

    [Fact]
    public async Task InvalidReadyPowerPointGotoDoesNotStartOrInjectInput()
    {
        AppDeveloperSettings.SetEnableAlphaFeatures(true);
        var originalPermissions = AppPermissionSettings.Load();
        try
        {
            AppPermissionSettings.Save(originalPermissions with { AllowPresentationControl = true });
            var automation = CreateReadyPowerPoint();
            await using var fixture = await WebHostFixture.StartAsync(
                powerPointAutomation: automation);
            using var socket = await ConnectAsync(fixture.WebHost);
            _ = await PairAsync(socket, fixture, $"client-{Guid.NewGuid():N}");

            var result = await SendPresentationResultAsync(socket, new
            {
                type = "presentation.command",
                operationId = "ready-goto-invalid",
                target = "powerpoint",
                action = "goto",
                runtimePresentationId = "presentation-a",
                slideNumber = 21
            });

            Assert.False(result.GetProperty("succeeded").GetBoolean());
            Assert.Equal("powerpoint-invalid-slide", result.GetProperty("code").GetString());
            Assert.Empty(automation.Commands);
            Assert.Empty(fixture.InputInjector.Events);
        }
        finally
        {
            AppPermissionSettings.Save(originalPermissions);
        }
    }

    [Fact]
    public async Task PresentationRemainsAdvertisedAndExecutableWhileAlphaFeaturesAreDisabled()
    {
        var originalPermissions = AppPermissionSettings.Load();
        try
        {
            AppDeveloperSettings.SetEnableAlphaFeatures(false);
            AppPermissionSettings.Save(originalPermissions with { AllowPresentationControl = true });
            var automation = CreatePresentingPowerPoint();
            await using var fixture = await WebHostFixture.StartAsync(
                powerPointAutomation: automation);
            var clientId = $"client-{Guid.NewGuid():N}";
            using var socket = await ConnectAsync(fixture.WebHost);
            var paired = await PairAsync(socket, fixture, clientId);

            var result = await SendPresentationResultAsync(socket, new
            {
                type = "presentation.command",
                operationId = "presentation-graduated",
                target = "powerpoint",
                action = "next",
                runtimePresentationId = "presentation-a"
            });
            var sessionResult = await SendPresentationFrameAsync(
                socket,
                "presentation.session.result",
                new
                {
                    type = "presentation.session",
                    operationId = "presentation-session-graduated",
                    action = "start",
                    runtimePresentationId = "presentation-a"
                });

            Assert.False(AppDeveloperSettings.EnableAlphaFeatures());
            Assert.True(paired.GetProperty("capabilities").GetProperty("presentation").GetProperty("canControl").GetBoolean());
            Assert.False(paired.GetProperty("capabilities").TryGetProperty("customScreens", out _));
            Assert.True(result.GetProperty("succeeded").GetBoolean());
            Assert.True(sessionResult.GetProperty("succeeded").GetBoolean());
            Assert.Contains(automation.Commands, command => command.Action == "next");
            Assert.Empty(fixture.InputInjector.Events);
            Assert.Equal(WebSocketState.Open, socket.State);
        }
        finally
        {
            AppPermissionSettings.Save(originalPermissions);
        }
    }

    [Fact]
    public async Task AlphaSettingOnlyChangesAlphaCapabilities()
    {
        AppDeveloperSettings.SetEnableAlphaFeatures(false);
        await using var fixture = await WebHostFixture.StartAsync();
        var clientId = $"client-{Guid.NewGuid():N}";
        using var socket = await ConnectAsync(fixture.WebHost);
        var paired = await PairAsync(socket, fixture, clientId);
        Assert.True(paired.GetProperty("capabilities").TryGetProperty("presentation", out _));
        Assert.False(paired.GetProperty("capabilities").TryGetProperty("customScreens", out _));

        AppDeveloperSettings.SetEnableAlphaFeatures(true);
        using var enabledTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        using var enabledStatus = JsonDocument.Parse(await ReceiveTextAsync(socket, enabledTimeout.Token));
        Assert.True(enabledStatus.RootElement.GetProperty("capabilities").GetProperty("presentation").GetProperty("canControl").GetBoolean());
        Assert.True(enabledStatus.RootElement.GetProperty("capabilities").TryGetProperty("customScreens", out _));

        AppDeveloperSettings.SetEnableAlphaFeatures(false);
        using var disabledTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        using var disabledStatus = JsonDocument.Parse(await ReceiveTextAsync(socket, disabledTimeout.Token));
        Assert.True(disabledStatus.RootElement.GetProperty("capabilities").TryGetProperty("presentation", out _));
        Assert.False(disabledStatus.RootElement.GetProperty("capabilities").TryGetProperty("customScreens", out _));
    }

    [Fact]
    public async Task PresentationLaserUsesHostOwnedStateAndReturnsItsMatchingResult()
    {
        AppDeveloperSettings.SetEnableAlphaFeatures(true);
        var originalPermissions = AppPermissionSettings.Load();
        try
        {
            AppPermissionSettings.Save(originalPermissions with { AllowPresentationControl = true });
            var automation = CreatePresentingPowerPoint();
            await using var fixture = await WebHostFixture.StartAsync(
                powerPointAutomation: automation);
            var clientId = $"client-{Guid.NewGuid():N}";
            using var socket = await ConnectAsync(fixture.WebHost);
            var paired = await PairAsync(socket, fixture, clientId);

            var result = await SendPresentationResultAsync(socket, new
            {
                type = "presentation.command",
                operationId = "presentation-1",
                target = "powerpoint",
                action = "pointer",
                enabled = true
            });

            Assert.True(paired.GetProperty("capabilities").GetProperty("presentation").GetProperty("canControl").GetBoolean());
            Assert.True(paired.GetProperty("capabilities").GetProperty("presentation").GetProperty("powerPoint").GetProperty("foregroundActivationSupported").GetBoolean());
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
    public async Task PowerPointPointerVisibilityFailureDoesNotDisableCustomLaserOrBlockLaterCommands()
    {
        AppDeveloperSettings.SetEnableAlphaFeatures(true);
        var originalPermissions = AppPermissionSettings.Load();
        try
        {
            AppPermissionSettings.Save(originalPermissions with { AllowPresentationControl = true });
            var automation = CreatePresentingPowerPoint();
            automation.ExecuteHandler = command => command.Action == "pointer"
                ? new(
                    false,
                    "powerpoint-automation-failed",
                    "Native pointer visibility failed.",
                    automation.Snapshot)
                : new(
                    true,
                    null,
                    "Done.",
                    automation.Snapshot,
                    automation.Snapshot.Presentations[0]);
            var appLog = new RecordingAppLog();
            await using var fixture = await WebHostFixture.StartAsync(
                appLog: appLog,
                powerPointAutomation: automation);
            var clientId = $"client-{Guid.NewGuid():N}";
            using var socket = await ConnectAsync(fixture.WebHost);
            _ = await PairAsync(socket, fixture, clientId);

            var enabled = await SendPresentationResultAsync(socket, new
            {
                type = "presentation.command",
                operationId = "pointer-enable-native-failure",
                target = "powerpoint",
                action = "pointer",
                runtimePresentationId = "presentation-a",
                enabled = true
            });
            var navigated = await SendPresentationResultAsync(socket, new
            {
                type = "presentation.command",
                operationId = "next-after-pointer-native-failure",
                target = "powerpoint",
                action = "next",
                runtimePresentationId = "presentation-a"
            });
            var disabled = await SendPresentationResultAsync(socket, new
            {
                type = "presentation.command",
                operationId = "pointer-disable-native-failure",
                target = "powerpoint",
                action = "pointer",
                runtimePresentationId = "presentation-a",
                enabled = false
            });

            Assert.True(enabled.GetProperty("succeeded").GetBoolean());
            Assert.True(enabled.GetProperty("laserPointerActive").GetBoolean());
            Assert.True(navigated.GetProperty("succeeded").GetBoolean());
            Assert.True(disabled.GetProperty("succeeded").GetBoolean());
            Assert.False(disabled.GetProperty("laserPointerActive").GetBoolean());
            Assert.Equal(
                ["pointer", "next", "pointer"],
                automation.Commands.Select(command => command.Action));
            Assert.Contains(
                appLog.Entries,
                entry =>
                    entry.Action == "presentation_laser_pointer_visibility" &&
                    entry.Outcome == "degraded");
            Assert.Contains(
                appLog.Entries,
                entry =>
                    entry.Action == "powerpoint_pointer_restore" &&
                    entry.Outcome == "degraded");
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

            var enabled = await SendPresentationResultAsync(socket, new
            {
                type = "presentation.command",
                operationId = "presentation-5",
                target = "pdf",
                action = "pointer",
                enabled = true
            });
            var disabled = await SendPresentationResultAsync(socket, new
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
            var automation = CreatePresentingPowerPoint();
            await using var fixture = await WebHostFixture.StartAsync(
                powerPointAutomation: automation);
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

    private static FakePowerPointAutomationService CreatePresentingPowerPoint() =>
        new(new(
            PowerPointDiscoveryState.Ready,
            [new(
                "presentation-a",
                "Quarterly update.pptx",
                true,
                20,
                4,
                4,
                "running")]));

    private static FakePowerPointAutomationService CreateReadyPowerPoint() =>
        new(new(
            PowerPointDiscoveryState.Ready,
            [new(
                "presentation-a",
                "Quarterly update.pptx",
                false,
                20,
                null,
                null,
                "ready",
                @"C:\Presentations\Quarterly update.pptx")]));

    private static async Task<JsonElement> SendPresentationResultAsync(
        WebSocket socket,
        object message) =>
        await SendPresentationFrameAsync(
            socket,
            "presentation.command.result",
            message);

    private static async Task<JsonElement> SendPresentationFrameAsync(
        WebSocket socket,
        string expectedType,
        object message)
    {
        await SendAsync(socket, message);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (true)
        {
            using var document = JsonDocument.Parse(await ReceiveTextAsync(socket, timeout.Token));
            if (document.RootElement.GetProperty("type").GetString() == expectedType)
            {
                return document.RootElement.Clone();
            }
        }
    }

    private sealed class FakePresentationBlankPowerController :
        ISystemPowerController,
        IPresentationBlankOverlay
    {
        public event EventHandler? StateChanged;

        internal bool ShowSucceeds { get; init; } = true;

        internal bool IsBlank { get; private set; }

        internal bool? LastWhite { get; private set; }

        public PresentationBlankOverlaySnapshot? Snapshot { get; private set; }

        public SystemPowerExecutionResult TryExecute(string action) =>
            SystemPowerExecutionResult.Success;

        public bool IsActionAvailable(string action) => true;

        public bool DismissBlackoutIfActive() =>
            DismissPresentationBlankIfActive();

        public SystemPowerExecutionResult TryShowPresentationBlank(
            string runtimePresentationId,
            bool white)
        {
            LastWhite = white;
            IsBlank = ShowSucceeds;
            Snapshot = ShowSucceeds
                ? new(runtimePresentationId, white ? "white" : "black")
                : null;
            StateChanged?.Invoke(this, EventArgs.Empty);
            return new(ShowSucceeds);
        }

        public bool DismissPresentationBlankIfActive()
        {
            var wasBlank = IsBlank;
            IsBlank = false;
            Snapshot = null;
            if (wasBlank)
            {
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
            return wasBlank;
        }
    }

    private sealed class RecordingAppLog : IAppLog
    {
        public event EventHandler? Changed;

        public string LogDirectory => string.Empty;

        public List<AppLogEntry> Entries { get; } = [];

        public AppLogDeleteResult DeleteAll() => new(true, 0);

        public AppLogReadResult Read(AppLogQuery query) => new(true, []);

        public void Write(AppLogEntry entry)
        {
            Entries.Add(entry);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
