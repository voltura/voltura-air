using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace VolturaAir.Host.Tests;

[Collection(AppPermissionSettingsCollection.Name)]
public sealed class ScreenViewAdaptiveRecoveryTests : WebHostServiceTestBase
{
    [Fact]
    public void CursorBackpressureRetainsTheShapeAndCoalescesNewPositions()
    {
        using var peer = new AdaptivePeer();
        peer.CursorSendResults.Enqueue(false);
        var delivery = new ScreenViewCursorDelivery();
        var cursor = new ScreenViewCursorUpdate(true, 1, 2, 0, 0, 4, 4, [1, 2, 3]);

        delivery.Publish(peer, "first", cursor);
        delivery.Publish(peer, "first", cursor with { X = 16, PngBytes = null });
        delivery.Publish(peer, "first", cursor with { X = 21, PngBytes = null });

        Assert.Equal(3, peer.CursorRecords.Count);
        Assert.Equal(16, BinaryPrimitives.ReadInt32BigEndian(peer.CursorRecords[1].AsSpan(10)));
        Assert.Equal([1, 2, 3], peer.CursorRecords[1][39..]);
        Assert.Equal(39, peer.CursorRecords[2].Length);
    }

    [Fact]
    public void CursorDeliveryRetriesTheSameRecordWithoutANewCaptureUpdate()
    {
        using var peer = new AdaptivePeer();
        peer.CursorSendResults.Enqueue(false);
        var delivery = new ScreenViewCursorDelivery();

        delivery.Publish(peer, "first", new ScreenViewCursorUpdate(true, 1, 2, 0, 0, 4, 4, [1, 2, 3]));
        delivery.Publish(peer, "first", null);
        delivery.Publish(peer, "first", null);

        Assert.Equal(2, peer.CursorRecords.Count);
        Assert.Same(peer.CursorRecords[0], peer.CursorRecords[1]);
    }

    [Fact]
    public void CursorDeliverySeedsTheReplacementPeerForTheSameDisplayOnly()
    {
        using var previous = new AdaptivePeer();
        using var replacement = new AdaptivePeer();
        using var afterSwitch = new AdaptivePeer();
        var delivery = new ScreenViewCursorDelivery();
        var cursor = new ScreenViewCursorUpdate(true, 1, 2, 0, 0, 4, 4, [1, 2, 3]);

        delivery.Publish(previous, "first", cursor);
        delivery.Publish(replacement, "first", cursor with { X = 16, PngBytes = null });
        delivery.Publish(afterSwitch, "second", null);
        delivery.Publish(afterSwitch, "second", cursor with { PngBytes = null });

        byte[] replacementRecord = Assert.Single(replacement.CursorRecords);
        Assert.Equal([1, 2, 3], replacementRecord[39..]);
        Assert.Equal(16, BinaryPrimitives.ReadInt32BigEndian(replacementRecord.AsSpan(10)));
        Assert.Equal(39, Assert.Single(afterSwitch.CursorRecords).Length);
    }

    [Fact]
    public void CursorDeliveryKeepsOversizedShapesOutOfTheRetryQueue()
    {
        using var peer = new AdaptivePeer();
        var delivery = new ScreenViewCursorDelivery();
        var oversized = new byte[ScreenViewRecordEncoder.MaximumCursorShapeBytes + 1];

        delivery.Publish(peer, "first", new ScreenViewCursorUpdate(true, 1, 2, 0, 0, 4, 4, oversized));
        delivery.Publish(peer, "first", null);

        Assert.Equal(39, Assert.Single(peer.CursorRecords).Length);
    }

