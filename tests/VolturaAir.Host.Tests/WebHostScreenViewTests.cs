using System.ComponentModel;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VolturaAir.Host.Tests;

[Collection(AppPermissionSettingsCollection.Name)]
public sealed class WebHostScreenViewTests : WebHostServiceTestBase
{
    [Fact]
    public async Task ActiveScreenCanPrepareAnAuthenticatedMemoryOnlyScreenshotTransfer()
    {
        HostPermissionSet originalPermissions = AppPermissionSettings.Load();
        try
        {
            AppPermissionSettings.Save(originalPermissions with { AllowScreenViewing = true, AllowFileTransfer = true });
            var capture = new FakeScreenViewCaptureSource();
            await using var fixture = await WebHostFixture.StartAsync(screenViewCapture: capture);
            using var reconnectKey = new PairingTestKey();
            using WebSocket control = await ConnectAsync(fixture.WebHost);
            await PairAsync(control, fixture.Manager, reconnectKey);
            JsonElement status = await SendUntilTypeAsync(control, new { type = "status.get" }, "status");
            JsonElement screenshotCapability = status.GetProperty("capabilities").GetProperty("screenView").GetProperty("screenshot");
            Assert.True(screenshotCapability.GetProperty("transferPermissionGranted").GetBoolean());
            Assert.Equal("image/png", screenshotCapability.GetProperty("format").GetString());
            Assert.Equal(33_177_600, screenshotCapability.GetProperty("maxPixels").GetInt64());
            Assert.Equal(64L * 1024 * 1024, screenshotCapability.GetProperty("maxBytes").GetInt64());

            const string screenOperationId = "screen-screenshot-start";
            const string displayId = "display-1";
            JsonElement start = await StartAsync(control, reconnectKey, screenOperationId, displayId);
            const string answer = "v=0\r\no=phone 1 1 IN IP4 127.0.0.1\r\ns=answer\r\nt=0 0\r\n";
            string answerTranscript = $"VolturaAir screen-view:answer:v2:client-screen:{screenOperationId}:{displayId}:{HashSdp(start.GetProperty("offerSdp").GetString()!)}:{HashSdp(answer)}";
            JsonElement answered = await SendUntilTypeAsync(control, new
            {
                type = "screen.view.answer",
                operationId = screenOperationId,
                answerSdp = answer,
                clientSignature = reconnectKey.SignPayload(answerTranscript)
            }, "screen.view.answer.result");
            Assert.True(answered.GetProperty("succeeded").GetBoolean());
            await capture.Captured.Task.WaitAsync(TimeSpan.FromSeconds(2));

            const string transferOperationId = "screenshot-transfer-1";
            string transferTranscript = FileTransferNegotiation.ScreenCaptureStartTranscript(
                "client-screen",
                fixture.Manager.HostIdentity.PublicKey,
                transferOperationId,
                screenOperationId,
                displayId);
            JsonElement transfer = await SendUntilTypeAsync(control, new
            {
                type = "file.transfer.start",
                operationId = transferOperationId,
                direction = "download",
                source = "screen-capture",
                screenOperationId,
                displayId,
                clientSignature = reconnectKey.SignPayload(transferTranscript)
            }, "file.transfer.start.result");

            Assert.True(transfer.GetProperty("succeeded").GetBoolean());
            Assert.Equal(1, capture.ScreenshotCaptureCalls);
            Assert.Equal(displayId, capture.LastScreenshotSourceId);
            Assert.NotNull(transfer.GetProperty("transferId").GetString());

            JsonElement replay = await SendUntilTypeAsync(control, new
            {
                type = "file.transfer.start",
                operationId = transferOperationId,
                direction = "download",
                source = "screen-capture",
                screenOperationId,
                displayId,
                clientSignature = reconnectKey.SignPayload(transferTranscript)
            }, "file.transfer.start.result");

            Assert.False(replay.GetProperty("succeeded").GetBoolean());
            Assert.Equal("duplicate-request", replay.GetProperty("code").GetString());
            Assert.Equal(1, capture.ScreenshotCaptureCalls);

            const string busyOperationId = "screenshot-transfer-busy";
            string busyTranscript = FileTransferNegotiation.ScreenCaptureStartTranscript(
                "client-screen",
                fixture.Manager.HostIdentity.PublicKey,
                busyOperationId,
                screenOperationId,
                displayId);
            JsonElement busy = await SendUntilTypeAsync(control, new
            {
                type = "file.transfer.start",
                operationId = busyOperationId,
                direction = "download",
                source = "screen-capture",
                screenOperationId,
                displayId,
                clientSignature = reconnectKey.SignPayload(busyTranscript)
            }, "file.transfer.start.result");

            Assert.False(busy.GetProperty("succeeded").GetBoolean());
            Assert.Equal("busy", busy.GetProperty("code").GetString());
            Assert.Equal(1, capture.ScreenshotCaptureCalls);
        }
        finally
        {
            AppPermissionSettings.Save(originalPermissions);
        }
    }

