using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VolturaAir.Host.Tests;

[Collection(AppPermissionSettingsCollection.Name)]
public sealed class ScreenViewAdaptiveRecoveryTests : WebHostServiceTestBase
{
    [Fact]
    public async Task RejectedReplacementFrameKeepsAKeyFramePendingUntilSendRecovers()
    {
        HostPermissionSet originalPermissions = AppPermissionSettings.Load();
        try
        {
            AppPermissionSettings.Save(originalPermissions with { AllowScreenViewing = true });
            var capture = new AdaptiveCaptureSource();
            var peer = new AdaptivePeer(false, false, true);
            await using var fixture = await WebHostFixture.StartAsync(
                screenViewCapture: capture,
                screenViewPeerFactory: new AdaptivePeerFactory(peer));
            using var reconnectKey = new PairingTestKey();
            using WebSocket control = await ConnectAsync(fixture.WebHost);
            await PairAsync(control, fixture.Manager, reconnectKey);

            await StartAndAnswerAsync(control, reconnectKey, "screen-keyframe-retry");
            await peer.AcceptedFrame.Task.WaitAsync(TimeSpan.FromSeconds(2));
            _ = await SendUntilTypeAsync(control, new { type = "screen.view.stop", operationId = "screen-keyframe-stop" }, "screen.view.stop.result");
            await capture.Ended.Task.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Equal([true, true, true], capture.ForceKeyFrameRequests.Take(3).ToArray());
        }
        finally
        {
            AppPermissionSettings.Save(originalPermissions);
        }
    }

    [Fact]
    public async Task EncoderSampleFailureFallsBackToALowerProfileAndCleansUp()
    {
        HostPermissionSet originalPermissions = AppPermissionSettings.Load();
        try
        {
            AppPermissionSettings.Save(originalPermissions with { AllowScreenViewing = true });
            var capture = new AdaptiveCaptureSource { EncoderFailuresRemaining = 1 };
            var peer = new AdaptivePeer(true);
            await using var fixture = await WebHostFixture.StartAsync(
                screenViewCapture: capture,
                screenViewPeerFactory: new AdaptivePeerFactory(peer));
            using var reconnectKey = new PairingTestKey();
            using WebSocket control = await ConnectAsync(fixture.WebHost);
            await PairAsync(control, fixture.Manager, reconnectKey);

            await StartAndAnswerAsync(control, reconnectKey, "screen-encoder-retry");
            await capture.Captured.Task.WaitAsync(TimeSpan.FromSeconds(2));
            _ = await SendUntilTypeAsync(control, new { type = "screen.view.stop", operationId = "screen-encoder-stop" }, "screen.view.stop.result");
            await capture.Ended.Task.WaitAsync(TimeSpan.FromSeconds(2));

            ScreenViewCaptureProfile[] profiles = [.. capture.Profiles.Take(2)];
            Assert.Equal(2, profiles.Length);
            Assert.NotEqual(profiles[0], profiles[1]);
        }
        finally
        {
            AppPermissionSettings.Save(originalPermissions);
        }
    }

    [Fact]
    public async Task AudioCaptureFailureLeavesVideoAndCommandHealthAvailable()
    {
        HostPermissionSet originalPermissions = AppPermissionSettings.Load();
        try
        {
            AppPermissionSettings.Save(originalPermissions with { AllowScreenViewing = true });
            var capture = new AdaptiveCaptureSource();
            var peer = new AdaptivePeer(true);
            var audio = new FailingAudioCaptureFactory();
            await using var fixture = await WebHostFixture.StartAsync(
                screenViewCapture: capture,
                screenViewPeerFactory: new AdaptivePeerFactory(peer),
                screenViewAudioCaptureFactory: audio);
            using var reconnectKey = new PairingTestKey();
            using WebSocket control = await ConnectAsync(fixture.WebHost);
            await PairAsync(control, fixture.Manager, reconnectKey);

            await StartAndAnswerAsync(control, reconnectKey, "screen-audio-failure");
            await audio.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await peer.AudioUnavailable.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await peer.AcceptedFrame.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await AssertControlHealthyAsync(control);

            _ = await SendUntilTypeAsync(control, new { type = "screen.view.stop", operationId = "screen-audio-failure-stop" }, "screen.view.stop.result");
            await capture.Ended.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            AppPermissionSettings.Save(originalPermissions);
        }
    }

