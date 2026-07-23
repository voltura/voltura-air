using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

[Collection(AppPermissionSettingsCollection.Name)]
public sealed class WebHostPresentationReportTests : WebHostServiceTestBase
{
    [Fact]
    public async Task AuthenticatedSaveCapturesDeviceAndIsIdempotent()
    {
        AppDeveloperSettings.SetEnableAlphaFeatures(true);
        var originalPermissions = AppPermissionSettings.Load();
        try
        {
            AppPermissionSettings.Save(originalPermissions with { AllowPresentationControl = true });
            await using var fixture = await WebHostFixture.StartAsync();
            var clientId = $"client-{Guid.NewGuid():N}";
            using var socket = await ConnectAsync(fixture.WebHost);
            _ = await PairAsync(socket, fixture, clientId, "Presenter phone");
            var payload = CreatePayload("save-1", "report-1");

            var saved = await SendAndReceiveAsync(socket, payload);
            var retry = await SendAndReceiveAsync(socket, payload);

            Assert.Equal("presentation.report.save.result", saved.GetProperty("type").GetString());
            Assert.True(saved.GetProperty("succeeded").GetBoolean());
            Assert.True(retry.GetProperty("succeeded").GetBoolean());
            Assert.Equal("Presentation data was already saved.", retry.GetProperty("message").GetString());
            var report = Assert.Single(fixture.WebHost.PresentationReportStore.ReadAll().Reports);
            Assert.Equal("Presenter phone", report.DeviceName);
            Assert.NotEqual(clientId, report.DeviceKey);
            Assert.Equal(2, report.Slides.Count);
            Assert.Equal(WebSocketState.Open, socket.State);
        }
        finally
        {
            AppPermissionSettings.Save(originalPermissions);
        }
    }

    [Fact]
    public async Task InvalidReportReturnsCorrelatedFailureWithoutClosingAuthenticatedSocket()
    {
        AppDeveloperSettings.SetEnableAlphaFeatures(true);
        var originalPermissions = AppPermissionSettings.Load();
        try
        {
            AppPermissionSettings.Save(originalPermissions with { AllowPresentationControl = true });
            await using var fixture = await WebHostFixture.StartAsync();
            var clientId = $"client-{Guid.NewGuid():N}";
            using var socket = await ConnectAsync(fixture.WebHost);
            _ = await PairAsync(socket, fixture, clientId, "Presenter phone");
            var start = new DateTimeOffset(2026, 7, 23, 8, 0, 0, TimeSpan.Zero);

            var failed = await SendAndReceiveAsync(socket, new
            {
                type = "presentation.report.save",
                operationId = "save-invalid",
                reportId = "report-invalid",
                target = "powerpoint",
                startedAt = start,
                endedAt = start.AddDays(8),
                utcOffsetMinutes = 0,
                plannedDurationSeconds = 60,
                presentationDurationSeconds = 60,
                endedDuringBreak = false,
                breaks = Array.Empty<object>(),
                slides = Array.Empty<object>()
            });

            Assert.False(failed.GetProperty("succeeded").GetBoolean());
            Assert.Equal("invalid-report", failed.GetProperty("code").GetString());
            Assert.Equal("save-invalid", failed.GetProperty("operationId").GetString());
            Assert.Empty(fixture.WebHost.PresentationReportStore.ReadAll().Reports);
            Assert.Equal(WebSocketState.Open, socket.State);
        }
        finally
        {
            AppPermissionSettings.Save(originalPermissions);
        }
    }

    [Fact]
    public async Task ReportPermissionDenialDoesNotReachStorage()
    {
        AppDeveloperSettings.SetEnableAlphaFeatures(true);
        var originalPermissions = AppPermissionSettings.Load();
        try
        {
            AppPermissionSettings.Save(originalPermissions with { AllowPresentationControl = false });
            await using var fixture = await WebHostFixture.StartAsync();
            var clientId = $"client-{Guid.NewGuid():N}";
            using var socket = await ConnectAsync(fixture.WebHost);
            _ = await PairAsync(socket, fixture, clientId, "Presenter phone");

            var denied = await SendAndReceiveAsync(socket, CreatePayload("save-denied", "report-denied"));

            Assert.False(denied.GetProperty("succeeded").GetBoolean());
            Assert.Equal("permission-denied", denied.GetProperty("code").GetString());
            Assert.Empty(fixture.WebHost.PresentationReportStore.ReadAll().Reports);
            Assert.Equal(WebSocketState.Open, socket.State);
        }
        finally
        {
            AppPermissionSettings.Save(originalPermissions);
        }
    }

    private static object CreatePayload(string operationId, string reportId)
    {
        var start = new DateTimeOffset(2026, 7, 23, 8, 0, 0, TimeSpan.FromHours(2));
        return new
        {
            type = "presentation.report.save",
            operationId,
            reportId,
            target = "powerpoint",
            startedAt = start,
            endedAt = start.AddMinutes(3),
            utcOffsetMinutes = 120,
            plannedDurationSeconds = 180,
            presentationDurationSeconds = 120,
            endedDuringBreak = false,
            breaks = new[]
            {
                new
                {
                    breakNumber = 1,
                    presentationElapsedSeconds = 60,
                    breakDurationSeconds = 60,
                    startedAt = start.AddMinutes(1),
                    endedAt = start.AddMinutes(2),
                    sessionSlideMinimum = 1,
                    sessionSlideMaximum = 2,
                    slideNumberAtStart = 2,
                    slideNumberAtEnd = 2
                }
            },
            slides = new[]
            {
                new { slideNumber = 1, durationSeconds = (double?)60 },
                new { slideNumber = 2, durationSeconds = (double?)60 }
            }
        };
    }

    private static async Task<JsonElement> PairAsync(
        WebSocket socket,
        WebHostFixture fixture,
        string clientId,
        string deviceName)
    {
        var token = fixture.Manager.CreatePairingToken();
        return await SendAndReceiveAsync(socket, new
        {
            type = "pair.hello",
            clientId,
            deviceName,
            pairToken = token,
            reconnectPublicKey = PairingTestKey.PublicKeyForFreshPairing
        });
    }
}