    [Fact]
    public async Task NativeScreenshotFailureReturnsABoundedResult()
    {
        HostPermissionSet originalPermissions = AppPermissionSettings.Load();
        try
        {
            AppPermissionSettings.Save(originalPermissions with { AllowScreenViewing = true, AllowFileTransfer = true });
            var capture = new FakeScreenViewCaptureSource { ScreenshotFailure = new Win32Exception("Injected capture failure.") };
            await using var fixture = await WebHostFixture.StartAsync(screenViewCapture: capture);
            using var reconnectKey = new PairingTestKey();
            using WebSocket control = await ConnectAsync(fixture.WebHost);
            await PairAsync(control, fixture.Manager, reconnectKey);

            const string screenOperationId = "screen-screenshot-failure";
            const string displayId = "display-1";
            JsonElement start = await StartAsync(control, reconnectKey, screenOperationId, displayId);
            const string answer = "v=0\r\no=phone 1 1 IN IP4 127.0.0.1\r\ns=answer\r\nt=0 0\r\n";
            string answerTranscript = $"VolturaAir screen-view:answer:v2:client-screen:{screenOperationId}:{displayId}:{HashSdp(start.GetProperty("offerSdp").GetString()!)}:{HashSdp(answer)}";
            JsonElement answered = await SendUntilTypeAsync(control, new
            {
                type = "screen.view.answer",
                operationId = screenOperationId,
                answerSdp = answer,
                clientSignature = reconnectKey.SignPayload(answerTranscript)
            }, "screen.view.answer.result");
            Assert.True(answered.GetProperty("succeeded").GetBoolean());
            await capture.Captured.Task.WaitAsync(TimeSpan.FromSeconds(2));

            const string transferOperationId = "screenshot-transfer-failure";
            string transferTranscript = FileTransferNegotiation.ScreenCaptureStartTranscript(
                "client-screen", fixture.Manager.HostIdentity.PublicKey, transferOperationId, screenOperationId, displayId);
            JsonElement transfer = await SendUntilTypeAsync(control, new
            {
                type = "file.transfer.start",
                operationId = transferOperationId,
                direction = "download",
                source = "screen-capture",
                screenOperationId,
                displayId,
                clientSignature = reconnectKey.SignPayload(transferTranscript)
            }, "file.transfer.start.result");

            Assert.False(transfer.GetProperty("succeeded").GetBoolean());
            Assert.Equal("screenshot-failed", transfer.GetProperty("code").GetString());
        }
        finally
        {
            AppPermissionSettings.Save(originalPermissions);
        }
    }