    [Fact]
    public async Task SoundQualityChangesLiveWithoutReplacingAudioVideoOrControl()
    {
        HostPermissionSet originalPermissions = AppPermissionSettings.Load();
        try
        {
            AppPermissionSettings.Save(originalPermissions with { AllowScreenViewing = true });
            var capture = new AdaptiveCaptureSource();
            var peer = new AdaptivePeer(true);
            var audio = new ObservingAudioCaptureFactory();
            await using var fixture = await WebHostFixture.StartAsync(
                screenViewCapture: capture,
                screenViewPeerFactory: new AdaptivePeerFactory(peer),
                screenViewAudioCaptureFactory: audio);
            using var reconnectKey = new PairingTestKey();
            using WebSocket control = await ConnectAsync(fixture.WebHost);
            await PairAsync(control, fixture.Manager, reconnectKey);
            await SendAsync(control, new
            {
                type = "screen.view.sound-quality.set",
                soundQuality = "standard"
            });

            await StartAndAnswerAsync(control, reconnectKey, "screen-audio-quality");
            await audio.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await capture.Captured.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.Equal(ScreenViewSoundQuality.Standard, audio.SoundQuality);
            ScreenViewCaptureProfile videoProfile = capture.Profiles.First();
            int videoBitrate = capture.Bitrates.First();

            await SendAsync(control, new
            {
                type = "screen.view.sound-quality.set",
                soundQuality = "low"
            });
            await WaitUntilAsync(
                () => audio.SoundQuality == ScreenViewSoundQuality.Low,
                TimeSpan.FromSeconds(2));
            await AssertControlHealthyAsync(control);

            Assert.Equal(1, audio.CreatedCount);
            Assert.All(capture.Profiles, profile => Assert.Equal(videoProfile, profile));
            Assert.All(capture.Bitrates, bitrate => Assert.Equal(videoBitrate, bitrate));

            _ = await SendUntilTypeAsync(
                control,
                new { type = "screen.view.stop", operationId = "screen-audio-quality-stop" },
                "screen.view.stop.result");
            await capture.Ended.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            AppPermissionSettings.Save(originalPermissions);
        }
    }

    [Fact]
    public async Task SoundQualityPersistenceFailureKeepsActiveViewAndControlHealthy()
    {
        HostPermissionSet originalPermissions = AppPermissionSettings.Load();
        try
        {
            AppPermissionSettings.Save(originalPermissions with { AllowScreenViewing = true });
            var capture = new AdaptiveCaptureSource();
            var peer = new AdaptivePeer(true);
            var audio = new ObservingAudioCaptureFactory();
            await using var fixture = await WebHostFixture.StartAsync(
                screenViewCapture: capture,
                screenViewPeerFactory: new AdaptivePeerFactory(peer),
                screenViewAudioCaptureFactory: audio);
            using var reconnectKey = new PairingTestKey();
            using WebSocket control = await ConnectAsync(fixture.WebHost);
            await PairAsync(control, fixture.Manager, reconnectKey);
            await SendAsync(control, new
            {
                type = "screen.view.sound-quality.set",
                soundQuality = "standard"
            });
            await StartAndAnswerAsync(control, reconnectKey, "screen-audio-quality-save-failure");
            await audio.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await capture.Captured.Task.WaitAsync(TimeSpan.FromSeconds(2));

            fixture.Store.Store.BeforeReplaceForTests = () => throw new IOException("injected replace failure");
            JsonElement status;
            try
            {
                status = await SendUntilTypeAsync(control, new
                {
                    type = "screen.view.sound-quality.set",
                    soundQuality = "low"
                }, "status");
            }
            finally
            {
                fixture.Store.Store.BeforeReplaceForTests = null;
            }

            JsonElement host = status.GetProperty("host");
            Assert.Equal("standard", host.GetProperty("screenSoundQuality").GetString());
            Assert.True(host.GetProperty("screenSoundQualityOverridden").GetBoolean());
            Assert.Equal(ScreenViewSoundQuality.Standard, fixture.Manager.GetDeviceScreenSoundQuality("client-screen-adaptive"));
            Assert.Equal(ScreenViewSoundQuality.Standard, audio.SoundQuality);
            Assert.Equal(1, audio.CreatedCount);
            await AssertControlHealthyAsync(control);

            _ = await SendUntilTypeAsync(
                control,
                new { type = "screen.view.stop", operationId = "screen-audio-quality-save-failure-stop" },
                "screen.view.stop.result");
            await capture.Ended.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            AppPermissionSettings.Save(originalPermissions);
        }
    }

