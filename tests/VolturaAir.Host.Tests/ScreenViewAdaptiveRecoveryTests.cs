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

    private static string HashSdp(string sdp) =>
        ScreenViewHostIdentity.Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(sdp)));

    private sealed class AdaptiveCaptureSource : IScreenViewCaptureSource
    {
        public int EncoderFailuresRemaining;
        public TaskCompletionSource Captured { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Ended { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public System.Collections.Concurrent.ConcurrentQueue<bool> ForceKeyFrameRequests { get; } = new();
        public System.Collections.Concurrent.ConcurrentQueue<ScreenViewCaptureProfile> Profiles { get; } = new();
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
        public event EventHandler? KeyFrameRequested;
        public Task Connected => Task.CompletedTask;
        public TaskCompletionSource AcceptedFrame { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<string> CreateOfferAsync(CancellationToken cancellationToken) =>
            Task.FromResult("v=0\r\no=voltura 1 1 IN IP4 127.0.0.1\r\ns=offer\r\nt=0 0\r\n");
        public void ApplyAnswer(string answerSdp) { }
        public bool TrySendH264(byte[] accessUnit, int framesPerSecond)
        {
            bool accepted = _sendResults.Count == 0 || _sendResults.Dequeue();
            if (accepted) AcceptedFrame.TrySetResult();
            return accepted;
        }
        public bool TrySendEvent(byte[] eventBytes) => true;
        public void Dispose()
        {
            _ = Stopped;
            _ = KeyFrameRequested;
        }
    }
}
