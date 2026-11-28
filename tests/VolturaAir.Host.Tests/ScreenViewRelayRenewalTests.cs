using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;

namespace VolturaAir.Host.Tests;

[Collection(AppPermissionSettingsCollection.Name)]
public sealed class ScreenViewRelayRenewalTests : IsolatedHostSettingsTest
{
    [Fact]
    public async Task ReplacementSocketOwnsRenewalBeforeOldSocketDisconnects()
    {
        await using var fixture = new Fixture(false);
        using var oldSocket = CreateSocket();
        using var replacementSocket = CreateSocket();
        await fixture.StartAsync("view", owner: oldSocket);
        await WaitUntilAsync(() => !fixture.Peers.Peers[0].Frames.IsEmpty);

        Assert.True((await fixture.OfferAsync("renew", "view", owner: replacementSocket)).Succeeded);
        Assert.False(fixture.Coordinator.StopConnection("viewer", oldSocket));
        Assert.True(fixture.Answer("renew", owner: replacementSocket).Succeeded);
        fixture.Peers.Peers[^1].Connection.TrySetResult();
        await WaitUntilAsync(() => !fixture.Peers.Peers[^1].Frames.IsEmpty);

        Assert.False(fixture.Peers.Peers[^1].Disposed);
        Assert.True(fixture.Coordinator.StopConnection("viewer", replacementSocket));
        await WaitUntilAsync(() => fixture.Peers.Peers.All(peer => peer.Disposed));
    }

