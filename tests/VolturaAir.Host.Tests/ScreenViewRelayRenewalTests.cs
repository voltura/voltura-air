using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace VolturaAir.Host.Tests;

[Collection(AppPermissionSettingsCollection.Name)]
public sealed class ScreenViewRelayRenewalTests : IsolatedHostSettingsTest
{
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
            var manager = new PairingManager(_store.Store);
            Manager = manager;
            Assert.True(manager.AcceptPairing("viewer", "Phone", manager.CreatePairingToken(), reconnectPublicKey: Key.PublicKey).Accepted);
            var status = new HostStatusPayloadFactory(manager, null!, null!, null!, null!, null!, null!, null!,
                null!, null!, null!, null!, null!, null!, null!);
            Capture = new Capture(staticDesktop);
            Coordinator = new ScreenViewCoordinator(manager, status, Capture, Peers, audioFactory: Audio);
        }
        public Task<ScreenViewStartResult> OfferAsync(string operation, string? renewalOf = null, RelayScreenQuality? quality = null)
        {
            string transcript = $"VolturaAir screen-view:start:v2:viewer:{operation}:display-1";
            if (renewalOf is not null) transcript += $":renew:{renewalOf}";
            return Coordinator.StartAsync("viewer", operation, "display-1", Key.SignPayload(transcript),
                CancellationToken.None, quality.HasValue ? Relay with { EffectiveQuality = quality.Value } : Relay, renewalOf: renewalOf);
        }
        public async Task StartAsync(string operation, string? renewalOf = null)
        {
            Assert.True((await OfferAsync(operation, renewalOf)).Succeeded);
            if (renewalOf is null) Peers.Peers[^1].Connection.TrySetResult();
            Assert.True(Answer(operation).Succeeded);
        }
        public ScreenViewOperationResult Answer(string operation, string? proof = null)
        {
            string hash = ScreenViewHostIdentity.Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(Peer.Sdp)));
            return Coordinator.CompleteAnswer("viewer", operation, Peer.Sdp,
                proof ?? Key.SignPayload($"VolturaAir screen-view:answer:v2:viewer:{operation}:display-1:{hash}:{hash}"));
        }
        public async ValueTask DisposeAsync()
        {
            await Coordinator.DisposeAsync();
            Key.Dispose();
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
        public IScreenViewWebRtcPeer Create() => Create(null);
        public IScreenViewWebRtcPeer Create(ScreenViewPeerConfiguration? configuration)
        {
            var peer = new Peer();
            Peers.Add(peer);
            Configurations.Add(configuration);
            return peer;
        }
    }

    private sealed class Peer : IScreenViewWebRtcPeer
    {
        public const string Sdp = "v=0\r\ns=isolated-renewal\r\n";
        public TaskCompletionSource Connection { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Connected => Connection.Task;
        public ConcurrentQueue<byte[]> Frames { get; } = new();
        public volatile bool Disposed;
        public TaskCompletionSource Disposal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public event EventHandler? Stopped;
        public event EventHandler? AudioStopped;
        public event EventHandler? KeyFrameRequested;
        public Action CaptureStopCallback() { EventHandler? callback = Stopped; return () => callback?.Invoke(this, EventArgs.Empty); }
        public Action CaptureAudioStopCallback() { EventHandler? callback = AudioStopped; return () => callback?.Invoke(this, EventArgs.Empty); }
        public Task<string> CreateOfferAsync(CancellationToken cancellationToken) => Task.FromResult(Sdp);
        public void ApplyAnswer(string answerSdp) { }
        public bool TrySendH264(byte[] accessUnit, int framesPerSecond) { Frames.Enqueue(accessUnit); return !Disposed; }
        public bool TrySendOpus(byte[] packet, uint rtpTimestamp) => !Disposed;
        public bool TrySendEvent(byte[] eventBytes) => !Disposed;
        public void Dispose() { Disposed = true; Connection.TrySetCanceled(); Disposal.TrySetResult(); _ = KeyFrameRequested; }
    }

    private sealed class AudioFactory : IScreenViewSystemAudioCaptureFactory, IScreenViewSystemAudioCapture
    {
        public int Created;
        public volatile bool Stopped;
        public IScreenViewSystemAudioCapture Create() { Interlocked.Increment(ref Created); return this; }
        public async Task RunAsync(Func<ScreenViewEncodedAudioFrame, bool> send, Action<ScreenViewAudioAvailability> reportAvailability,
            Func<ScreenViewSoundQuality> getSoundQuality, CancellationToken cancellationToken)
        {
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            finally { Stopped = true; }
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
