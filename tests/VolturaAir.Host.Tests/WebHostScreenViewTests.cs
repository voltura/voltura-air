using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VolturaAir.Host.Tests;

[Collection(AppPermissionSettingsCollection.Name)]
public sealed class WebHostScreenViewTests : WebHostServiceTestBase
{
    [Fact]
    public async Task TestServerBindsOfferAndAnswerToThePairedClientAndReleasesCaptureOnStop()
    {
        HostPermissionSet originalPermissions = AppPermissionSettings.Load();
        try
        {
            AppPermissionSettings.Save(originalPermissions with { AllowScreenViewing = true });
            var capture = new FakeScreenViewCaptureSource();
            await using var fixture = await WebHostFixture.StartAsync(screenViewCapture: capture);
            using var reconnectKey = new PairingTestKey();
            using WebSocket control = await ConnectAsync(fixture.WebHost);
            await PairAsync(control, fixture.Manager, reconnectKey);
            JsonElement status = await SendUntilTypeAsync(control, new { type = "status.get" }, "status");
            JsonElement capability = status.GetProperty("capabilities").GetProperty("screenView");
            Assert.True(capability.GetProperty("enabled").GetBoolean());
            Assert.True(capability.GetProperty("permissionGranted").GetBoolean());
            Assert.True(capability.GetProperty("canView").GetBoolean());

            const string operationId = "screen-start-1";
            const string displayId = "display-1";
            JsonElement start = await StartAsync(control, reconnectKey, operationId, displayId);
            Assert.True(start.GetProperty("succeeded").GetBoolean());
            string offer = start.GetProperty("offerSdp").GetString()!;
            Assert.NotEmpty(offer);
            Assert.NotEmpty(start.GetProperty("hostSignature").GetString()!);

            var activityStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var activityStopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.WebHost.ScreenViewActivityChanged += (_, activity) =>
            {
                if (activity.Active) activityStarted.TrySetResult(); else activityStopped.TrySetResult();
            };

            const string answer = "v=0\r\no=phone 1 1 IN IP4 127.0.0.1\r\ns=answer\r\nt=0 0\r\n";
            string offerHash = HashSdp(offer);
            string answerHash = HashSdp(answer);
            string transcript = $"VolturaAir screen-view:answer:v2:client-screen:{operationId}:{displayId}:{offerHash}:{answerHash}";
            JsonElement answered = await SendUntilTypeAsync(control, new
            {
                type = "screen.view.answer",
                operationId,
                answerSdp = answer,
                clientSignature = reconnectKey.SignPayload(transcript)
            }, "screen.view.answer.result");
            Assert.True(answered.GetProperty("succeeded").GetBoolean());
            await activityStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await capture.Captured.Task.WaitAsync(TimeSpan.FromSeconds(2));

            JsonElement switched = await SendUntilTypeAsync(control, new
            {
                type = "screen.view.source.set",
                operationId = "screen-source-1",
                displayId = "display-2"
            }, "screen.view.source.result");
            Assert.True(switched.GetProperty("succeeded").GetBoolean());

            JsonElement stop = await SendUntilTypeAsync(control, new { type = "screen.view.stop", operationId = "screen-stop-1" }, "screen.view.stop.result");
            Assert.True(stop.GetProperty("succeeded").GetBoolean());
            await activityStopped.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(capture.EndCaptureCalls > 0);
        }
        finally
        {
            AppPermissionSettings.Save(originalPermissions);
        }
    }

