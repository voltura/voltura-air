using System.Net.WebSockets;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VolturaAir.Host.Features.PhoneWebcam;

namespace VolturaAir.Host.Tests;

[Collection(AppPermissionSettingsCollection.Name)]
public sealed class WebHostPhoneWebcamTests : WebHostServiceTestBase
{
    [Fact]
    public void HighRelayCeilingDoesNotExtendThePublishedPhoneWebcamQualityEnum()
    {
        Assert.Null(PhoneWebcamCoordinator.ToWireRelayQuality(RelayScreenQuality.High));
        Assert.Equal(RelayScreenQuality.Standard, PhoneWebcamCoordinator.ToWireRelayQuality(RelayScreenQuality.Standard));
        Assert.Equal(RelayScreenQuality.DataSaver, PhoneWebcamCoordinator.ToWireRelayQuality(RelayScreenQuality.DataSaver));
    }

    [Fact]
    public async Task FramePipeStartupFailureDisablesCapabilityAndSessionAdmission()
    {
        HostPermissionSet originalPermissions = AppPermissionSettings.Load();
        try
        {
            AppPermissionSettings.Save(originalPermissions with { AllowPhoneWebcam = true });
            await using var feature = new PhoneWebcamFeature(
                new SuccessfulPhoneWebcamSetup(),
                static () => new PhoneWebcamFramePipeServer(
                    static () => throw new IOException("Injected initial pipe bind failure.")));
            PhoneWebcamFeatureStatus failed = await feature.EnableAsync();
            Assert.True(failed.HasError);
            Assert.False(failed.IsInstalled);
            var peerFactory = new QueuePhoneWebcamPeerFactory();
            await using var fixture = await WebHostFixture.StartAsync(
                phoneWebcamFeature: feature,
                phoneWebcamPeerFactory: peerFactory);
            using var reconnectKey = new PairingTestKey();
            using WebSocket control = await ConnectAsync(fixture.WebHost);
            await PairAsync(control, fixture.Manager, reconnectKey);

            JsonElement status = await SendUntilTypeAsync(control, new { type = "status.get" }, "status");
            JsonElement capability = status.GetProperty("capabilities").GetProperty("phoneWebcam");
            Assert.False(capability.GetProperty("enabled").GetBoolean());
            Assert.False(capability.GetProperty("canUse").GetBoolean());

            const string operationId = "pipe-failure-start";
            string transcript = PhoneWebcamCoordinator.StartTranscript("client-webcam", operationId, 1920, 1080, 30, false);
            JsonElement start = await SendUntilTypeAsync(control, new
            {
                type = "phone.webcam.start",
                operationId,
                captureWidth = 1920,
                captureHeight = 1080,
                captureFps = 30,
                useMicrophone = false,
                clientSignature = reconnectKey.SignPayload(transcript)
            }, "phone.webcam.start.result");
            Assert.False(start.GetProperty("succeeded").GetBoolean());
            Assert.Equal("permission-denied", start.GetProperty("code").GetString());
            Assert.Equal(0, peerFactory.CreateCount);
        }
        finally
        {
            AppPermissionSettings.Save(originalPermissions);
        }
    }