    [Fact]
    public void ScreenshotFileNameUsesTheDisplayLabelAndLocalCaptureTime()
    {
        Assert.Equal(
            "Voltura Air - Display 1 - 2026-08-26 14-32-07.png",
            ScreenViewCoordinator.CreateScreenshotFileName(
                "Display 1",
                new DateTimeOffset(2026, 8, 26, 14, 32, 7, TimeSpan.FromHours(2))));
        Assert.Equal(
            "Voltura Air - Display-1 - 2026-08-26 14-32-07.png",
            ScreenViewCoordinator.CreateScreenshotFileName(
                "Display/1",
                new DateTimeOffset(2026, 8, 26, 14, 32, 7, TimeSpan.FromHours(2))));
    }

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
    public async Task DirectPointerRequiresTheActiveSelectedDisplayAndReleasesHeldButtonsOnStop()
    {
        HostPermissionSet originalPermissions = AppPermissionSettings.Load();
        try
        {
            AppPermissionSettings.Save(originalPermissions with { AllowScreenViewing = true, AllowRemoteInput = true });
            await using var fixture = await WebHostFixture.StartAsync(screenViewCapture: new FakeScreenViewCaptureSource());
            using var reconnectKey = new PairingTestKey();
            using WebSocket control = await ConnectAsync(fixture.WebHost);
            await PairAsync(control, fixture.Manager, reconnectKey);
            JsonElement status = await SendUntilTypeAsync(control, new { type = "status.get" }, "status");
            Assert.True(status.GetProperty("capabilities").GetProperty("screenView").GetProperty("directPointer").GetProperty("permissionGranted").GetBoolean());

            const string operationId = "screen-direct-start";
            const string displayId = "display-1";
            JsonElement start = await StartAsync(control, reconnectKey, operationId, displayId);
            string offerHash = HashSdp(start.GetProperty("offerSdp").GetString()!);
            const string answer = "v=0\r\no=phone 1 1 IN IP4 127.0.0.1\r\ns=answer\r\nt=0 0\r\n";
            string transcript = $"VolturaAir screen-view:answer:v2:client-screen:{operationId}:{displayId}:{offerHash}:{HashSdp(answer)}";
            JsonElement answered = await SendUntilTypeAsync(control, new
            {
                type = "screen.view.answer",
                operationId,
                answerSdp = answer,
                clientSignature = reconnectKey.SignPayload(transcript)
            }, "screen.view.answer.result");
            Assert.True(answered.GetProperty("succeeded").GetBoolean());

            JsonElement moveAck = await SendUntilTypeAsync(control, new
            {
                type = "screen.pointer.move",
                seq = 41,
                displayId,
                x = 0.5,
                y = 0.5
            }, "input.ack");
            Assert.Equal(41, moveAck.GetProperty("seq").GetInt32());
            Assert.Contains(fixture.InputInjector.Events, entry => entry.StartsWith("MoveMouseAbsolute:", StringComparison.Ordinal));

            JsonElement stale = await SendUntilTypeAsync(control, new
            {
                type = "screen.pointer.button",
                seq = 42,
                displayId = "display-2",
                x = 0.5,
                y = 0.5,
                button = "left",
                action = "down"
            }, "input.error");
            Assert.Equal("VAIR-SCREEN-STALE-DISPLAY", stale.GetProperty("code").GetString());

            _ = await SendUntilTypeAsync(control, new
            {
                type = "screen.pointer.button",
                seq = 43,
                displayId,
                x = 0.5,
                y = 0.5,
                button = "left",
                action = "down"
            }, "input.ack");
            _ = await SendUntilTypeAsync(control, new { type = "screen.view.stop", operationId = "screen-direct-stop" }, "screen.view.stop.result");
            Assert.Contains("ReleaseMouseButtons", fixture.InputInjector.Events);

            JsonElement finalStatus = await SendUntilTypeAsync(control, new { type = "status.get" }, "status");
            Assert.True(finalStatus.GetProperty("connected").GetBoolean());
        }
        finally
        {
            AppPermissionSettings.Save(originalPermissions);
        }
    }

    [Fact]
    public async Task DirectPointerRejectsUndeclaredFieldsOnTheAuthenticatedSocket()
    {
        HostPermissionSet originalPermissions = AppPermissionSettings.Load();
        try
        {
            AppPermissionSettings.Save(originalPermissions with { AllowScreenViewing = true, AllowRemoteInput = true });
            await using var fixture = await WebHostFixture.StartAsync(screenViewCapture: new FakeScreenViewCaptureSource());
            using var reconnectKey = new PairingTestKey();
            using WebSocket control = await ConnectAsync(fixture.WebHost);
            await PairAsync(control, fixture.Manager, reconnectKey);

            await SendAsync(control, new
            {
                type = "screen.pointer.wheel",
                displayId = "display-1",
                x = 0.5,
                y = 0.5,
                dx = 0,
                dy = 1,
                extra = true
            });

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            Assert.Equal(WebSocketCloseStatus.PolicyViolation, await ReceiveCloseStatusAsync(control, timeout.Token));
            Assert.DoesNotContain(fixture.InputInjector.Events, entry => entry.StartsWith("ScrollAt:", StringComparison.Ordinal));
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
        public int ScreenshotCaptureCalls { get; private set; }
        public string? LastScreenshotSourceId { get; private set; }
        public IReadOnlyList<ScreenViewSource> Sources { get; set; } = DefaultSources;
        public ScreenViewCaptureException? DiscoveryFailure { get; set; }
        public Exception? ScreenshotFailure { get; set; }
        public IReadOnlyList<ScreenViewSource> GetSources() =>
            DiscoveryFailure is null ? Sources : throw DiscoveryFailure;
        public Task<ScreenViewEncodedFrame?> CaptureVideoAsync(string sourceId, ScreenViewCaptureProfile profile, int bitrate, bool forceKeyFrame, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Captured.TrySetResult();
            return Task.FromResult<ScreenViewEncodedFrame?>(new([0, 0, 0, 1, 0x65, 1], 800, 600, 30, true));
        }
        public ScreenViewScreenshot CaptureScreenshot(string sourceId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ScreenshotCaptureCalls++;
            LastScreenshotSourceId = sourceId;
            if (ScreenshotFailure is not null) throw ScreenshotFailure;
            var content = new MemoryStream([137, 80, 78, 71], writable: false);
            return new(content, content.Length, 800, 600);
        }
        public void EndCapture() => EndCaptureCalls++;
    }

}