    [Fact]
    public async Task PendingOfferKeepsTheSingleViewerSlotBusyAndInvalidAnswerReleasesIt()
    {
        HostPermissionSet originalPermissions = AppPermissionSettings.Load();
        try
        {
            AppPermissionSettings.Save(originalPermissions with { AllowScreenViewing = true });
            await using var fixture = await WebHostFixture.StartAsync(screenViewCapture: new FakeScreenViewCaptureSource());
            using var reconnectKey = new PairingTestKey();
            using WebSocket control = await ConnectAsync(fixture.WebHost);
            await PairAsync(control, fixture.Manager, reconnectKey);

            JsonElement first = await StartAsync(control, reconnectKey, "op-first", "display-1");
            Assert.True(first.GetProperty("succeeded").GetBoolean());
            JsonElement second = await StartAsync(control, reconnectKey, "op-second", "display-1");
            Assert.False(second.GetProperty("succeeded").GetBoolean());
            Assert.Equal("busy", second.GetProperty("code").GetString());

            JsonElement rejected = await SendUntilTypeAsync(control, new
            {
                type = "screen.view.answer",
                operationId = "op-first",
                answerSdp = "v=0\r\n",
                clientSignature = reconnectKey.SignPayload("wrong-transcript")
            }, "screen.view.answer.result");
            Assert.False(rejected.GetProperty("succeeded").GetBoolean());
            Assert.Equal("invalid-proof", rejected.GetProperty("code").GetString());

            JsonElement retry = await StartAsync(control, reconnectKey, "op-retry", "display-1");
            Assert.True(retry.GetProperty("succeeded").GetBoolean());
        }
        finally
        {
            AppPermissionSettings.Save(originalPermissions);
        }
    }

    [Fact]
    public async Task DisplayDiscoveryResultsKeepTheControlSocketOpenAcrossEmptyAndFailedDiscovery()
    {
        HostPermissionSet originalPermissions = AppPermissionSettings.Load();
        try
        {
            AppPermissionSettings.Save(originalPermissions with { AllowScreenViewing = true });
            var capture = new FakeScreenViewCaptureSource { Sources = [] };
            await using var fixture = await WebHostFixture.StartAsync(screenViewCapture: capture);
            using var reconnectKey = new PairingTestKey();
            using WebSocket control = await ConnectAsync(fixture.WebHost);
            await PairAsync(control, fixture.Manager, reconnectKey);

            JsonElement empty = await SendUntilTypeAsync(control, new
            {
                type = "screen.view.sources.get",
                operationId = "screen-sources-empty"
            }, "screen.view.sources.result");
            Assert.True(empty.GetProperty("succeeded").GetBoolean());
            Assert.Equal("accepted", empty.GetProperty("code").GetString());
            Assert.Equal("No displays are available.", empty.GetProperty("message").GetString());
            Assert.Empty(empty.GetProperty("sources").EnumerateArray());

            capture.DiscoveryFailure = new ScreenViewCaptureException(
                "capture-unavailable",
                "Windows desktop capture is unavailable.");
            JsonElement failedSources = await SendUntilTypeAsync(control, new
            {
                type = "screen.view.sources.get",
                operationId = "screen-sources-failed"
            }, "screen.view.sources.result");
            Assert.False(failedSources.GetProperty("succeeded").GetBoolean());
            Assert.Equal("capture-unavailable", failedSources.GetProperty("code").GetString());

            JsonElement failedStart = await StartAsync(control, reconnectKey, "screen-start-failed", "display-1");
            Assert.False(failedStart.GetProperty("succeeded").GetBoolean());
            Assert.Equal("capture-unavailable", failedStart.GetProperty("code").GetString());

            JsonElement statusAfterFailures = await SendUntilTypeAsync(
                control,
                new { type = "status.get" },
                "status");
            Assert.True(statusAfterFailures.GetProperty("connected").GetBoolean());

            capture.DiscoveryFailure = null;
            capture.Sources = FakeScreenViewCaptureSource.DefaultSources;
            const string operationId = "screen-start-before-switch-failure";
            const string displayId = "display-1";
            JsonElement start = await StartAsync(control, reconnectKey, operationId, displayId);
            Assert.True(start.GetProperty("succeeded").GetBoolean());
            string offerHash = HashSdp(start.GetProperty("offerSdp").GetString()!);
            const string answer = "v=0\r\no=phone 1 1 IN IP4 127.0.0.1\r\ns=answer\r\nt=0 0\r\n";
            string answerHash = HashSdp(answer);
            string transcript = $"VolturaAir screen-view:answer:v2:client-screen:{operationId}:{displayId}:{offerHash}:{answerHash}";
            JsonElement answered = await SendUntilTypeAsync(control, new
            {
                type = "screen.view.answer",
                operationId,
                answerSdp = answer,
                clientSignature = reconnectKey.SignPayload(transcript)
            }, "screen.view.answer.result");
            Assert.True(answered.GetProperty("succeeded").GetBoolean());

            capture.DiscoveryFailure = new ScreenViewCaptureException(
                "capture-unavailable",
                "Windows desktop capture is unavailable.");
            JsonElement failedSwitch = await SendUntilTypeAsync(control, new
            {
                type = "screen.view.source.set",
                operationId = "screen-source-failed",
                displayId = "display-2"
            }, "screen.view.source.result");
            Assert.False(failedSwitch.GetProperty("succeeded").GetBoolean());
            Assert.Equal("capture-unavailable", failedSwitch.GetProperty("code").GetString());

            JsonElement finalStatus = await SendUntilTypeAsync(control, new { type = "status.get" }, "status");
            Assert.True(finalStatus.GetProperty("connected").GetBoolean());
        }
        finally
        {
            AppPermissionSettings.Save(originalPermissions);
        }
    }

