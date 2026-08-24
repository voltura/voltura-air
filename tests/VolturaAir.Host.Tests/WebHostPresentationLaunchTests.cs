namespace VolturaAir.Host.Tests;

[Collection(AppPermissionSettingsCollection.Name)]
public sealed class WebHostPresentationLaunchTests : WebHostServiceTestBase
{
    [Fact]
    public async Task SavedLaunchRechecksPermissionBeforeCreatingSession()
    {
        var originalPermissions = AppPermissionSettings.Load();
        var path = Path.Combine(Path.GetTempPath(), $"VolturaAir-{Guid.NewGuid():N}.pptx");
        await File.WriteAllTextAsync(path, "test");
        try
        {
            AppPermissionSettings.Save(originalPermissions with { AllowPresentationControl = true });
            var ready = new PowerPointPresentationSnapshot(
                "runtime-1",
                Path.GetFileName(path),
                false,
                12,
                1,
                null,
                "ready",
                path);
            var presenting = ready with
            {
                IsPresenting = true,
                CurrentShowPosition = 1,
                SlideShowState = "running"
            };
            var automation = new FakePowerPointAutomationService(
                new(PowerPointDiscoveryState.Ready, [ready]));
            automation.ExecuteAsyncHandler = (_, _) =>
            {
                AppPermissionSettings.Save(originalPermissions with { AllowPresentationControl = false });
                return Task.FromResult(new PowerPointAutomationResult(
                    true,
                    null,
                    "Done.",
                    new(PowerPointDiscoveryState.Ready, [presenting]),
                    presenting));
            };
            await using var fixture = await WebHostFixture.StartAsync(powerPointAutomation: automation);
            var endedAt = DateTimeOffset.UtcNow;
            Assert.True((await fixture.WebHost.PresentationReportStore.SaveAsync(
                new(
                    "operation-report-permission",
                    "report-permission",
                    "powerpoint",
                    endedAt.AddMinutes(-1),
                    endedAt,
                    0,
                    60,
                    60,
                    false,
                    [],
                    [],
                    SuggestedTitle: "Permission check",
                    PresentationFilePath: path),
                "client-launch-permission",
                "Presenter phone",
                CancellationToken.None)).Succeeded);
            using var socket = await ConnectAsync(fixture.WebHost);
            var token = fixture.Manager.CreatePairingToken();
            _ = await SendAndReceiveAsync(socket, new
            {
                type = "pair.hello",
                clientId = "client-launch-permission",
                deviceName = "Presenter phone",
                pairToken = token,
                reconnectPublicKey = PairingTestKey.PublicKeyForFreshPairing
            });
            fixture.Manager.SetDevicePermission(
                "client-launch-permission",
                DevicePermissionKind.PresentationControl,
                false);
            using (var status = JsonDocument.Parse(await ReceiveTextAsync(socket)))
            {
                Assert.Equal("status", status.RootElement.GetProperty("type").GetString());
            }

            var result = await SendAndReceiveAsync(socket, new
            {
                type = "presentation.powerpoint.launch",
                operationId = "launch-permission",
                presentationId = "report-permission"
            });
            while (result.GetProperty("type").GetString() != "presentation.powerpoint.launch.result")
            {
                using var frame = System.Text.Json.JsonDocument.Parse(await ReceiveTextAsync(socket));
                result = frame.RootElement.Clone();
            }

            Assert.False(result.GetProperty("succeeded").GetBoolean());
            Assert.Equal("permission-denied", result.GetProperty("code").GetString());
            Assert.Equal("inactive", fixture.WebHost.PresentationSessionSnapshot.State);
        }
        finally
        {
            AppPermissionSettings.Save(originalPermissions);
            File.Delete(path);
        }
    }

