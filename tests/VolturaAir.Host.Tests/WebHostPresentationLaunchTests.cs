namespace VolturaAir.Host.Tests;

[Collection(AppPermissionSettingsCollection.Name)]
public sealed class WebHostPresentationLaunchTests : WebHostServiceTestBase
{
    [Fact]
    public async Task SavedLaunchUsesOpaqueIdAndStartsAuthoritativeSession()
    {
        var originalAlpha = AppDeveloperSettings.EnableAlphaFeatures();
        AppDeveloperSettings.SetEnableAlphaFeatures(false);
        var path = Path.Combine(Path.GetTempPath(), $"VolturaAir-{Guid.NewGuid():N}.pptx");
        await File.WriteAllTextAsync(path, "test");
        try
        {
            var automation = new FakePowerPointAutomationService(
                new(PowerPointDiscoveryState.Ready, []));
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

            var result = await SendAndReceiveAsync(socket, new
            {
                type = "presentation.powerpoint.launch",
                operationId = "launch-1",
                presentationId = "report-1"
            });

            Assert.Equal("presentation.powerpoint.launch.result", result.GetProperty("type").GetString());
            Assert.True(result.GetProperty("succeeded").GetBoolean());
            Assert.Equal("runtime-1", result.GetProperty("runtimePresentationId").GetString());
            Assert.Equal("tracking", fixture.WebHost.PresentationSessionSnapshot.State);
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
            AppDeveloperSettings.SetEnableAlphaFeatures(originalAlpha);
        }
    }
}