    private static async Task PairAsync(WebSocket control, PairingManager manager, PairingTestKey key)
    {
        JsonElement accepted = await SendAndReceiveAsync(control, new
        {
            type = "pair.hello",
            clientId = "client-screen",
            deviceName = "Screen test phone",
            pairToken = manager.CreatePairingToken(),
            reconnectPublicKey = key.PublicKey
        });
        Assert.Equal("pair.accepted", accepted.GetProperty("type").GetString());
    }

    private static Task<JsonElement> StartAsync(WebSocket control, PairingTestKey key, string operationId, string displayId)
    {
        string transcript = $"VolturaAir screen-view:start:v2:client-screen:{operationId}:{displayId}";
        return SendUntilTypeAsync(control, new
        {
            type = "screen.view.start",
            operationId,
            displayId,
            clientSignature = key.SignPayload(transcript)
        }, "screen.view.start.result");
    }

    private static async Task<JsonElement> SendUntilTypeAsync(WebSocket socket, object payload, string expectedType)
    {
        await SendAsync(socket, payload);
        for (int attempt = 0; attempt < 8; attempt++)
        {
            string text = await ReceiveTextAsync(socket, new CancellationTokenSource(TimeSpan.FromSeconds(2)).Token);
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.GetProperty("type").GetString() == expectedType) return document.RootElement.Clone();
        }
        throw new InvalidOperationException($"The host did not send {expectedType}.");
    }

    private static string HashSdp(string sdp) => ScreenViewHostIdentity.Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(sdp)));

    private sealed class FakeScreenViewCaptureSource : IScreenViewCaptureSource
    {
        public static IReadOnlyList<ScreenViewSource> DefaultSources { get; } =
            [new("display-1", "Display 1", 800, 600, true), new("display-2", "Display 2", 1280, 720, false)];
        public TaskCompletionSource Captured { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int EndCaptureCalls { get; private set; }
        public IReadOnlyList<ScreenViewSource> Sources { get; set; } = DefaultSources;
        public ScreenViewCaptureException? DiscoveryFailure { get; set; }
        public IReadOnlyList<ScreenViewSource> GetSources() =>
            DiscoveryFailure is null ? Sources : throw DiscoveryFailure;
        public Task<ScreenViewEncodedFrame?> CaptureVideoAsync(string sourceId, ScreenViewCaptureProfile profile, int bitrate, bool forceKeyFrame, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Captured.TrySetResult();
            return Task.FromResult<ScreenViewEncodedFrame?>(new([0, 0, 0, 1, 0x65, 1], 800, 600, 30, true));
        }
        public void EndCapture() => EndCaptureCalls++;
    }
}