    [Fact]
    public async Task SavedLaunchUsesOpaqueIdAndAutomaticallySavesPreviousSession()
    {
        var path = Path.Combine(Path.GetTempPath(), $"VolturaAir-{Guid.NewGuid():N}.pptx");
        await File.WriteAllTextAsync(path, "test");
        try
        {
            var existing = new PowerPointPresentationSnapshot(
                "runtime-existing",
                "Existing presentation.pptx",
                true,
                8,
                2,
                2,
                "running");
            var automation = new FakePowerPointAutomationService(
                new(PowerPointDiscoveryState.Ready, [existing]));
            var ready = new PowerPointPresentationSnapshot(
                "runtime-1",
                Path.GetFileName(path),
                false,
                12,
                1,
                null,
                "ready",
                path);
            var presenting = ready with
            {
                IsPresenting = true,
                CurrentShowPosition = 1,
                SlideShowState = "running"
            };
            automation.ExecuteHandler = command =>
            {
                var selected = command.Action == "open" ? ready : presenting;
                var snapshot = new PowerPointAutomationSnapshot(PowerPointDiscoveryState.Ready, [selected]);
                automation.Publish(snapshot);
                return new(true, null, "Done.", snapshot, selected);
            };

            await using var fixture = await WebHostFixture.StartAsync(powerPointAutomation: automation);
            var endedAt = DateTimeOffset.UtcNow;
            var saved = await fixture.WebHost.PresentationReportStore.SaveAsync(
                new(
                    "operation-report-1",
                    "report-1",
                    "powerpoint",
                    endedAt.AddMinutes(-5),
                    endedAt,
                    0,
                    300,
                    300,
                    false,
                    [],
                    [],
                    SuggestedTitle: "Quarterly update",
                    PresentationFilePath: path),
                "client-launch",
                "Presenter phone",
                CancellationToken.None);
            Assert.True(saved.Succeeded);

            using var socket = await ConnectAsync(fixture.WebHost);
            var token = fixture.Manager.CreatePairingToken();
            var accepted = await SendAndReceiveAsync(socket, new
            {
                type = "pair.hello",
                clientId = "client-launch",
                deviceName = "Presenter phone",
                pairToken = token,
                reconnectPublicKey = PairingTestKey.PublicKeyForFreshPairing
            });
            var available = Assert.Single(
                accepted.GetProperty("capabilities")
                    .GetProperty("presentation")
                    .GetProperty("powerPoint")
                    .GetProperty("availablePresentations")
                    .EnumerateArray());
            Assert.Equal("report-1", available.GetProperty("presentationId").GetString());
            Assert.False(available.TryGetProperty("canonicalPath", out _));

            var tracked = await SendAndReceiveAsync(socket, new
            {
                type = "presentation.session",
                operationId = "track-existing",
                action = "start",
                runtimePresentationId = existing.RuntimePresentationId
            });
            Assert.True(tracked.GetProperty("succeeded").GetBoolean());
            using (var status = System.Text.Json.JsonDocument.Parse(
                await ReceiveTextAsync(socket)))
            {
                Assert.Equal("status", status.RootElement.GetProperty("type").GetString());
            }

            var result = await SendAndReceiveAsync(socket, new
            {
                type = "presentation.powerpoint.launch",
                operationId = "launch-1",
                presentationId = "report-1"
            });
            while (result.GetProperty("type").GetString() !=
                   "presentation.powerpoint.launch.result")
            {
                using var frame = System.Text.Json.JsonDocument.Parse(
                    await ReceiveTextAsync(socket));
                result = frame.RootElement.Clone();
            }

            Assert.Equal("presentation.powerpoint.launch.result", result.GetProperty("type").GetString());
            Assert.True(result.GetProperty("succeeded").GetBoolean());
            Assert.Equal("runtime-1", result.GetProperty("runtimePresentationId").GetString());
            Assert.Equal("tracking", fixture.WebHost.PresentationSessionSnapshot.State);
            Assert.Equal(2, fixture.WebHost.PresentationReportStore.ReadAll().Reports.Count);
            Assert.Collection(
                automation.Commands,
                command =>
                {
                    Assert.Equal("open", command.Action);
                    Assert.Equal(path, command.SourcePath);
                },
                command =>
                {
                    Assert.Equal("start", command.Action);
                    Assert.Equal("runtime-1", command.RuntimePresentationId);
                });
        }
        finally
        {
            File.Delete(path);
        }
    }
}