    [Fact]
    public async Task PairedDeviceNegotiatesVideoOnlySessionAndStopDisposesThePeer()
    {
        HostPermissionSet originalPermissions = AppPermissionSettings.Load();
        try
        {
            AppPermissionSettings.Save(originalPermissions with { AllowPhoneWebcam = true });
            var peer = new FakePhoneWebcamPeer();
            await using var fixture = await WebHostFixture.StartAsync(
                phoneWebcamFeature: new InstalledPhoneWebcamFeature(),
                phoneWebcamPeerFactory: new FakePhoneWebcamPeerFactory(peer));
            using var reconnectKey = new PairingTestKey();
            using WebSocket control = await ConnectAsync(fixture.WebHost);
            await PairAsync(control, fixture.Manager, reconnectKey);

            JsonElement status = await SendUntilTypeAsync(control, new { type = "status.get" }, "status");
            JsonElement capability = status.GetProperty("capabilities").GetProperty("phoneWebcam");
            Assert.True(capability.GetProperty("enabled").GetBoolean());
            Assert.True(capability.GetProperty("permissionGranted").GetBoolean());
            Assert.True(capability.GetProperty("canUse").GetBoolean());
            Assert.False(capability.GetProperty("microphoneAvailable").GetBoolean());

            const string operationId = "webcam-start-1";
            string startTranscript = PhoneWebcamCoordinator.StartTranscript("client-webcam", operationId, 1920, 1080, 30, false);
            JsonElement start = await SendUntilTypeAsync(control, new
            {
                type = "phone.webcam.start",
                operationId,
                captureWidth = 1920,
                captureHeight = 1080,
                captureFps = 30,
                useMicrophone = false,
                clientSignature = reconnectKey.SignPayload(startTranscript)
            }, "phone.webcam.start.result");
            Assert.True(start.GetProperty("succeeded").GetBoolean());
            Assert.Equal(FakePhoneWebcamPeer.Offer, start.GetProperty("offerSdp").GetString());

            string offerHash = HashSdp(FakePhoneWebcamPeer.Offer);
            string answerHash = HashSdp(FakePhoneWebcamPeer.Answer);
            string answerTranscript = PhoneWebcamCoordinator.AnswerTranscript("client-webcam", operationId, offerHash, answerHash);
            JsonElement answer = await SendUntilTypeAsync(control, new
            {
                type = "phone.webcam.answer",
                operationId,
                answerSdp = FakePhoneWebcamPeer.Answer,
                clientSignature = reconnectKey.SignPayload(answerTranscript)
            }, "phone.webcam.answer.result");
            Assert.True(answer.GetProperty("succeeded").GetBoolean());
            Assert.Equal(FakePhoneWebcamPeer.Answer, peer.AppliedAnswer);

            JsonElement stopped = await SendUntilTypeAsync(control, new
            {
                type = "phone.webcam.stop",
                operationId = "webcam-stop-1"
            }, "phone.webcam.stop.result");
            Assert.True(stopped.GetProperty("succeeded").GetBoolean());
            Assert.True(peer.Disposed);

            JsonElement statusAfterStop = await SendUntilTypeAsync(control, new { type = "status.get" }, "status");
            Assert.True(statusAfterStop.GetProperty("connected").GetBoolean());
            Assert.True(statusAfterStop.GetProperty("capabilities").GetProperty("phoneWebcam").GetProperty("canUse").GetBoolean());
        }
        finally
        {
            AppPermissionSettings.Save(originalPermissions);
        }
    }

    [Fact]
    public async Task FailureBeforeActivePublicationRejectsTheAnswerAndReleasesTheSession()
    {
        HostPermissionSet originalPermissions = AppPermissionSettings.Load();
        try
        {
            AppPermissionSettings.Save(originalPermissions with { AllowPhoneWebcam = true });
            var failedPeer = new FakePhoneWebcamPeer(stopWhenSubscribed: true);
            var replacementPeer = new FakePhoneWebcamPeer();
            await using var fixture = await WebHostFixture.StartAsync(
                phoneWebcamFeature: new InstalledPhoneWebcamFeature(),
                phoneWebcamPeerFactory: new QueuePhoneWebcamPeerFactory(failedPeer, replacementPeer));
            using var reconnectKey = new PairingTestKey();
            using WebSocket control = await ConnectAsync(fixture.WebHost);
            await PairAsync(control, fixture.Manager, reconnectKey);

            const string operationId = "webcam-startup-failure";
            JsonElement start = await StartAsync(control, reconnectKey, operationId);
            string offerHash = HashSdp(start.GetProperty("offerSdp").GetString()!);
            JsonElement answer = await SendUntilTypeAsync(control, new
            {
                type = "phone.webcam.answer",
                operationId,
                answerSdp = FakePhoneWebcamPeer.Answer,
                clientSignature = reconnectKey.SignPayload(PhoneWebcamCoordinator.AnswerTranscript(
                    "client-webcam", operationId, offerHash, HashSdp(FakePhoneWebcamPeer.Answer)))
            }, "phone.webcam.answer.result");

            Assert.False(answer.GetProperty("succeeded").GetBoolean());
            Assert.Equal("invalid-answer", answer.GetProperty("code").GetString());
            Assert.True(failedPeer.Disposed);
            Assert.True((await StartAsync(control, reconnectKey, "webcam-after-startup-failure"))
                .GetProperty("succeeded").GetBoolean());
        }
        finally
        {
            AppPermissionSettings.Save(originalPermissions);
        }
    }