    [Fact]
    public async Task OfferPreparationDoesNotConsumeTheClientsAnswerWindow()
    {
        HostPermissionSet originalPermissions = AppPermissionSettings.Load();
        try
        {
            AppPermissionSettings.Save(originalPermissions with { AllowScreenViewing = true });
            var capture = new AdaptiveCaptureSource();
            var peer = new AdaptivePeer(true) { OfferDelay = TimeSpan.FromSeconds(6) };
            await using var fixture = await WebHostFixture.StartAsync(
                screenViewCapture: capture, screenViewPeerFactory: new AdaptivePeerFactory(peer));
            using var key = new PairingTestKey();
            using WebSocket control = await ConnectAsync(fixture.WebHost);
            await PairAsync(control, fixture.Manager, key);
            await StartAndAnswerAsync(control, key, "screen-delayed-offer", TimeSpan.FromSeconds(10));
            await peer.AcceptedFrame.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await AssertControlHealthyAsync(control);
        }
        finally { AppPermissionSettings.Save(originalPermissions); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisplayTransitionResumesVideoOnTheSamePeer(bool afterFirstFrame)
    {
        HostPermissionSet originalPermissions = AppPermissionSettings.Load();
        try
        {
            AppPermissionSettings.Save(originalPermissions with { AllowScreenViewing = true });
            var capture = new AdaptiveCaptureSource { CaptureFailuresRemaining = afterFirstFrame ? 0 : 3 };
            var peer = new AdaptivePeer(true);
            await using var fixture = await WebHostFixture.StartAsync(
                screenViewCapture: capture, screenViewPeerFactory: new AdaptivePeerFactory(peer));
            using var key = new PairingTestKey();
            using WebSocket control = await ConnectAsync(fixture.WebHost);
            await PairAsync(control, fixture.Manager, key);
            await StartAndAnswerAsync(control, key, "screen-display-transition");
            if (afterFirstFrame)
            {
                await peer.AcceptedFrame.Task.WaitAsync(TimeSpan.FromSeconds(2));
                Interlocked.Exchange(ref capture.CaptureFailuresRemaining, 3);
            }
            Assert.True(await capture.Recovered.Task.WaitAsync(TimeSpan.FromSeconds(3)));
            Assert.False(peer.Disposed.Task.IsCompleted);
            await AssertControlHealthyAsync(control);

            // A second mode change (such as stopping the movie) gets its own recovery.
            capture.Recovered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Interlocked.Exchange(ref capture.CaptureFailuresRemaining, 2);
            Assert.True(await capture.Recovered.Task.WaitAsync(TimeSpan.FromSeconds(3)));
            Assert.False(peer.Disposed.Task.IsCompleted);
            _ = await SendUntilTypeAsync(control, new { type = "screen.view.stop", operationId = "screen-transition-stop" }, "screen.view.stop.result");
            await peer.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally { AppPermissionSettings.Save(originalPermissions); }
    }

    [Fact]
    public async Task RecoveryKeepsTheLowerEncoderProfileWhenDisplayGeometryIsUnchanged()
    {
        AppPermissionSettings.Save(AppPermissionSettings.Load() with { AllowScreenViewing = true });
        var capture = new AdaptiveCaptureSource { CaptureFailuresRemaining = 1, EncoderFailuresRemaining = 1 };
        var peer = new AdaptivePeer(true);
        await using var fixture = await WebHostFixture.StartAsync(
            screenViewCapture: capture, screenViewPeerFactory: new AdaptivePeerFactory(peer));
        using var key = new PairingTestKey();
        using WebSocket control = await ConnectAsync(fixture.WebHost);
        await PairAsync(control, fixture.Manager, key);
        await StartAndAnswerAsync(control, key, "screen-recovery-profile");
        await peer.AcceptedFrame.Task.WaitAsync(TimeSpan.FromSeconds(2));

        ScreenViewCaptureProfile[] attempts = [.. capture.Profiles.Take(3)];
        Assert.Equal(3, attempts.Length);
        Assert.Equal(attempts[0], attempts[1]);
        Assert.NotEqual(attempts[1], attempts[2]);
        Assert.False(peer.Disposed.Task.IsCompleted);
        await AssertControlHealthyAsync(control);
    }

    [Fact]
    public async Task RecoveryWaitsForAnAcceptedKeyFrameBeforeSendingDependentFrames()
    {
        AppPermissionSettings.Save(AppPermissionSettings.Load() with { AllowScreenViewing = true });
        int captured = 0;
        var capture = new AdaptiveCaptureSource
        {
            CaptureFailuresRemaining = 1,
            CaptureOverride = async (_, profile, _, token) =>
            {
                int index = ++captured;
                if (index > 4) await Task.Delay(Timeout.InfiniteTimeSpan, token);
                bool keyFrame = index % 2 == 0;
                return new([0, 0, 0, 1, keyFrame ? (byte)0x65 : (byte)0x41, checked((byte)index)],
                    profile.MaxWidth, profile.MaxHeight, profile.FramesPerSecond, keyFrame);
            }
        };
        var peer = new AdaptivePeer(false, true) { TrackVideoSends = true };
        await using var fixture = await WebHostFixture.StartAsync(
            screenViewCapture: capture, screenViewPeerFactory: new AdaptivePeerFactory(peer));
        using var key = new PairingTestKey();
        using WebSocket control = await ConnectAsync(fixture.WebHost);
        await PairAsync(control, fixture.Manager, key);
        await StartAndAnswerAsync(control, key, "screen-recovery-keyframe");
        await peer.AcceptedFrame.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(new byte[] { 2, 4 }, peer.SentVideoMarkers.ToArray());
        Assert.All(capture.ForceKeyFrameRequests.Take(5), requested => Assert.True(requested));
        await AssertControlHealthyAsync(control);
    }

    [Fact]
    public async Task RecoveryDoesNotReleaseADragWhenTheDesktopBoundsDidNotChange()
    {
        AppPermissionSettings.Save(AppPermissionSettings.Load() with { AllowScreenViewing = true, AllowRemoteInput = true });
        var waitingForVideo = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var nextCapture = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowEmptyFrame = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var capture = new AdaptiveCaptureSource
        {
            CaptureFailuresRemaining = 1,
            CaptureOverride = async (_, _, _, token) =>
            {
                if (waitingForVideo.TrySetResult())
                {
                    await allowEmptyFrame.Task.WaitAsync(token);
                    return null;
                }
                nextCapture.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return null;
            }
        };
        var peer = new AdaptivePeer(true);
        await using var fixture = await WebHostFixture.StartAsync(
            screenViewCapture: capture, screenViewPeerFactory: new AdaptivePeerFactory(peer));
        using var key = new PairingTestKey();
        using WebSocket control = await ConnectAsync(fixture.WebHost);
        await PairAsync(control, fixture.Manager, key);
        await StartAndAnswerAsync(control, key, "screen-recovery-drag");
        await waitingForVideo.Task.WaitAsync(TimeSpan.FromSeconds(2));
        _ = await SendUntilTypeAsync(control, new
        {
            type = "screen.pointer.button",
            seq = 41,
            displayId = "display-1",
            x = 0.25,
            y = 0.75,
            button = "left",
            action = "down"
        }, "input.ack");
        allowEmptyFrame.TrySetResult();
        await nextCapture.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.DoesNotContain("ReleaseMouseButtons", fixture.InputInjector.Events);
        _ = await SendUntilTypeAsync(control, new { type = "screen.view.stop", operationId = "screen-recovery-drag-stop" }, "screen.view.stop.result");
        Assert.Contains("ReleaseMouseButtons", fixture.InputInjector.Events);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task APreviousSelectionCannotPublishResultsAfterSwitchingAwayAndBack(bool lateFailure)
    {
        AppPermissionSettings.Save(AppPermissionSettings.Load() with { AllowScreenViewing = true });
        var pending = new TaskCompletionSource<ScreenViewEncodedFrame?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var captureStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int attempts = 0;
        var capture = new AdaptiveCaptureSource
        {
            Sources = [new("display-1", "Display 1", 1920, 1080, true), new("display-2", "Display 2", 1024, 768, false, 1920, 0)],
            CaptureOverride = async (_, profile, _, token) =>
            {
                int attempt = ++attempts;
                if (attempt == 1)
                {
                    captureStarted.TrySetResult();
                    return await pending.Task.WaitAsync(token);
                }
                if (attempt > 2) await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return new([0, 0, 0, 1, 0x65, 2], profile.MaxWidth, profile.MaxHeight, profile.FramesPerSecond, true);
            }
        };
        var peer = new AdaptivePeer(true) { TrackVideoSends = true };
        await using var fixture = await WebHostFixture.StartAsync(
            screenViewCapture: capture, screenViewPeerFactory: new AdaptivePeerFactory(peer));
        using var key = new PairingTestKey();
        using WebSocket control = await ConnectAsync(fixture.WebHost);
        await PairAsync(control, fixture.Manager, key);
        await StartAndAnswerAsync(control, key, "screen-display-generation");
        await captureStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        foreach (string displayId in new[] { "display-2", "display-1" })
        {
            JsonElement switched = await SendUntilTypeAsync(control, new
            {
                type = "screen.view.source.set",
                operationId = $"switch-{displayId}",
                displayId
            }, "screen.view.source.result");
            Assert.True(switched.GetProperty("succeeded").GetBoolean());
        }
        Assert.False(capture.Ended.Task.IsCompleted);

        if (lateFailure) pending.TrySetException(new ScreenViewCaptureException("capture-failed", "Previous selection failed."));
        else pending.TrySetResult(new([0, 0, 0, 1, 0x65, 1], 1920, 1080, 30, true));

        await peer.AcceptedFrame.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.True(capture.Ended.Task.IsCompletedSuccessfully);
        Assert.Equal((byte)2, Assert.Single(peer.SentVideoMarkers));
        Assert.Equal([true, true], capture.ForceKeyFrameRequests.Take(2).ToArray());
        Assert.False(peer.Disposed.Task.IsCompleted);
        await AssertControlHealthyAsync(control);
    }

    [Fact]
    public async Task AFrameThatCompletesAfterStopIsNeverSent()
    {
        AppPermissionSettings.Save(AppPermissionSettings.Load() with { AllowScreenViewing = true });
        var captureStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopping = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lateFrame = new TaskCompletionSource<ScreenViewEncodedFrame?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var capture = new AdaptiveCaptureSource
        {
            CaptureOverride = async (_, _, _, token) =>
            {
                using CancellationTokenRegistration registration = token.Register(() => stopping.TrySetResult());
                captureStarted.TrySetResult();
                return await lateFrame.Task.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            }
        };
        var peer = new AdaptivePeer(true) { TrackVideoSends = true };
        await using var fixture = await WebHostFixture.StartAsync(
            screenViewCapture: capture, screenViewPeerFactory: new AdaptivePeerFactory(peer));
        using var key = new PairingTestKey();
        using WebSocket control = await ConnectAsync(fixture.WebHost);
        await PairAsync(control, fixture.Manager, key);
        await StartAndAnswerAsync(control, key, "screen-stop-inflight");
        await captureStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Task<JsonElement> stop = SendUntilTypeAsync(
            control, new { type = "screen.view.stop", operationId = "stop-inflight" }, "screen.view.stop.result");
        await stopping.Task.WaitAsync(TimeSpan.FromSeconds(2));
        lateFrame.TrySetResult(new([0, 0, 0, 1, 0x65, 1], 1920, 1080, 30, true));
        Assert.True((await stop).GetProperty("succeeded").GetBoolean());
        await peer.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Empty(peer.SentVideoMarkers);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DisplayRecoveryStopsOnCancellationOrPermanentFailure(bool recoverable)
    {
        HostPermissionSet originalPermissions = AppPermissionSettings.Load();
        try
        {
            AppPermissionSettings.Save(originalPermissions with { AllowScreenViewing = true });
            var capture = new AdaptiveCaptureSource { CaptureFailuresRemaining = int.MaxValue, RecoverableFailure = recoverable };
            var peer = new AdaptivePeer(true);
            await using var fixture = await WebHostFixture.StartAsync(
                screenViewCapture: capture, screenViewPeerFactory: new AdaptivePeerFactory(peer));
            using var key = new PairingTestKey();
            using WebSocket control = await ConnectAsync(fixture.WebHost);
            await PairAsync(control, fixture.Manager, key);
            await StartAndAnswerAsync(control, key, "screen-display-stop");
            await capture.Failed.Task.WaitAsync(TimeSpan.FromSeconds(2));
            if (recoverable)
                _ = await SendUntilTypeAsync(control, new { type = "screen.view.stop", operationId = "screen-recovery-stop" }, "screen.view.stop.result");
            await peer.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(peer.AcceptedFrame.Task.IsCompleted);
            await AssertControlHealthyAsync(control);
        }
        finally { AppPermissionSettings.Save(originalPermissions); }
    }

    [Fact]
    public async Task DisplayThatNeverReturnsExhaustsRecoveryAndReleasesPeer()
    {
        HostPermissionSet originalPermissions = AppPermissionSettings.Load();
        try
        {
            AppPermissionSettings.Save(originalPermissions with { AllowScreenViewing = true });
            var capture = new AdaptiveCaptureSource { CaptureFailuresRemaining = int.MaxValue };
            var peer = new AdaptivePeer(true);
            await using var fixture = await WebHostFixture.StartAsync(
                screenViewCapture: capture, screenViewPeerFactory: new AdaptivePeerFactory(peer));
            using var key = new PairingTestKey();
            using WebSocket control = await ConnectAsync(fixture.WebHost);
            await PairAsync(control, fixture.Manager, key);
            await StartAndAnswerAsync(control, key, "screen-display-timeout");
            await capture.Failed.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await AssertControlHealthyAsync(control);
            await Task.Delay(500);
            Assert.False(peer.Disposed.Task.IsCompleted);
            await peer.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(12));
            Assert.InRange(capture.ForceKeyFrameRequests.Count, 2, 45);
            await AssertControlHealthyAsync(control);
        }
        finally { AppPermissionSettings.Save(originalPermissions); }
    }

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
    public async Task VideoStartsWhileSystemAudioEndpointInitializationIsBlocked()
    {
        AppPermissionSettings.Save(AppPermissionSettings.Load() with { AllowScreenViewing = true });
        using var finishAudioStartup = new ManualResetEventSlim();
        var audioStartupEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var capture = new AdaptiveCaptureSource();
        var peer = new AdaptivePeer(true) { TrackVideoSends = true };
        var audio = new DeferredAudioStartupFactory(() =>
        {
            audioStartupEntered.TrySetResult();
            finishAudioStartup.Wait(TimeSpan.FromSeconds(5));
        });
        await using var fixture = await WebHostFixture.StartAsync(
            screenViewCapture: capture, screenViewPeerFactory: new AdaptivePeerFactory(peer),
            screenViewAudioCaptureFactory: audio);
        using var key = new PairingTestKey();
        using WebSocket control = await ConnectAsync(fixture.WebHost);
        await PairAsync(control, fixture.Manager, key);

        try
        {
            await StartAndAnswerAsync(control, key, "screen-slow-audio");
            await audioStartupEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await peer.AcceptedFrame.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await AssertControlHealthyAsync(control);
        }
        finally
        {
            finishAudioStartup.Set();
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

    [Fact]
    public async Task ScreenshotWaitsForTheActivePeerToConnect()
    {
        HostPermissionSet originalPermissions = AppPermissionSettings.Load();
        try
        {
            AppPermissionSettings.Save(originalPermissions with { AllowScreenViewing = true, AllowFileTransfer = true });
            var connection = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var peer = new AdaptivePeer { Connected = connection.Task };
            await using var fixture = await WebHostFixture.StartAsync(
                screenViewCapture: new AdaptiveCaptureSource(), screenViewPeerFactory: new AdaptivePeerFactory(peer));
            using var key = new PairingTestKey();
            using WebSocket control = await ConnectAsync(fixture.WebHost);
            await PairAsync(control, fixture.Manager, key);
            await StartAndAnswerAsync(control, key, "screen-awaiting-media");

            JsonElement response = await SendUntilTypeAsync(control,
                ScreenshotRequest(fixture.Manager, key, "screen-awaiting-media"), "file.transfer.start.result");

            Assert.False(response.GetProperty("succeeded").GetBoolean());
            Assert.Equal("screen-session-stale", response.GetProperty("code").GetString());
        }
        finally { AppPermissionSettings.Save(originalPermissions); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InFlightScreenshotRetiresWhenTheMirrorStopsOrChangesDisplay(bool stop)
    {
        HostPermissionSet originalPermissions = AppPermissionSettings.Load();
        var captureStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishCapture = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var screenshot = new MemoryStream([137, 80, 78, 71], writable: false);
        try
        {
            AppPermissionSettings.Save(originalPermissions with { AllowScreenViewing = true, AllowFileTransfer = true });
            var capture = new AdaptiveCaptureSource
            {
                Sources = [new("display-1", "First", 800, 600, true), new("display-2", "Second", 800, 600, false)],
                ScreenshotOverride = (_, token) =>
                {
                    using var registration = token.Register(() => canceled.TrySetResult());
                    captureStarted.TrySetResult();
                    finishCapture.Task.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None).GetAwaiter().GetResult();
                    return new(screenshot, screenshot.Length, 800, 600);
                }
            };
            await using var fixture = await WebHostFixture.StartAsync(screenViewCapture: capture);
            using var key = new PairingTestKey();
            using WebSocket control = await ConnectAsync(fixture.WebHost);
            await PairAsync(control, fixture.Manager, key);
            await StartAndAnswerAsync(control, key, "screen-capture-inflight");
            await capture.Captured.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await SendAsync(control, ScreenshotRequest(fixture.Manager, key, "screen-capture-inflight"));
            try
            {
                await captureStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
                if (stop)
                {
                    JsonElement stopped = await SendUntilTypeAsync(control,
                        new { type = "screen.view.stop", operationId = "stop-during-screenshot" }, "screen.view.stop.result");
                    Assert.True(stopped.GetProperty("succeeded").GetBoolean());
                    await canceled.Task.WaitAsync(TimeSpan.FromSeconds(2));
                }
                else
                {
                    foreach (string displayId in new[] { "display-2", "display-1" })
                    {
                        JsonElement changed = await SendUntilTypeAsync(control,
                            new { type = "screen.view.source.set", operationId = $"switch-to-{displayId}", displayId }, "screen.view.source.result");
                        Assert.True(changed.GetProperty("succeeded").GetBoolean());
                    }
                }
            }
            finally { finishCapture.TrySetResult(); }

            JsonElement response = await ReceiveUntilTypeAsync(control, "file.transfer.start.result");
            Assert.False(response.GetProperty("succeeded").GetBoolean());
            Assert.Equal("screen-session-stale", response.GetProperty("code").GetString());
            Assert.False(screenshot.CanRead);
        }
        finally { AppPermissionSettings.Save(originalPermissions); }
    }

    [Fact]
    public async Task NativePixelSizeChangeRetiresTheScreenshotBeforeOfferingATransfer()
    {
        AppPermissionSettings.Save(AppPermissionSettings.Load() with { AllowScreenViewing = true, AllowFileTransfer = true });
        using var screenshot = new MemoryStream([137, 80, 78, 71], writable: false);
        var capture = new AdaptiveCaptureSource
        {
            Sources = [new("display-1", "First", 800, 600, true)],
            ScreenshotOverride = (_, _) => new(screenshot, screenshot.Length, 1024, 768)
        };
        await using var fixture = await WebHostFixture.StartAsync(screenViewCapture: capture);
        using var key = new PairingTestKey();
        using WebSocket control = await ConnectAsync(fixture.WebHost);
        await PairAsync(control, fixture.Manager, key);
        await StartAndAnswerAsync(control, key, "screen-geometry-change");
        await capture.Captured.Task.WaitAsync(TimeSpan.FromSeconds(2));

        JsonElement response = await SendUntilTypeAsync(control,
            ScreenshotRequest(fixture.Manager, key, "screen-geometry-change"), "file.transfer.start.result");

        Assert.False(response.GetProperty("succeeded").GetBoolean());
        Assert.Equal("screen-session-stale", response.GetProperty("code").GetString());
        Assert.False(screenshot.CanRead);
        await AssertControlHealthyAsync(control);
    }

    [Theory]
    [InlineData("stop")]
    [InlineData("switch")]
    [InlineData("complete")]
    public async Task ScreenshotTransferFollowsTheLiveDisplayUntilTheLastAcknowledgement(string change)
    {
        AppPermissionSettings.Save(AppPermissionSettings.Load() with { AllowScreenViewing = true, AllowFileTransfer = true });
        await using var screenshot = new MemoryStream([137, 80, 78, 71], writable: false);
        var capture = new AdaptiveCaptureSource
        {
            Sources = [new("display-1", "First", 800, 600, true), new("display-2", "Second", 800, 600, false)],
            ScreenshotOverride = (_, _) => new(screenshot, screenshot.Length, 800, 600)
        };
        var peer = new ScreenshotTransferPeer();
        await using var fixture = await WebHostFixture.StartAsync(screenViewCapture: capture, fileTransferPeerFactory: peer);
        using var key = new PairingTestKey();
        using WebSocket control = await ConnectAsync(fixture.WebHost);
        await PairAsync(control, fixture.Manager, key);
        const string screenOperation = "screen-transfer-lifetime";
        await StartAndAnswerAsync(control, key, screenOperation);
        await capture.Captured.Task.WaitAsync(TimeSpan.FromSeconds(2));
        JsonElement started = await SendUntilTypeAsync(control,
            ScreenshotRequest(fixture.Manager, key, screenOperation), "file.transfer.start.result");
        Assert.True(started.GetProperty("succeeded").GetBoolean());
        string transferId = started.GetProperty("transferId").GetString()!;
        JsonElement offer = await ReceiveUntilTypeAsync(control, "file.transfer.offer");
        const string answerSdp = "v=0\r\ns=screenshot-answer\r\n";
        string transcript = FileTransferNegotiation.AnswerTranscript("client-screen-adaptive", fixture.Manager.HostIdentity.PublicKey,
            $"{screenOperation}-still", transferId, "download", offer.GetProperty("fileName").GetString()!,
            offer.GetProperty("declaredSize").GetInt64(), FileTransferNegotiation.HashSdp(offer.GetProperty("offerSdp").GetString()!),
            FileTransferNegotiation.HashSdp(answerSdp));
        JsonElement answer = await SendUntilTypeAsync(control, new
        {
            type = "file.transfer.answer",
            operationId = "answer-screenshot-transfer",
            transferId,
            answerSdp,
            clientSignature = key.SignPayload(transcript)
        }, "file.transfer.answer.result");
        Assert.True(answer.GetProperty("succeeded").GetBoolean());
        await peer.DataSent.Task.WaitAsync(TimeSpan.FromSeconds(2));

        if (change == "complete")
            peer.Acknowledge(screenshot.Length);
        else if (change == "stop")
            await SendAsync(control, new { type = "screen.view.stop", operationId = "stop-screen-transfer" });
        else
            await SendAsync(control, new { type = "screen.view.source.set", operationId = "switch-screen-transfer", displayId = "display-2" });

        JsonElement result = await ReceiveUntilTypeAsync(control, "file.transfer.result");
        Assert.Equal(transferId, result.GetProperty("transferId").GetString());
        if (change == "complete")
        {
            Assert.True(result.GetProperty("succeeded").GetBoolean());
            _ = await SendUntilTypeAsync(control, new { type = "screen.view.stop", operationId = "stop-after-screenshot" }, "screen.view.stop.result");
        }
        else
        {
            Assert.False(result.GetProperty("succeeded").GetBoolean());
            Assert.Equal("screen-session-stale", result.GetProperty("code").GetString());
        }
        await peer.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await AssertControlHealthyAsync(control);
    }

    private static object ScreenshotRequest(PairingManager manager, PairingTestKey key, string screenOperationId)
    {
        const string displayId = "display-1";
        string operationId = $"{screenOperationId}-still";
        return new
        {
            type = "file.transfer.start",
            operationId,
            direction = "download",
            source = "screen-capture",
            screenOperationId,
            displayId,
            clientSignature = key.SignPayload(FileTransferNegotiation.ScreenCaptureStartTranscript(
                "client-screen-adaptive", manager.HostIdentity.PublicKey, operationId, screenOperationId, displayId))
        };
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

    private static async Task StartAndAnswerAsync(WebSocket control, PairingTestKey key, string operationId, TimeSpan? answerDelay = null)
    {
        const string clientId = "client-screen-adaptive";
        const string displayId = "display-1";
        JsonElement start = await SendUntilTypeAsync(control, new
        {
            type = "screen.view.start",
            operationId,
            displayId,
            clientSignature = key.SignPayload($"VolturaAir screen-view:start:v2:{clientId}:{operationId}:{displayId}")
        }, "screen.view.start.result", TimeSpan.FromSeconds(12));
        if (answerDelay is { } delay) await Task.Delay(delay);
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

    private static async Task<JsonElement> SendUntilTypeAsync(WebSocket socket, object payload, string expectedType, TimeSpan? timeout = null)
    {
        await SendAsync(socket, payload);
        return await ReceiveUntilTypeAsync(socket, expectedType, timeout);
    }

    private static async Task<JsonElement> ReceiveUntilTypeAsync(WebSocket socket, string expectedType, TimeSpan? timeout = null)
    {
        for (int attempt = 0; attempt < 8; attempt++)
        {
            using var cancellation = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(2));
            string text = await ReceiveTextAsync(socket, cancellation.Token);
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
        public int CaptureFailuresRemaining;
        public bool RecoverableFailure { get; init; } = true;
        public IReadOnlyList<ScreenViewSource> Sources { get; init; } = [new("display-1", "Display 1", 1920, 1080, true)];
        public Func<string, ScreenViewCaptureProfile, bool, CancellationToken, Task<ScreenViewEncodedFrame?>>? CaptureOverride { get; init; }
        public Func<string, CancellationToken, ScreenViewScreenshot>? ScreenshotOverride { get; init; }
        private bool _recovering;
        public TaskCompletionSource Failed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> Recovered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Captured { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Ended { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public System.Collections.Concurrent.ConcurrentQueue<bool> ForceKeyFrameRequests { get; } = new();
        public System.Collections.Concurrent.ConcurrentQueue<ScreenViewCaptureProfile> Profiles { get; } = new();
        public System.Collections.Concurrent.ConcurrentQueue<int> Bitrates { get; } = new();
        public IReadOnlyList<ScreenViewSource> GetSources() => Sources;
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
            if (Interlocked.Decrement(ref CaptureFailuresRemaining) >= 0)
            {
                _recovering = true;
                Failed.TrySetResult();
                throw new ScreenViewCaptureException(RecoverableFailure ? "capture-device-lost" : "protected-content", "Injected capture transition.")
                {
                    CanRetryCapture = RecoverableFailure
                };
            }
            if (_recovering)
            {
                _recovering = false;
                Recovered.TrySetResult(forceKeyFrame);
            }
            if (Interlocked.Decrement(ref EncoderFailuresRemaining) >= 0)
                throw new ScreenViewCaptureException("encoder-failed", "Injected encoder sample failure.");
            if (CaptureOverride is not null)
                return CaptureOverride(sourceId, profile, forceKeyFrame, cancellationToken);
            Captured.TrySetResult();
            return Task.FromResult<ScreenViewEncodedFrame?>(new([0, 0, 0, 1, 0x65, 1], profile.MaxWidth, profile.MaxHeight, profile.FramesPerSecond, true));
        }
        public void EndCapture() => Ended.TrySetResult();
        public ScreenViewScreenshot CaptureScreenshot(string sourceId, CancellationToken cancellationToken) =>
            ScreenshotOverride?.Invoke(sourceId, cancellationToken) ?? throw new NotSupportedException();
    }

    private sealed class ScreenshotTransferPeer : IFileTransferWebRtcPeerFactory, IFileTransferWebRtcPeer
    {
        private readonly Channel<byte[]> _messages = Channel.CreateBounded<byte[]>(1);
        public TaskCompletionSource DataSent { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Opened => Task.CompletedTask;
        public ChannelReader<byte[]> Messages => _messages.Reader;
        public IFileTransferWebRtcPeer Create(FileTransferPeerConfiguration? configuration) => this;
        public Task<string> CreateOfferAsync(CancellationToken cancellationToken) => Task.FromResult("v=0\r\ns=screenshot-offer\r\n");
        public void ApplyAnswer(string answerSdp) { }
        public bool TrySend(byte[] record)
        {
            DataSent.TrySetResult();
            return true;
        }
        public void Acknowledge(long size) => Assert.True(_messages.Writer.TryWrite(FileTransferProtocol.CreateAcknowledgement(size)));
        public ValueTask DisposeAsync()
        {
            _messages.Writer.TryComplete();
            Disposed.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class AdaptivePeerFactory(AdaptivePeer peer) : IScreenViewWebRtcPeerFactory
    {
        public IScreenViewWebRtcPeer Create() => peer;
    }

    private sealed class AdaptivePeer(params bool[] sendResults) : IScreenViewWebRtcPeer
    {
        public TimeSpan OfferDelay { get; init; }
        public bool TrackVideoSends { get; init; }
        private readonly Queue<bool> _sendResults = new(sendResults);
        public event EventHandler? Stopped;
        public event EventHandler? AudioStopped;
        public event EventHandler? KeyFrameRequested;
        public Task Connected { get; init; } = Task.CompletedTask;
        public TaskCompletionSource AcceptedFrame { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AudioUnavailable { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Queue<bool> CursorSendResults { get; } = new();
        public List<byte[]> CursorRecords { get; } = [];
        public System.Collections.Concurrent.ConcurrentQueue<byte> SentVideoMarkers { get; } = new();
        public async Task<string> CreateOfferAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(OfferDelay, cancellationToken);
            return "v=0\r\no=voltura 1 1 IN IP4 127.0.0.1\r\ns=offer\r\nt=0 0\r\n";
        }
        public void ApplyAnswer(string answerSdp) { }
        public bool TrySendH264(byte[] accessUnit, int framesPerSecond)
        {
            if (TrackVideoSends) SentVideoMarkers.Enqueue(accessUnit[^1]);
            bool accepted = _sendResults.Count == 0 || _sendResults.Dequeue();
            if (accepted) AcceptedFrame.TrySetResult();
            return accepted;
        }
        public bool TrySendOpus(byte[] packet, uint rtpTimestamp) => packet.Length > 0 && rtpTimestamp > 0;
        public bool TrySendEvent(byte[] eventBytes)
        {
            if (eventBytes is [7, 0, ..]) AudioUnavailable.TrySetResult();
            if (eventBytes is [4, ..])
            {
                CursorRecords.Add(eventBytes);
                return CursorSendResults.Count == 0 || CursorSendResults.Dequeue();
            }
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

    private sealed class DeferredAudioStartupFactory(Action waitForStartup) : IScreenViewSystemAudioCaptureFactory
    {
        public IScreenViewSystemAudioCapture Create()
        {
            waitForStartup();
            return new UnavailableScreenViewSystemAudioCaptureFactory().Create();
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