    [Fact]
    public async Task DisconnectClaimsTheViewBeforeWaitingToCancelNativeWork()
    {
        await using var fixture = new Fixture(false);
        using var originalSocket = CreateSocket();
        using var replacementSocket = CreateSocket();
        using var releaseAudioStop = new ManualResetEventSlim();
        var audioStopEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Audio.OnCanceled = () =>
        {
            audioStopEntered.TrySetResult();
            releaseAudioStop.Wait(TimeSpan.FromSeconds(7));
        };
        await fixture.StartAsync("view", owner: originalSocket);
        await fixture.Audio.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Action audioStopped = fixture.Peers.Peers[0].CaptureAudioStopCallback();
        Task audioStop = Task.Run(audioStopped);
        try
        {
            await audioStopEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Task<bool> disconnected = Task.Run(() => fixture.Coordinator.StopConnection("viewer", originalSocket));
            await WaitUntilAsync(() => fixture.Coordinator.SetSource("viewer", "display-1").Code == "not-viewing");
            Task<ScreenViewStartResult> renewal = fixture.OfferAsync("renew", "view", owner: replacementSocket);
            Assert.False(renewal.IsCompleted);
            Assert.Single(fixture.Peers.Peers);

            releaseAudioStop.Set();
            Assert.True(await disconnected.WaitAsync(TimeSpan.FromSeconds(2)));
            Assert.Equal("renewal-unavailable", (await renewal.WaitAsync(TimeSpan.FromSeconds(2))).Code);
        }
        finally
        {
            releaseAudioStop.Set();
            await audioStop.WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    [Fact]
    public async Task RenewalWaitsForOldSocketCleanupBeforeFreshStartFallback()
    {
        await using var fixture = new Fixture(false);
        using var oldSocket = CreateSocket();
        using var replacementSocket = CreateSocket();
        await fixture.StartAsync("view", owner: oldSocket);
        await WaitUntilAsync(() => !fixture.Peers.Peers[0].Frames.IsEmpty);

        Assert.True(fixture.Coordinator.StopConnection("viewer", oldSocket));
        ScreenViewStartResult renewal = await fixture.OfferAsync("renew", "view", owner: replacementSocket);
        Assert.Equal("renewal-unavailable", renewal.Code);

        Assert.True((await fixture.OfferAsync("fresh", owner: replacementSocket)).Succeeded);
    }

    private static RelayVirtualWebSocket CreateSocket() =>
        new(Guid.NewGuid(), new string('r', 22), (_, _) => Task.CompletedTask);

    [Fact]
    public async Task PermissionRevokedDuringPeerCreationPreventsOfferAdmission()
    {
        await using var fixture = new Fixture(false);
        fixture.Peers.OnCreate = () => fixture.Manager.SetDevicePermission("viewer", DevicePermissionKind.ScreenViewing, false);

        ScreenViewStartResult start = await fixture.OfferAsync("revoked-view");

        Assert.False(start.Succeeded);
        Assert.Equal("permission-denied", start.Code);
        Assert.Null(start.OfferSdp);
        Assert.True(Assert.Single(fixture.Peers.Peers).Disposed);
    }

    [Fact]
    public async Task RevocationCancelsOfferPreparationBeforeDisposingItsPeer()
    {
        await using var fixture = new Fixture(false);
        var offerEntered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishOffer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Peers.OnCreate = () => fixture.Peers.Peers[^1].OnCreateOffer = async token =>
        {
            offerEntered.TrySetResult(token);
            await finishOffer.Task.ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            return Peer.Sdp;
        };

        Task<ScreenViewStartResult> start = fixture.OfferAsync("revoked-during-offer");
        try
        {
            CancellationToken offerToken = await offerEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Peer peer = Assert.Single(fixture.Peers.Peers);
            fixture.Manager.SetDevicePermission("viewer", DevicePermissionKind.ScreenViewing, false);

            Assert.True(offerToken.IsCancellationRequested);
            Assert.False(peer.Disposed);
            finishOffer.TrySetResult();
            Assert.False((await start).Succeeded);
            Assert.True(peer.Disposed);
        }
        finally
        {
            finishOffer.TrySetResult();
        }
    }

    [Fact]
    public async Task ShutdownLeavesPeerOwnedUntilItsInFlightAnswerReturns()
    {
        await using var fixture = new Fixture(false);
        Assert.True((await fixture.OfferAsync("slow-answer")).Succeeded);
        Peer peer = Assert.Single(fixture.Peers.Peers);
        using var finishAnswer = new ManualResetEventSlim();
        var enteredAnswer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        peer.OnApplyAnswer = () =>
        {
            enteredAnswer.TrySetResult();
            finishAnswer.Wait(TimeSpan.FromSeconds(7));
        };
        Task<ScreenViewOperationResult> answer = Task.Run(() => fixture.Answer("slow-answer"));

        try
        {
            await enteredAnswer.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await fixture.Coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(peer.Disposed);
            Assert.False(answer.IsCompleted);

            finishAnswer.Set();
            Assert.Equal("offer-expired", (await answer.WaitAsync(TimeSpan.FromSeconds(2))).Code);
            Assert.True(peer.Disposed);
        }
        finally
        {
            finishAnswer.Set();
            await answer.WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    [Fact]
    public async Task TransportThatStopsDuringAnswerCannotStartScreenCapture()
    {
        await using var fixture = new Fixture(false);
        Assert.True((await fixture.OfferAsync("failed-view")).Succeeded);
        Peer failed = Assert.Single(fixture.Peers.Peers);
        failed.OnApplyAnswer = () =>
        {
            failed.Connection.TrySetResult();
            failed.StopTransport();
        };

        ScreenViewOperationResult answer = fixture.Answer("failed-view");

        Assert.False(answer.Succeeded);
        Assert.Equal("offer-expired", answer.Code);
        Assert.True(failed.Disposed);
        Assert.Empty(failed.Frames);
        Assert.Equal(0, fixture.Audio.Created);
        Assert.True((await fixture.OfferAsync("next-view")).Succeeded);
    }

    [Fact]
    public async Task CandidateTransportThatStopsDuringTheKeyFrameKeepsTheOriginalView()
    {
        await using var fixture = new Fixture(false);
        await fixture.StartAsync("view");
        Peer original = Assert.Single(fixture.Peers.Peers);
        await WaitUntilAsync(() => !original.Frames.IsEmpty);

        await fixture.StartAsync("renew", "view");
        Peer replacement = fixture.Peers.Peers[1];
        replacement.AdmitVideo = () =>
        {
            replacement.StopTransport();
            return true;
        };
        replacement.Connection.TrySetResult();
        await replacement.Disposal.Task.WaitAsync(TimeSpan.FromSeconds(3));

        int framesBefore = original.Frames.Count;
        await WaitUntilAsync(() => original.Frames.Count > framesBefore);
        Assert.False(original.Disposed);
        Assert.Equal(0, fixture.Capture.EndCount);
        Assert.Equal(1, fixture.Audio.Created);
    }

    [Fact]
    public async Task PreviousViewPublishesItsStopBeforeTheNextViewEvenIfPeerDisposalIsDelayed()
    {
        await using var fixture = new Fixture(false);
        var activities = new ConcurrentQueue<(string OperationId, bool Active)>();
        fixture.Coordinator.ActivityChanged += (_, activity) => activities.Enqueue((activity.OperationId, activity.Active));
        await fixture.StartAsync("first-view");
        Peer first = Assert.Single(fixture.Peers.Peers);
        await WaitUntilAsync(() => !first.Frames.IsEmpty);
        using var releaseDisposal = new ManualResetEventSlim();
        var disposalEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        first.OnDispose = () =>
        {
            disposalEntered.TrySetResult();
            releaseDisposal.Wait(TimeSpan.FromSeconds(5));
        };

        try
        {
            Assert.True(fixture.Coordinator.Stop("viewer"));
            await disposalEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await fixture.StartAsync("second-view");
            await WaitUntilAsync(() => !fixture.Peers.Peers[1].Frames.IsEmpty);

            Assert.Equal([("first-view", true), ("first-view", false), ("second-view", true)], activities.ToArray());
        }
        finally
        {
            releaseDisposal.Set();
            await first.Disposal.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }
    }

    [Fact]
    public async Task DifferentDeviceRemainsBusyAndIsNotAllowedToTakeOver()
    {
        await using var fixture = new Fixture(false);
        await fixture.StartAsync("view");
        using var otherKey = new PairingTestKey();
        Assert.True(fixture.Manager.AcceptPairing(
            "other-viewer",
            "Other phone",
            fixture.Manager.CreatePairingToken(),
            reconnectPublicKey: otherKey.PublicKey).Accepted);
        const string transcript = "VolturaAir screen-view:start:v2:other-viewer:other:display-1";

        ScreenViewStartResult result = await fixture.Coordinator.StartAsync(
            "other-viewer",
            "other",
            "display-1",
            otherKey.SignPayload(transcript),
            CancellationToken.None,
            fixture.Relay);

        Assert.Equal("busy", result.Code);
        Assert.Equal("Another device is already viewing the screen.", result.Message);
        Assert.False(fixture.Peers.Peers[0].Disposed);
        Assert.True(fixture.Peers.Peers[1].Disposed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RenewalKeepsOneCaptureAndAudioRunAndSwitchesOnlyAtAKeyFrame(bool staticDesktop)
    {
        await using var fixture = new Fixture(staticDesktop);
        await fixture.StartAsync("view");
        await WaitUntilAsync(() => !fixture.Peers.Peers[0].Frames.IsEmpty);
        Peer original = fixture.Peers.Peers[0];
        Action staleStop = original.CaptureStopCallback();
        Action staleAudioStop = original.CaptureAudioStopCallback();
        for (int index = 0; index < 2; index++)
        {
            await fixture.StartAsync($"renew-{index}", "view");
            Peer replacement = fixture.Peers.Peers[^1];
            Assert.Empty(replacement.Frames);
            Assert.False(fixture.Peers.Peers[index].Disposed);
            replacement.Connection.TrySetResult();
            await WaitUntilAsync(() => !replacement.Frames.IsEmpty && fixture.Peers.Peers[index].Disposed);
            Assert.Equal(0x65, replacement.Frames.First()[0]);
            Assert.Equal(0, fixture.Capture.EndCount);
            Assert.Equal(1, fixture.Audio.Created);
        }
        staleStop();
        staleAudioStop();
        Assert.Equal(0, fixture.Capture.EndCount);
        Assert.False(fixture.Audio.Stopped);
        Assert.Single(fixture.Capture.Profiles.Distinct());
        // Every generated frame went to exactly one transport; no second encoder or duplicate stream.
        Assert.All(fixture.Peers.Configurations, configuration => Assert.True(configuration!.RelayOnly));
        Assert.True(fixture.Coordinator.Stop("viewer"));
        await WaitUntilAsync(() => fixture.Peers.Peers.All(peer => peer.Disposed));
        Assert.Equal(fixture.Capture.SentFrames, fixture.Peers.Peers.Sum(peer => peer.Frames.Count));
    }

    [Theory]
    [InlineData("proof")]
    [InlineData("connection")]
    [InlineData("stop")]
    [InlineData("permission")]
    public async Task FailedOrCanceledRenewalReleasesCandidateWithoutASecondCapture(string boundary)
    {
        await using var fixture = new Fixture(false);
        await fixture.StartAsync("view");
        await WaitUntilAsync(() => !fixture.Peers.Peers[0].Frames.IsEmpty);
        ScreenViewStartResult offer = await fixture.OfferAsync("renew", "view");
        Assert.True(offer.Succeeded);
        Assert.Equal("busy", (await fixture.OfferAsync("duplicate", "view")).Code);
        Peer candidate = fixture.Peers.Peers[1];
        ScreenViewOperationResult answer = fixture.Answer("renew", boundary == "proof" ? "invalid" : null);
        if (boundary == "proof") Assert.False(answer.Succeeded);
        else Assert.True(answer.Succeeded);
        if (boundary == "connection") candidate.Connection.TrySetException(new ScreenViewWebRtcException("Injected failure."));
        if (boundary == "stop") fixture.Coordinator.Stop("viewer");
        if (boundary == "permission") fixture.Manager.SetDevicePermission("viewer", DevicePermissionKind.ScreenViewing, false);
        await WaitUntilAsync(() => candidate.Disposed);
        Assert.Empty(candidate.Frames);
        Assert.Equal(1, fixture.Audio.Created);
        if (boundary is "proof" or "connection")
        {
            Assert.Equal(0, fixture.Capture.EndCount);
            Assert.False(fixture.Peers.Peers[0].Disposed);
        }
        else await WaitUntilAsync(() => fixture.Peers.Peers[0].Disposed);
    }

    [Fact]
    public async Task RenewalRejectsStaleSessionChangedBudgetAndUnsignedRenewalContext()
    {
        await using var fixture = new Fixture(false);
        await fixture.StartAsync("view");
        Assert.Equal("renewal-unavailable", (await fixture.OfferAsync("stale", "wrong-view")).Code);
        Assert.Equal("renewal-unavailable", (await fixture.OfferAsync("budget", "view", RelayScreenQuality.DataSaver)).Code);
        string ordinaryProof = fixture.Key.SignPayload("VolturaAir screen-view:start:v2:viewer:tampered:display-1");
        ScreenViewStartResult tampered = await fixture.Coordinator.StartAsync("viewer", "tampered", "display-1",
            ordinaryProof, CancellationToken.None, fixture.Relay, renewalOf: "view");
        Assert.Equal("invalid-proof", tampered.Code);
        Assert.Single(fixture.Peers.Peers);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!condition()) await Task.Delay(5, timeout.Token);
    }

    [Fact]
    public async Task ConnectedCandidateWithoutAKeyFrameTimesOutAndLeavesOriginalRunning()
    {
        await using var fixture = new Fixture(false);
        await fixture.StartAsync("view");
        await WaitUntilAsync(() => !fixture.Peers.Peers[0].Frames.IsEmpty);
        fixture.Capture.SuppressKeyFrames = true;
        await fixture.StartAsync("renew", "view");
        Peer candidate = fixture.Peers.Peers[1];
        candidate.Connection.TrySetResult();
        await candidate.Disposal.Task.WaitAsync(TimeSpan.FromSeconds(18));
        Assert.Empty(candidate.Frames);
        Assert.False(fixture.Peers.Peers[0].Disposed);
        Assert.Equal(0, fixture.Capture.EndCount);
    }

    [Fact]
    public async Task CandidateBackpressureKeepsFramesOnTheOriginalPeerUntilAKeyFrameIsAccepted()
    {
        await using var fixture = new Fixture(true);
        await fixture.StartAsync("view");
        Peer original = fixture.Peers.Peers[0];
        await WaitUntilAsync(() => !original.Frames.IsEmpty);
        int originalsBeforeRenewal = original.Frames.Count;
        await fixture.StartAsync("renew", "view");
        Peer candidate = fixture.Peers.Peers[1];
        bool allowCandidate = false;
        var rejected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        candidate.AdmitVideo = () =>
        {
            if (Volatile.Read(ref allowCandidate)) return true;
            rejected.TrySetResult();
            return false;
        };
        candidate.Connection.TrySetResult();
        await rejected.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await WaitUntilAsync(() => original.Frames.Count > originalsBeforeRenewal);
        Assert.False(original.Disposed);
        Assert.Empty(candidate.Frames);

        Volatile.Write(ref allowCandidate, true);
        await WaitUntilAsync(() => original.Disposed && !candidate.Frames.IsEmpty);
        byte[] firstReplacementFrame = candidate.Frames.First();
        Assert.Equal(0x65, firstReplacementFrame[0]);
        Assert.DoesNotContain(original.Frames, frame => ReferenceEquals(frame, firstReplacementFrame));
        Assert.Equal(0, fixture.Capture.EndCount);
        Assert.Equal(1, fixture.Audio.Created);
    }

    [Fact]
    public async Task FailedNativeCandidateSendReleasesOnlyTheReplacement()
    {
        await using var fixture = new Fixture(false);
        await fixture.StartAsync("view");
        Peer original = fixture.Peers.Peers[0];
        await WaitUntilAsync(() => !original.Frames.IsEmpty);
        await fixture.StartAsync("renew", "view");
        Peer candidate = fixture.Peers.Peers[1];
        candidate.AdmitVideo = () => throw new ScreenViewWebRtcException("Injected candidate send failure.");
        candidate.Connection.TrySetResult();
        await candidate.Disposal.Task.WaitAsync(TimeSpan.FromSeconds(3));
        int originalsAfterFailure = original.Frames.Count;
        await WaitUntilAsync(() => original.Frames.Count > originalsAfterFailure);
        Assert.False(original.Disposed);
        Assert.Empty(candidate.Frames);
        Assert.Equal(0, fixture.Capture.EndCount);
        Assert.False(fixture.Audio.Stopped);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisposalFailureCannotKeepTheRenewalSlotOccupied(bool promoted)
    {
        await using var fixture = new Fixture(false);
        await fixture.StartAsync("view");
        Peer original = fixture.Peers.Peers[0];
        await WaitUntilAsync(() => !original.Frames.IsEmpty);
        await fixture.StartAsync("renew", "view");
        Peer candidate = fixture.Peers.Peers[1];
        Peer retiring = promoted ? original : candidate;
        retiring.FailDisposal = true;
        if (!promoted)
            candidate.AdmitVideo = () => throw new ScreenViewWebRtcException("Injected candidate send failure.");
        candidate.Connection.TrySetResult();
        await retiring.Disposal.Task.WaitAsync(TimeSpan.FromSeconds(3));

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (true)
        {
            ScreenViewStartResult next = await fixture.OfferAsync("next-renewal", "view");
            if (next.Succeeded) break;
            Assert.Equal("busy", next.Code);
            await Task.Delay(5, timeout.Token);
        }
        Assert.False((promoted ? candidate : original).Disposed);
    }

    [Fact]
    public async Task DisplaySwitchDuringCandidateSendKeepsControlResponsiveAndRetiresTheOldHandover()
    {
        await using var fixture = new Fixture(false);
        await fixture.StartAsync("view");
        Peer original = fixture.Peers.Peers[0];
        await WaitUntilAsync(() => !original.Frames.IsEmpty);
        await fixture.StartAsync("renew", "view");
        Peer candidate = fixture.Peers.Peers[1];
        using var allowSend = new ManualResetEventSlim(false);
        var sending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        candidate.AdmitVideo = () =>
        {
            if (sending.TrySetResult()) return allowSend.Wait(TimeSpan.FromSeconds(5));
            return true;
        };
        candidate.Connection.TrySetResult();
        try
        {
            await sending.Task.WaitAsync(TimeSpan.FromSeconds(3));
            fixture.Capture.SuppressKeyFrames = true;
            ScreenViewOperationResult switched = await Task.Run(() => fixture.Coordinator.SetSource("viewer", "display-2"))
                .WaitAsync(TimeSpan.FromSeconds(3));
            Assert.True(switched.Succeeded);
            Assert.False(original.Disposed);
            int originalFrames = original.Frames.Count;
            int capturedBeforeRelease = Volatile.Read(ref fixture.Capture.SentFrames);
            allowSend.Set();
            await WaitUntilAsync(() => Volatile.Read(ref fixture.Capture.SentFrames) >= capturedBeforeRelease + 3);
            Assert.False(original.Disposed);
            Assert.Equal(originalFrames, original.Frames.Count);
            Assert.Single(candidate.Frames);

            fixture.Capture.SuppressKeyFrames = false;
            await WaitUntilAsync(() => original.Disposed && candidate.Frames.Count >= 2);
            Assert.Equal("display-2", fixture.Capture.LastSource);
            Assert.Equal(0x65, candidate.Frames.ElementAt(1)[0]);
        }
        finally { allowSend.Set(); }
    }

    [Fact]
    public async Task DisplaySwitchDuringPreparationKeepsTheSelectedDisplay()
    {
        await using var fixture = new Fixture(false);
        await fixture.StartAsync("view");
        await WaitUntilAsync(() => !fixture.Peers.Peers[0].Frames.IsEmpty);
        await fixture.StartAsync("renew", "view");
        Assert.True(fixture.Coordinator.SetSource("viewer", "display-2").Succeeded);
        fixture.Peers.Peers[1].Connection.TrySetResult();
        await WaitUntilAsync(() => !fixture.Peers.Peers[1].Frames.IsEmpty);
        Assert.Equal("display-2", fixture.Capture.LastSource);
        Assert.Equal(1, fixture.Capture.EndCount);
        Assert.Equal(1, fixture.Audio.Created);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly TempPairingStore _store = new();
        private readonly ScreenViewHostIdentity _identity = ScreenViewHostIdentity.CreateEphemeral();
        public PairingTestKey Key { get; } = new();
        public Capture Capture { get; }
        public PeerFactory Peers { get; } = new();
        public AudioFactory Audio { get; } = new();
        public RelayTurnConfiguration Relay { get; } = new([], [], DateTimeOffset.UtcNow.AddMinutes(15), 0,
            DateTimeOffset.UtcNow, RelayScreenQuality.High);
        public ScreenViewCoordinator Coordinator { get; }
        public PairingManager Manager { get; }
        public Fixture(bool staticDesktop)
        {
            AppPermissionSettings.Save(AppPermissionSettings.Load() with { AllowScreenViewing = true });
            var manager = new PairingManager(_store.Store, _identity);
            Manager = manager;
            Assert.True(manager.AcceptPairing("viewer", "Phone", manager.CreatePairingToken(), reconnectPublicKey: Key.PublicKey).Accepted);
            var status = new HostStatusPayloadFactory(manager, null!, null!, null!, null!, null!, null!, null!,
                null!, null!, null!, null!, null!, null!, null!);
            Capture = new Capture(staticDesktop);
            Coordinator = new ScreenViewCoordinator(manager, status, Capture, Peers, audioFactory: Audio);
        }
        public Task<ScreenViewStartResult> OfferAsync(
            string operation,
            string? renewalOf = null,
            RelayScreenQuality? quality = null,
            WebSocket? owner = null)
        {
            string transcript = $"VolturaAir screen-view:start:v2:viewer:{operation}:display-1";
            if (renewalOf is not null) transcript += $":renew:{renewalOf}";
            return Coordinator.StartAsync("viewer", operation, "display-1", Key.SignPayload(transcript),
                CancellationToken.None, quality.HasValue ? Relay with { EffectiveQuality = quality.Value } : Relay,
                renewalOf: renewalOf, owner: owner);
        }
        public async Task StartAsync(string operation, string? renewalOf = null, WebSocket? owner = null)
        {
            int previousAudioRuns = Volatile.Read(ref Audio.Created);
            Assert.True((await OfferAsync(operation, renewalOf, owner: owner)).Succeeded);
            if (renewalOf is null) Peers.Peers[^1].Connection.TrySetResult();
            Assert.True(Answer(operation, owner: owner).Succeeded);
            if (renewalOf is null) await WaitUntilAsync(() => Volatile.Read(ref Audio.Created) > previousAudioRuns);
        }
        public ScreenViewOperationResult Answer(string operation, string? proof = null, WebSocket? owner = null)
        {
            string hash = ScreenViewHostIdentity.Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(Peer.Sdp)));
            return Coordinator.CompleteAnswer("viewer", operation, Peer.Sdp,
                proof ?? Key.SignPayload($"VolturaAir screen-view:answer:v2:viewer:{operation}:display-1:{hash}:{hash}"),
                owner: owner);
        }
        public async ValueTask DisposeAsync()
        {
            await Coordinator.DisposeAsync();
            Key.Dispose();
            _identity.Dispose();
            _store.Dispose();
        }
    }

    private sealed class Capture(bool staticDesktop) : IScreenViewCaptureSource
    {
        public int EndCount;
        public int SentFrames;
        public volatile bool SuppressKeyFrames;
        public string? LastSource;
        public ConcurrentQueue<(ScreenViewCaptureProfile, int)> Profiles { get; } = new();
        public IReadOnlyList<ScreenViewSource> GetSources() => [new("display-1", "Display", 1920, 1080, true), new("display-2", "Other", 1080, 1920, false)];
        public async Task<ScreenViewEncodedFrame?> CaptureVideoAsync(string sourceId, ScreenViewCaptureProfile profile,
            int bitrate, bool forceKeyFrame, CancellationToken cancellationToken)
        {
            await Task.Delay(5, cancellationToken);
            LastSource = sourceId;
            forceKeyFrame = forceKeyFrame && !SuppressKeyFrames;
            Profiles.Enqueue((profile, bitrate));
            if (staticDesktop && !forceKeyFrame) return null;
            Interlocked.Increment(ref SentFrames);
            return new([(byte)(forceKeyFrame ? 0x65 : 0x41)], profile.MaxWidth, profile.MaxHeight, profile.FramesPerSecond, forceKeyFrame);
        }
        public void EndCapture() => Interlocked.Increment(ref EndCount);
    }

    private sealed class PeerFactory : IScreenViewWebRtcPeerFactory
    {
        public List<Peer> Peers { get; } = [];
        public List<ScreenViewPeerConfiguration?> Configurations { get; } = [];
        public Action? OnCreate { get; set; }
        public IScreenViewWebRtcPeer Create() => Create(null);
        public IScreenViewWebRtcPeer Create(ScreenViewPeerConfiguration? configuration)
        {
            var peer = new Peer();
            Peers.Add(peer);
            Configurations.Add(configuration);
            OnCreate?.Invoke();
            return peer;
        }
    }

    private sealed class Peer : IScreenViewWebRtcPeer
    {
        public const string Sdp = "v=0\r\ns=isolated-renewal\r\n";
        public TaskCompletionSource Connection { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Connected => Connection.Task;
        public ConcurrentQueue<byte[]> Frames { get; } = new();
        public Func<bool>? AdmitVideo { get; set; }
        public Func<CancellationToken, Task<string>>? OnCreateOffer { get; set; }
        public Action? OnApplyAnswer { get; set; }
        public Action? OnDispose { get; set; }
        public bool TransportStopped { get; private set; }
        public bool FailDisposal { get; set; }
        public volatile bool Disposed;
        public TaskCompletionSource Disposal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public event EventHandler? Stopped;
        public event EventHandler? AudioStopped;
        public event EventHandler? KeyFrameRequested;
        public Action CaptureStopCallback() { EventHandler? callback = Stopped; return () => callback?.Invoke(this, EventArgs.Empty); }
        public Action CaptureAudioStopCallback() { EventHandler? callback = AudioStopped; return () => callback?.Invoke(this, EventArgs.Empty); }
        public Task<string> CreateOfferAsync(CancellationToken cancellationToken) =>
            OnCreateOffer?.Invoke(cancellationToken) ?? Task.FromResult(Sdp);
        public void ApplyAnswer(string answerSdp) => OnApplyAnswer?.Invoke();
        public void StopTransport()
        {
            TransportStopped = true;
            Stopped?.Invoke(this, EventArgs.Empty);
        }
        public bool TrySendH264(byte[] accessUnit, int framesPerSecond)
        {
            if (Disposed || AdmitVideo?.Invoke() == false) return false;
            Frames.Enqueue(accessUnit);
            return true;
        }
        public bool TrySendOpus(byte[] packet, uint rtpTimestamp) => !Disposed;
        public bool TrySendEvent(byte[] eventBytes) => !Disposed;
        public void Dispose()
        {
            OnDispose?.Invoke();
            Disposed = true;
            Connection.TrySetCanceled();
            Disposal.TrySetResult();
            _ = KeyFrameRequested;
            if (FailDisposal) throw new IOException("Injected peer cleanup failure.");
        }
    }

    private sealed class AudioFactory : IScreenViewSystemAudioCaptureFactory, IScreenViewSystemAudioCapture
    {
        public int Created;
        public volatile bool Stopped;
        public Action? OnCanceled { get; set; }
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IScreenViewSystemAudioCapture Create() { Interlocked.Increment(ref Created); return this; }
        public async Task RunAsync(Func<ScreenViewEncodedAudioFrame, bool> send, Action<ScreenViewAudioAvailability> reportAvailability,
            Func<ScreenViewSoundQuality> getSoundQuality, CancellationToken cancellationToken)
        {
            using CancellationTokenRegistration registration = OnCanceled is { } callback ? cancellationToken.Register(callback) : default;
            Started.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            finally { Stopped = true; }
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