    [Fact]
    public async Task StopKeepsTheAuthenticatedControlPathResponsiveWhileMediaCleanupCompletes()
    {
        HostPermissionSet originalPermissions = AppPermissionSettings.Load();
        var disposeEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseDispose = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            AppPermissionSettings.Save(originalPermissions with { AllowPhoneWebcam = true });
            var peer = new FakePhoneWebcamPeer(dispose: async () =>
            {
                disposeEntered.TrySetResult();
                await releaseDispose.Task;
            });
            await using var fixture = await WebHostFixture.StartAsync(
                phoneWebcamFeature: new InstalledPhoneWebcamFeature(),
                phoneWebcamPeerFactory: new FakePhoneWebcamPeerFactory(peer));
            using var reconnectKey = new PairingTestKey();
            using WebSocket control = await ConnectAsync(fixture.WebHost);
            await PairAsync(control, fixture.Manager, reconnectKey);
            await CompleteSessionAsync(control, reconnectKey, "webcam-nonblocking-stop");

            await SendAsync(control, new
            {
                type = "phone.webcam.stop",
                operationId = "webcam-nonblocking-stop-request"
            });
            await disposeEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

            JsonElement status = await SendUntilTypeAsync(control, new { type = "status.get" }, "status");
            Assert.True(status.GetProperty("connected").GetBoolean());
            Assert.False(peer.Disposed);

            releaseDispose.TrySetResult();
            JsonElement stopped = await ReceiveUntilTypeAsync(control, "phone.webcam.stop.result");
            Assert.True(stopped.GetProperty("succeeded").GetBoolean());
            Assert.True(peer.Disposed);
        }
        finally
        {
            releaseDispose.TrySetResult();
            AppPermissionSettings.Save(originalPermissions);
        }
    }

    [Fact]
    public async Task PermissionRevocationStopsTheActiveSession()
    {
        HostPermissionSet originalPermissions = AppPermissionSettings.Load();
        try
        {
            AppPermissionSettings.Save(originalPermissions with { AllowPhoneWebcam = true });
            var peer = new FakePhoneWebcamPeer();
            await using var fixture = await WebHostFixture.StartAsync(
                phoneWebcamFeature: new InstalledPhoneWebcamFeature(),
                phoneWebcamPeerFactory: new FakePhoneWebcamPeerFactory(peer));
            using var reconnectKey = new PairingTestKey();
            using WebSocket control = await ConnectAsync(fixture.WebHost);
            await PairAsync(control, fixture.Manager, reconnectKey);
            const string operationId = "webcam-revoke-1";
            JsonElement start = await SendUntilTypeAsync(control, new
            {
                type = "phone.webcam.start",
                operationId,
                captureWidth = 1920,
                captureHeight = 1080,
                captureFps = 30,
                useMicrophone = false,
                clientSignature = reconnectKey.SignPayload(PhoneWebcamCoordinator.StartTranscript("client-webcam", operationId, 1920, 1080, 30, false))
            }, "phone.webcam.start.result");
            string offerHash = HashSdp(start.GetProperty("offerSdp").GetString()!);
            _ = await SendUntilTypeAsync(control, new
            {
                type = "phone.webcam.answer",
                operationId,
                answerSdp = FakePhoneWebcamPeer.Answer,
                clientSignature = reconnectKey.SignPayload(PhoneWebcamCoordinator.AnswerTranscript("client-webcam", operationId, offerHash, HashSdp(FakePhoneWebcamPeer.Answer)))
            }, "phone.webcam.answer.result");

            fixture.Manager.SetDevicePermission("client-webcam", DevicePermissionKind.PhoneWebcam, false);
            JsonElement ended = await ReceiveUntilTypeAsync(control, "phone.webcam.ended");
            Assert.Equal("permission-revoked", ended.GetProperty("reason").GetString());
            Assert.True(peer.Disposed);
        }
        finally
        {
            AppPermissionSettings.Save(originalPermissions);
        }
    }

    [Fact]
    public async Task InvalidStartProofIsRejectedBeforeCreatingNativeWebRtcState()
    {
        HostPermissionSet originalPermissions = AppPermissionSettings.Load();
        try
        {
            AppPermissionSettings.Save(originalPermissions with { AllowPhoneWebcam = true });
            var factory = new QueuePhoneWebcamPeerFactory();
            await using var fixture = await WebHostFixture.StartAsync(
                phoneWebcamFeature: new InstalledPhoneWebcamFeature(),
                phoneWebcamPeerFactory: factory);
            using var reconnectKey = new PairingTestKey();
            using WebSocket control = await ConnectAsync(fixture.WebHost);
            await PairAsync(control, fixture.Manager, reconnectKey);

            JsonElement result = await SendUntilTypeAsync(control, new
            {
                type = "phone.webcam.start",
                operationId = "webcam-invalid-proof",
                captureWidth = 1920,
                captureHeight = 1080,
                captureFps = 30,
                useMicrophone = false,
                clientSignature = "invalid"
            }, "phone.webcam.start.result");

            Assert.False(result.GetProperty("succeeded").GetBoolean());
            Assert.Equal("invalid-proof", result.GetProperty("code").GetString());
            Assert.Equal(0, factory.CreateCount);
        }
        finally
        {
            AppPermissionSettings.Save(originalPermissions);
        }
    }

    [Fact]
    public async Task SecondStartIsRejectedWithoutReplacingTheOriginalPendingSession()
    {
        HostPermissionSet originalPermissions = AppPermissionSettings.Load();
        try
        {
            AppPermissionSettings.Save(originalPermissions with { AllowPhoneWebcam = true });
            var firstPeer = new FakePhoneWebcamPeer();
            var rejectedPeer = new FakePhoneWebcamPeer();
            var factory = new QueuePhoneWebcamPeerFactory(firstPeer, rejectedPeer);
            await using var fixture = await WebHostFixture.StartAsync(
                phoneWebcamFeature: new InstalledPhoneWebcamFeature(),
                phoneWebcamPeerFactory: factory);
            using var reconnectKey = new PairingTestKey();
            using WebSocket control = await ConnectAsync(fixture.WebHost);
            await PairAsync(control, fixture.Manager, reconnectKey);

            JsonElement first = await StartAsync(control, reconnectKey, "webcam-first");
            JsonElement second = await StartAsync(control, reconnectKey, "webcam-second");

            Assert.True(first.GetProperty("succeeded").GetBoolean());
            Assert.False(second.GetProperty("succeeded").GetBoolean());
            Assert.Equal("busy", second.GetProperty("code").GetString());
            Assert.False(firstPeer.Disposed);
            Assert.True(rejectedPeer.Disposed);
        }
        finally
        {
            AppPermissionSettings.Save(originalPermissions);
        }
    }

    [Fact]
    public async Task InvalidAnswerProofDisposesThePendingNativePeer()
    {
        HostPermissionSet originalPermissions = AppPermissionSettings.Load();
        try
        {
            AppPermissionSettings.Save(originalPermissions with { AllowPhoneWebcam = true });
            var peer = new FakePhoneWebcamPeer();
            await using var fixture = await WebHostFixture.StartAsync(
                phoneWebcamFeature: new InstalledPhoneWebcamFeature(),
                phoneWebcamPeerFactory: new FakePhoneWebcamPeerFactory(peer));
            using var reconnectKey = new PairingTestKey();
            using WebSocket control = await ConnectAsync(fixture.WebHost);
            await PairAsync(control, fixture.Manager, reconnectKey);
            const string operationId = "webcam-invalid-answer-proof";
            JsonElement started = await StartAsync(control, reconnectKey, operationId);
            Assert.True(started.GetProperty("succeeded").GetBoolean());

            JsonElement answer = await SendUntilTypeAsync(control, new
            {
                type = "phone.webcam.answer",
                operationId,
                answerSdp = FakePhoneWebcamPeer.Answer,
                clientSignature = "invalid"
            }, "phone.webcam.answer.result");

            Assert.False(answer.GetProperty("succeeded").GetBoolean());
            Assert.Equal("invalid-proof", answer.GetProperty("code").GetString());
            Assert.True(peer.Disposed);
        }
        finally
        {
            AppPermissionSettings.Save(originalPermissions);
        }
    }

    [Fact]
    public async Task DisconnectingAnOlderSocketDoesNotStopTheNewerOwnedSession()
    {
        HostPermissionSet originalPermissions = AppPermissionSettings.Load();
        try
        {
            AppPermissionSettings.Save(originalPermissions with { AllowPhoneWebcam = true });
            var peer = new FakePhoneWebcamPeer();
            await using var fixture = await WebHostFixture.StartAsync(
                phoneWebcamFeature: new InstalledPhoneWebcamFeature(),
                phoneWebcamPeerFactory: new FakePhoneWebcamPeerFactory(peer));
            using var reconnectKey = new PairingTestKey();
            using WebSocket older = await ConnectAsync(fixture.WebHost);
            await PairAsync(older, fixture.Manager, reconnectKey);
            using WebSocket newer = await ConnectAsync(fixture.WebHost);
            await ReconnectAsync(newer, reconnectKey);

            const string operationId = "webcam-newer-socket";
            JsonElement start = await StartAsync(newer, reconnectKey, operationId);
            string offerHash = HashSdp(start.GetProperty("offerSdp").GetString()!);
            JsonElement answer = await SendUntilTypeAsync(newer, new
            {
                type = "phone.webcam.answer",
                operationId,
                answerSdp = FakePhoneWebcamPeer.Answer,
                clientSignature = reconnectKey.SignPayload(PhoneWebcamCoordinator.AnswerTranscript(
                    "client-webcam",
                    operationId,
                    offerHash,
                    HashSdp(FakePhoneWebcamPeer.Answer)))
            }, "phone.webcam.answer.result");
            Assert.True(answer.GetProperty("succeeded").GetBoolean());

            await older.CloseAsync(WebSocketCloseStatus.NormalClosure, "older socket closed", CancellationToken.None);
            await Task.Delay(100);
            Assert.False(peer.Disposed);

            JsonElement stopped = await SendUntilTypeAsync(newer, new
            {
                type = "phone.webcam.stop",
                operationId = "webcam-newer-stop"
            }, "phone.webcam.stop.result");
            Assert.True(stopped.GetProperty("succeeded").GetBoolean());
            Assert.True(peer.Disposed);
        }
        finally
        {
            AppPermissionSettings.Save(originalPermissions);
        }
    }

    [Fact]
    public async Task PermissionStopDuringAnswerWaitsForNativePeerUseToFinish()
    {
        HostPermissionSet originalPermissions = AppPermissionSettings.Load();
        using var applyEntered = new ManualResetEventSlim();
        using var continueApply = new ManualResetEventSlim();
        try
        {
            AppPermissionSettings.Save(originalPermissions with { AllowPhoneWebcam = true });
            var peer = new FakePhoneWebcamPeer(applyAnswer: _ =>
            {
                applyEntered.Set();
                continueApply.Wait(TimeSpan.FromSeconds(5));
            });
            await using var fixture = await WebHostFixture.StartAsync(
                phoneWebcamFeature: new InstalledPhoneWebcamFeature(),
                phoneWebcamPeerFactory: new FakePhoneWebcamPeerFactory(peer));
            using var reconnectKey = new PairingTestKey();
            using WebSocket control = await ConnectAsync(fixture.WebHost);
            await PairAsync(control, fixture.Manager, reconnectKey);
            const string operationId = "webcam-answer-race";
            JsonElement start = await StartAsync(control, reconnectKey, operationId);
            string offerHash = HashSdp(start.GetProperty("offerSdp").GetString()!);
            Task<JsonElement> answerTask = SendUntilTypeAsync(control, new
            {
                type = "phone.webcam.answer",
                operationId,
                answerSdp = FakePhoneWebcamPeer.Answer,
                clientSignature = reconnectKey.SignPayload(PhoneWebcamCoordinator.AnswerTranscript(
                    "client-webcam", operationId, offerHash, HashSdp(FakePhoneWebcamPeer.Answer)))
            }, "phone.webcam.answer.result");
            Assert.True(applyEntered.Wait(TimeSpan.FromSeconds(2)));

            fixture.Manager.SetDevicePermission("client-webcam", DevicePermissionKind.PhoneWebcam, false);
            await Task.Delay(100);
            Assert.False(peer.Disposed);
            continueApply.Set();

            JsonElement answer = await answerTask;
            Assert.False(answer.GetProperty("succeeded").GetBoolean());
            await WaitUntilAsync(() => peer.Disposed);
        }
        finally
        {
            continueApply.Set();
            AppPermissionSettings.Save(originalPermissions);
        }
    }

    [Fact]
    public async Task DelayedOldTerminalEventIsCorrelatedAndCannotTargetTheReplacementOperation()
    {
        HostPermissionSet originalPermissions = AppPermissionSettings.Load();
        var releaseOldDispose = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            AppPermissionSettings.Save(originalPermissions with { AllowPhoneWebcam = true });
            var oldPeer = new FakePhoneWebcamPeer(dispose: async () => await releaseOldDispose.Task);
            var newPeer = new FakePhoneWebcamPeer();
            await using var feature = CreateInstalledFeature();
            await using var fixture = await WebHostFixture.StartAsync(
                phoneWebcamFeature: feature,
                phoneWebcamPeerFactory: new QueuePhoneWebcamPeerFactory(oldPeer, newPeer));
            using var reconnectKey = new PairingTestKey();
            using WebSocket control = await ConnectAsync(fixture.WebHost);
            await PairAsync(control, fixture.Manager, reconnectKey);
            await CompleteSessionAsync(control, reconnectKey, "webcam-old");

            oldPeer.Stop();
            await CompleteSessionAsync(control, reconnectKey, "webcam-new");
            string replacementActivity = feature.Activity.State;
            Assert.Equal("connecting", replacementActivity);
            releaseOldDispose.TrySetResult();

            JsonElement ended = await ReceiveUntilTypeAsync(control, "phone.webcam.ended");
            Assert.Equal("webcam-old", ended.GetProperty("operationId").GetString());
            Assert.False(newPeer.Disposed);
            Assert.Equal(replacementActivity, feature.Activity.State);
        }
        finally
        {
            releaseOldDispose.TrySetResult();
            AppPermissionSettings.Save(originalPermissions);
        }
    }

    [Fact]
    public async Task RemovalRejectsNewSessionsBeforeWaitingForNativeCleanup()
    {
        HostPermissionSet originalPermissions = AppPermissionSettings.Load();
        var setup = new BlockingRemovalPhoneWebcamSetup();
        try
        {
            AppPermissionSettings.Save(originalPermissions with { AllowPhoneWebcam = true });
            await using var feature = CreateInstalledFeature(setup);
            var peerFactory = new QueuePhoneWebcamPeerFactory();
            await using var fixture = await WebHostFixture.StartAsync(
                phoneWebcamFeature: feature,
                phoneWebcamPeerFactory: peerFactory);
            using var reconnectKey = new PairingTestKey();
            using WebSocket control = await ConnectAsync(fixture.WebHost);
            await PairAsync(control, fixture.Manager, reconnectKey);

            Task<PhoneWebcamFeatureStatus> removal = feature.RemoveAsync();
            await setup.RemoveEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            JsonElement start = await StartAsync(control, reconnectKey, "webcam-during-remove");

            Assert.False(start.GetProperty("succeeded").GetBoolean());
            Assert.Equal("permission-denied", start.GetProperty("code").GetString());
            Assert.Equal(0, peerFactory.CreateCount);
            setup.CompleteRemove();
            Assert.Equal(PhoneWebcamFeatureState.NotInstalled, (await removal).State);
        }
        finally
        {
            setup.CompleteRemove();
            AppPermissionSettings.Save(originalPermissions);
        }
    }

    private static Task<JsonElement> StartAsync(WebSocket control, PairingTestKey reconnectKey, string operationId) =>
        SendUntilTypeAsync(control, new
        {
            type = "phone.webcam.start",
            operationId,
            captureWidth = 1920,
            captureHeight = 1080,
            captureFps = 30,
            useMicrophone = false,
            clientSignature = reconnectKey.SignPayload(PhoneWebcamCoordinator.StartTranscript("client-webcam", operationId, 1920, 1080, 30, false))
        }, "phone.webcam.start.result");

    private static async Task CompleteSessionAsync(WebSocket control, PairingTestKey reconnectKey, string operationId)
    {
        JsonElement start = await StartAsync(control, reconnectKey, operationId);
        string offerHash = HashSdp(start.GetProperty("offerSdp").GetString()!);
        JsonElement answer = await SendUntilTypeAsync(control, new
        {
            type = "phone.webcam.answer",
            operationId,
            answerSdp = FakePhoneWebcamPeer.Answer,
            clientSignature = reconnectKey.SignPayload(PhoneWebcamCoordinator.AnswerTranscript(
                "client-webcam", operationId, offerHash, HashSdp(FakePhoneWebcamPeer.Answer)))
        }, "phone.webcam.answer.result");
        Assert.True(answer.GetProperty("succeeded").GetBoolean());
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private static async Task PairAsync(WebSocket control, PairingManager manager, PairingTestKey key)
    {
        JsonElement accepted = await SendAndReceiveAsync(control, new
        {
            type = "pair.hello",
            clientId = "client-webcam",
            deviceName = "Webcam test phone",
            pairToken = manager.CreatePairingToken(),
            reconnectPublicKey = key.PublicKey
        });
        Assert.Equal("pair.accepted", accepted.GetProperty("type").GetString());
        manager.SetDevicePermissionOverrides(
            "client-webcam",
            DeviceAccessProfiles.ToCompleteOverrides(AppPermissionSettings.Load()));
        using var status = JsonDocument.Parse(await ReceiveTextAsync(control));
        Assert.Equal("status", status.RootElement.GetProperty("type").GetString());
    }

    private static async Task ReconnectAsync(WebSocket control, PairingTestKey key)
    {
        JsonElement challenge = await SendAndReceiveAsync(control, new
        {
            type = "pair.hello",
            clientId = "client-webcam",
            deviceName = "Webcam test phone"
        });
        Assert.Equal("pair.challenge", challenge.GetProperty("type").GetString());
        JsonElement accepted = await SendAndReceiveAsync(control, new
        {
            type = "pair.proof",
            clientId = "client-webcam",
            signature = key.SignReconnectChallenge("client-webcam", challenge.GetProperty("challenge").GetString()!)
        });
        Assert.Equal("pair.accepted", accepted.GetProperty("type").GetString());
    }

    private static async Task<JsonElement> SendUntilTypeAsync(WebSocket socket, object payload, string expectedType)
    {
        await SendAsync(socket, payload);
        return await ReceiveUntilTypeAsync(socket, expectedType);
    }

    private static async Task<JsonElement> ReceiveUntilTypeAsync(WebSocket socket, string expectedType)
    {
        for (int attempt = 0; attempt < 8; attempt++)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            string text = await ReceiveTextAsync(socket, timeout.Token);
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.GetProperty("type").GetString() == expectedType)
            {
                return document.RootElement.Clone();
            }
        }
        throw new InvalidOperationException($"The host did not send {expectedType}.");
    }

    private static string HashSdp(string sdp) =>
        ScreenViewHostIdentity.Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(sdp)));

    private sealed class InstalledPhoneWebcamFeature : IPhoneWebcamFeature
    {
        public PhoneWebcamFeatureStatus Status { get; } = new(PhoneWebcamFeatureState.Installed, "Installed.");
        public PhoneWebcamActivity Activity { get; } = new("idle");
        public event EventHandler? ActivityChanged { add { } remove { } }
        public event EventHandler? StatusChanged { add { } remove { } }
        public Task<PhoneWebcamFeatureStatus> EnableAsync(CancellationToken cancellationToken = default) => Task.FromResult(Status);
        public Task<PhoneWebcamFeatureStatus> RemoveAsync(CancellationToken cancellationToken = default) => Task.FromResult(Status);
        public void Publish(PhoneWebcamFrame frame) => frame.Dispose();
    }

    private sealed class SuccessfulPhoneWebcamSetup : IPhoneWebcamSetup
    {
        private static readonly PhoneWebcamFeatureStatus Installed = new(
            PhoneWebcamFeatureState.Installed,
            "Installed.");

        public Task<PhoneWebcamFeatureStatus> GetStatusAsync(CancellationToken cancellationToken) => Task.FromResult(Installed);
        public Task<PhoneWebcamFeatureStatus> InstallAsync(CancellationToken cancellationToken) => Task.FromResult(Installed);
        public Task<PhoneWebcamFeatureStatus> RemoveAsync(CancellationToken cancellationToken) => Task.FromResult(Installed);
    }

    private static PhoneWebcamFeature CreateInstalledFeature(IPhoneWebcamSetup? setup = null)
    {
        string pipeName = $"voltura-air-webcam-test-{Guid.NewGuid():N}";
        var feature = new PhoneWebcamFeature(
            setup ?? new SuccessfulPhoneWebcamSetup(),
            () => new PhoneWebcamFramePipeServer(() => new NamedPipeServerStream(
                pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous)));
        PhoneWebcamFeatureStatus status = feature.EnableAsync().GetAwaiter().GetResult();
        Assert.True(status.IsInstalled);
        return feature;
    }

    private sealed class BlockingRemovalPhoneWebcamSetup : IPhoneWebcamSetup
    {
        private readonly TaskCompletionSource _removeCompleted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource RemoveEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<PhoneWebcamFeatureStatus> GetStatusAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new PhoneWebcamFeatureStatus(PhoneWebcamFeatureState.Installed, "Installed."));

        public Task<PhoneWebcamFeatureStatus> InstallAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new PhoneWebcamFeatureStatus(PhoneWebcamFeatureState.Installed, "Installed."));

        public async Task<PhoneWebcamFeatureStatus> RemoveAsync(CancellationToken cancellationToken)
        {
            RemoveEntered.TrySetResult();
            await _removeCompleted.Task.WaitAsync(cancellationToken);
            return new PhoneWebcamFeatureStatus(PhoneWebcamFeatureState.NotInstalled, "Not installed.");
        }

        internal void CompleteRemove() => _removeCompleted.TrySetResult();
    }

    private sealed class FakePhoneWebcamPeerFactory(FakePhoneWebcamPeer peer) : IPhoneWebcamWebRtcPeerFactory
    {
        public IPhoneWebcamWebRtcPeer Create(RelayTurnConfiguration? relay) => peer;
    }

    private sealed class QueuePhoneWebcamPeerFactory(params FakePhoneWebcamPeer[] peers) : IPhoneWebcamWebRtcPeerFactory
    {
        private readonly Queue<FakePhoneWebcamPeer> _peers = new(peers);
        internal int CreateCount { get; private set; }

        public IPhoneWebcamWebRtcPeer Create(RelayTurnConfiguration? relay)
        {
            CreateCount++;
            return _peers.Count > 0 ? _peers.Dequeue() : new FakePhoneWebcamPeer();
        }
    }

    private sealed class FakePhoneWebcamPeer(
        Action<string>? applyAnswer = null,
        Func<ValueTask>? dispose = null,
        bool stopWhenSubscribed = false) : IPhoneWebcamWebRtcPeer
    {
        internal const string Offer = "v=0\r\nm=video 9 UDP/TLS/RTP/SAVPF 96\r\na=rtpmap:96 H264/90000\r\n";
        internal const string Answer = "v=0\r\nm=video 9 UDP/TLS/RTP/SAVPF 96\r\na=rtpmap:96 H264/90000\r\n";
        public event Action<byte[], uint>? AccessUnitReceived { add { } remove { } }
        private EventHandler? _stopped;
        public event EventHandler? Stopped
        {
            add
            {
                _stopped += value;
                if (stopWhenSubscribed) value?.Invoke(this, EventArgs.Empty);
            }
            remove => _stopped -= value;
        }
        public Task TrackOpen => Task.CompletedTask;
        internal bool Disposed { get; private set; }
        internal string? AppliedAnswer { get; private set; }
        public Task<string> CreateOfferAsync(CancellationToken cancellationToken) => Task.FromResult(Offer);
        public void ApplyAnswer(string answerSdp) { applyAnswer?.Invoke(answerSdp); AppliedAnswer = answerSdp; }
        public void RequestKeyFrame() { }
        internal void Stop() => _stopped?.Invoke(this, EventArgs.Empty);
        public async ValueTask DisposeAsync()
        {
            if (dispose is not null)
            {
                await dispose();
            }
            Disposed = true;
        }
    }
}