    [Fact]
    public async Task AudioTrackClosureLeavesVideoAndCommandHealthAvailable()
    {
        HostPermissionSet originalPermissions = AppPermissionSettings.Load();
        try
        {
            AppPermissionSettings.Save(originalPermissions with { AllowScreenViewing = true });
            var capture = new AdaptiveCaptureSource();
            var peer = new AdaptivePeer(true);
            var audio = new WaitingAudioCaptureFactory();
            await using var fixture = await WebHostFixture.StartAsync(
                screenViewCapture: capture,
                screenViewPeerFactory: new AdaptivePeerFactory(peer),
                screenViewAudioCaptureFactory: audio);
            using var reconnectKey = new PairingTestKey();
            using WebSocket control = await ConnectAsync(fixture.WebHost);
            await PairAsync(control, fixture.Manager, reconnectKey);

            await StartAndAnswerAsync(control, reconnectKey, "screen-audio-track-close");
            await audio.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            peer.RaiseAudioStopped();
            await audio.Ended.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await peer.AudioUnavailable.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await peer.AcceptedFrame.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await AssertControlHealthyAsync(control);

            _ = await SendUntilTypeAsync(control, new { type = "screen.view.stop", operationId = "screen-audio-track-close-stop" }, "screen.view.stop.result");
            await capture.Ended.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            AppPermissionSettings.Save(originalPermissions);
        }
    }

    [Fact]
    public async Task LateAudioTrackCallbackCannotEscapeCompletedSessionTeardown()
    {
        HostPermissionSet originalPermissions = AppPermissionSettings.Load();
        try
        {
            AppPermissionSettings.Save(originalPermissions with { AllowScreenViewing = true });
            var capture = new AdaptiveCaptureSource();
            var peer = new AdaptivePeer(true);
            var audio = new WaitingAudioCaptureFactory();
            await using var fixture = await WebHostFixture.StartAsync(
                screenViewCapture: capture,
                screenViewPeerFactory: new AdaptivePeerFactory(peer),
                screenViewAudioCaptureFactory: audio);
            using var reconnectKey = new PairingTestKey();
            using WebSocket control = await ConnectAsync(fixture.WebHost);
            await PairAsync(control, fixture.Manager, reconnectKey);

            await StartAndAnswerAsync(control, reconnectKey, "screen-audio-late-callback");
            await audio.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
            (Task callback, Action release) = peer.DeferAudioStoppedCallback();
            _ = await SendUntilTypeAsync(control, new { type = "screen.view.stop", operationId = "screen-audio-late-callback-stop" }, "screen.view.stop.result");
            await peer.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(2));

            release();
            await callback.WaitAsync(TimeSpan.FromSeconds(2));
            await AssertControlHealthyAsync(control);
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
            clientId = "client-screen-adaptive",
            deviceName = "Adaptive screen test",
            pairToken = manager.CreatePairingToken(),
            reconnectPublicKey = key.PublicKey
        });
        Assert.Equal("pair.accepted", accepted.GetProperty("type").GetString());
    }

    private static async Task StartAndAnswerAsync(WebSocket control, PairingTestKey key, string operationId)
    {
        const string clientId = "client-screen-adaptive";
        const string displayId = "display-1";
        JsonElement start = await SendUntilTypeAsync(control, new
        {
            type = "screen.view.start",
            operationId,
            displayId,
            clientSignature = key.SignPayload($"VolturaAir screen-view:start:v2:{clientId}:{operationId}:{displayId}")
        }, "screen.view.start.result");
        string offer = start.GetProperty("offerSdp").GetString()!;
        const string answer = "v=0\r\no=phone 1 1 IN IP4 127.0.0.1\r\ns=answer\r\nt=0 0\r\n";
        string transcript = $"VolturaAir screen-view:answer:v2:{clientId}:{operationId}:{displayId}:{HashSdp(offer)}:{HashSdp(answer)}";
        JsonElement result = await SendUntilTypeAsync(control, new
        {
            type = "screen.view.answer",
            operationId,
            answerSdp = answer,
            clientSignature = key.SignPayload(transcript)
        }, "screen.view.answer.result");
        Assert.True(result.GetProperty("succeeded").GetBoolean());
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

    private static async Task AssertControlHealthyAsync(WebSocket control)
    {
        JsonElement pong = await SendUntilTypeAsync(control, new { type = "health.ping" }, "health.pong");
        Assert.Equal("health.pong", pong.GetProperty("type").GetString());
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        using var cancellation = new CancellationTokenSource(timeout);
        while (!condition())
        {
            await Task.Delay(10, cancellation.Token);
        }
    }

    private static string HashSdp(string sdp) =>
        ScreenViewHostIdentity.Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(sdp)));

    private sealed class AdaptiveCaptureSource : IScreenViewCaptureSource
    {
        public int EncoderFailuresRemaining;
        public TaskCompletionSource Captured { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Ended { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public System.Collections.Concurrent.ConcurrentQueue<bool> ForceKeyFrameRequests { get; } = new();
        public System.Collections.Concurrent.ConcurrentQueue<ScreenViewCaptureProfile> Profiles { get; } = new();
        public System.Collections.Concurrent.ConcurrentQueue<int> Bitrates { get; } = new();
        public IReadOnlyList<ScreenViewSource> GetSources() =>
            [new("display-1", "Display 1", 1920, 1080, true)];
        public Task<ScreenViewEncodedFrame?> CaptureVideoAsync(
            string sourceId,
            ScreenViewCaptureProfile profile,
            int bitrate,
            bool forceKeyFrame,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ForceKeyFrameRequests.Enqueue(forceKeyFrame);
            Profiles.Enqueue(profile);
            Bitrates.Enqueue(bitrate);
            if (Interlocked.Decrement(ref EncoderFailuresRemaining) >= 0)
                throw new ScreenViewCaptureException("encoder-failed", "Injected encoder sample failure.");
            Captured.TrySetResult();
            return Task.FromResult<ScreenViewEncodedFrame?>(new([0, 0, 0, 1, 0x65, 1], profile.MaxWidth, profile.MaxHeight, profile.FramesPerSecond, true));
        }
        public void EndCapture() => Ended.TrySetResult();
    }

    private sealed class AdaptivePeerFactory(AdaptivePeer peer) : IScreenViewWebRtcPeerFactory
    {
        public IScreenViewWebRtcPeer Create() => peer;
    }

    private sealed class AdaptivePeer(params bool[] sendResults) : IScreenViewWebRtcPeer
    {
        private readonly Queue<bool> _sendResults = new(sendResults);
        public event EventHandler? Stopped;
        public event EventHandler? AudioStopped;
        public event EventHandler? KeyFrameRequested;
        public Task Connected => Task.CompletedTask;
        public TaskCompletionSource AcceptedFrame { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AudioUnavailable { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<string> CreateOfferAsync(CancellationToken cancellationToken) =>
            Task.FromResult("v=0\r\no=voltura 1 1 IN IP4 127.0.0.1\r\ns=offer\r\nt=0 0\r\n");
        public void ApplyAnswer(string answerSdp) { }
        public bool TrySendH264(byte[] accessUnit, int framesPerSecond)
        {
            bool accepted = _sendResults.Count == 0 || _sendResults.Dequeue();
            if (accepted) AcceptedFrame.TrySetResult();
            return accepted;
        }
        public bool TrySendOpus(byte[] packet, uint rtpTimestamp) => packet.Length > 0 && rtpTimestamp > 0;
        public bool TrySendEvent(byte[] eventBytes)
        {
            if (eventBytes is [7, 0, ..]) AudioUnavailable.TrySetResult();
            return true;
        }
        public void RaiseAudioStopped() => AudioStopped?.Invoke(this, EventArgs.Empty);
        public (Task Callback, Action Release) DeferAudioStoppedCallback()
        {
            EventHandler? callback = AudioStopped;
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            return (Task.Run(async () =>
            {
                await release.Task.ConfigureAwait(false);
                callback?.Invoke(this, EventArgs.Empty);
            }), () => release.TrySetResult());
        }
        public void Dispose()
        {
            Disposed.TrySetResult();
            _ = Stopped;
            _ = AudioStopped;
            _ = KeyFrameRequested;
        }
    }

    private sealed class FailingAudioCaptureFactory : IScreenViewSystemAudioCaptureFactory
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IScreenViewSystemAudioCapture Create() => new Capture(this);

        private sealed class Capture(FailingAudioCaptureFactory owner) : IScreenViewSystemAudioCapture
        {
            public Task RunAsync(
                Func<ScreenViewEncodedAudioFrame, bool> send,
                Action<ScreenViewAudioAvailability> reportAvailability,
                Func<ScreenViewSoundQuality> getSoundQuality,
                CancellationToken cancellationToken)
            {
                _ = send;
                _ = getSoundQuality;
                cancellationToken.ThrowIfCancellationRequested();
                reportAvailability(new(true, "audio-ready", "PC sound is available."));
                owner.Started.TrySetResult();
                throw new InvalidOperationException("Injected audio capture failure.");
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class WaitingAudioCaptureFactory : IScreenViewSystemAudioCaptureFactory
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Ended { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IScreenViewSystemAudioCapture Create() => new Capture(this);

        private sealed class Capture(WaitingAudioCaptureFactory owner) : IScreenViewSystemAudioCapture
        {
            public async Task RunAsync(
                Func<ScreenViewEncodedAudioFrame, bool> send,
                Action<ScreenViewAudioAvailability> reportAvailability,
                Func<ScreenViewSoundQuality> getSoundQuality,
                CancellationToken cancellationToken)
            {
                _ = send;
                _ = getSoundQuality;
                reportAvailability(new(true, "audio-ready", "PC sound is available."));
                owner.Started.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    owner.Ended.TrySetResult();
                }
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class ObservingAudioCaptureFactory : IScreenViewSystemAudioCaptureFactory
    {
        private Func<ScreenViewSoundQuality>? _getSoundQuality;
        private int _createdCount;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int CreatedCount => Volatile.Read(ref _createdCount);
        public ScreenViewSoundQuality SoundQuality =>
            Volatile.Read(ref _getSoundQuality)?.Invoke() ?? ScreenViewSoundQuality.High;

        public IScreenViewSystemAudioCapture Create()
        {
            Interlocked.Increment(ref _createdCount);
            return new Capture(this);
        }

        private sealed class Capture(ObservingAudioCaptureFactory owner) : IScreenViewSystemAudioCapture
        {
            public async Task RunAsync(
                Func<ScreenViewEncodedAudioFrame, bool> send,
                Action<ScreenViewAudioAvailability> reportAvailability,
                Func<ScreenViewSoundQuality> getSoundQuality,
                CancellationToken cancellationToken)
            {
                _ = send;
                Volatile.Write(ref owner._getSoundQuality, getSoundQuality);
                reportAvailability(new(true, "audio-ready", "PC sound is available."));
                owner.Started.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
