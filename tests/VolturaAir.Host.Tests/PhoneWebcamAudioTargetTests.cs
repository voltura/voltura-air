using VolturaAir.Host.Features.PhoneWebcam;

namespace VolturaAir.Host.Tests;

public sealed class PhoneWebcamAudioTargetTests
{
    [Theory]
    [InlineData((int)PhoneWebcamAudioTargetState.Ready, 0)]
    [InlineData((int)PhoneWebcamAudioTargetState.InstalledButUnavailable, 10)]
    [InlineData((int)PhoneWebcamAudioTargetState.NotInstalled, 20)]
    [InlineData((int)PhoneWebcamAudioTargetState.DetectionFailed, 30)]
    public async Task InstallerProbeMapsEveryBoundedDetectorResult(int state, int expected) =>
        Assert.Equal(expected, await Program.GetPhoneMicrophoneStatusExitCodeAsync(
            () => Task.FromResult((PhoneWebcamAudioTargetState)state),
            TimeSpan.FromSeconds(1)));

    [Fact]
    public async Task InstallerProbeTimesOutAsDetectionFailure()
    {
        var pending = new TaskCompletionSource<PhoneWebcamAudioTargetState>(TaskCreationOptions.RunContinuationsAsynchronously);

        int result = await Program.GetPhoneMicrophoneStatusExitCodeAsync(
            () => pending.Task,
            TimeSpan.FromMilliseconds(10));

        Assert.Equal(30, result);
    }

    [Fact]
    public void ResolvesTheExactActiveBaseCableEndpoint()
    {
        PhoneWebcamAudioTargetStatus status = PhoneWebcamAudioTarget.Classify([
            new("disabled", false, "CABLE Input (VB-Audio Virtual Cable)", "VB-Audio Virtual Cable"),
            new("ready", true, "Renamed by user", "VB-Audio Virtual Cable")]);

        Assert.Equal(PhoneWebcamAudioTargetState.Ready, status.State);
        Assert.Equal("ready", status.EndpointId);
    }

    [Fact]
    public void DistinguishesUnavailableFromAbsent()
    {
        Assert.Equal(
            PhoneWebcamAudioTargetState.InstalledButUnavailable,
            PhoneWebcamAudioTarget.Classify([
                new("disabled", false, "CABLE Input (VB-Audio Virtual Cable)", "VB-Audio Virtual Cable")]).State);
        Assert.Equal(
            PhoneWebcamAudioTargetState.NotInstalled,
            PhoneWebcamAudioTarget.Classify([
                new("speakers", true, "Speakers", "Audio device")]).State);
    }

    [Fact]
    public void RecognizesTheBaseCableCaptureEndpointForAudioTesting()
    {
        Assert.True(PhoneWebcamAudioTarget.IsBaseCableIdentity(
            "CABLE Output (VB-Audio Virtual Cable)",
            "VB-Audio Virtual Cable"));
        Assert.False(PhoneWebcamAudioTarget.IsBaseCableIdentity("Microphone", "USB audio device"));
    }

    [Fact]
    public void EnumerationFailureIsNotReportedAsAbsence()
    {
        var target = new PhoneWebcamAudioTarget(static () => throw new InvalidOperationException("Injected failure."));

        Assert.Equal(PhoneWebcamAudioTargetState.DetectionFailed, target.Refresh().State);
    }

    [Fact]
    public async Task OlderOverlappingRefreshCannotOverwriteNewerResult()
    {
        using var firstStarted = new ManualResetEventSlim();
        using var releaseFirst = new ManualResetEventSlim();
        int calls = 0;
        var target = new PhoneWebcamAudioTarget(() =>
        {
            if (Interlocked.Increment(ref calls) != 1) return [];
            firstStarted.Set();
            Assert.True(releaseFirst.Wait(TimeSpan.FromSeconds(5)));
            return [new PhoneWebcamAudioEndpoint(
                "ready",
                true,
                "CABLE Input (VB-Audio Virtual Cable)",
                "VB-Audio Virtual Cable")];
        });

        Task<PhoneWebcamAudioTargetStatus> older = Task.Run(target.Refresh);
        Assert.True(firstStarted.Wait(TimeSpan.FromSeconds(5)));
        PhoneWebcamAudioTargetStatus newer = await Task.Run(target.Refresh);
        releaseFirst.Set();

        Assert.Equal(PhoneWebcamAudioTargetState.NotInstalled, newer.State);
        Assert.Equal(PhoneWebcamAudioTargetState.NotInstalled, (await older).State);
        Assert.Equal(PhoneWebcamAudioTargetState.NotInstalled, target.Status.State);
    }

    [Fact]
    public async Task FeatureRefreshTimesOutAndDiscardsTheLateDetectorResult()
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int calls = 0;
        var target = new PhoneWebcamAudioTarget(() =>
        {
            Interlocked.Increment(ref calls);
            started.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(5)));
            return [new PhoneWebcamAudioEndpoint(
                "ready",
                true,
                "CABLE Input (VB-Audio Virtual Cable)",
                "VB-Audio Virtual Cable")];
        });
        await using var feature = new PhoneWebcamFeature(
            new InstalledSetup(),
            audioTarget: target,
            audioTargetRefreshTimeout: TimeSpan.FromMilliseconds(20));

        try
        {
            Task<PhoneWebcamAudioTargetStatus> refresh = feature.RefreshAudioTargetAsync();
            Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
            Assert.Equal(PhoneWebcamAudioTargetState.DetectionFailed, (await refresh).State);
            Assert.Equal(
                PhoneWebcamAudioTargetState.DetectionFailed,
                (await feature.RefreshAudioTargetAsync()).State);
            Assert.Equal(1, Volatile.Read(ref calls));
        }
        finally
        {
            release.Set();
        }

        await Task.Delay(50);
        Assert.Equal(PhoneWebcamAudioTargetState.DetectionFailed, feature.AudioTargetStatus.State);
    }

    [Fact]
    public async Task EndpointDisappearingBeforeOpenPublishesCapabilityChange()
    {
        int enumeration = 0;
        var target = new PhoneWebcamAudioTarget(() => ++enumeration == 1
            ? [new PhoneWebcamAudioEndpoint("ready", true, "CABLE Input (VB-Audio Virtual Cable)", "VB-Audio Virtual Cable")]
            : []);
        await using var feature = new PhoneWebcamFeature(new InstalledSetup(), audioTarget: target);
        int changes = 0;
        feature.StatusChanged += (_, _) => changes++;
        Assert.True(target.Refresh().IsReady);
        changes = 0;

        Assert.Throws<InvalidOperationException>(() => target.OpenReadyEndpoint());

        Assert.Equal(PhoneWebcamAudioTargetState.NotInstalled, feature.AudioTargetStatus.State);
        Assert.Equal(1, changes);
    }

    [Fact]
    public async Task OpenBoundaryFailureClearsReadyCapability()
    {
        var target = new PhoneWebcamAudioTarget(
            () => [new PhoneWebcamAudioEndpoint("ready", true, "CABLE Input (VB-Audio Virtual Cable)", "VB-Audio Virtual Cable")],
            static _ => throw new InvalidOperationException("Injected open failure."));
        await using var feature = new PhoneWebcamFeature(new InstalledSetup(), audioTarget: target);
        int changes = 0;
        feature.StatusChanged += (_, _) => changes++;
        Assert.True(target.Refresh().IsReady);
        changes = 0;

        Assert.Throws<InvalidOperationException>(() => target.OpenReadyEndpoint());

        Assert.Equal(PhoneWebcamAudioTargetState.DetectionFailed, feature.AudioTargetStatus.State);
        Assert.Equal(1, changes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2049)]
    [InlineData(65536)]
    public void RejectsOversizedAudioRtpBeforeNativeCopy(int size) =>
        Assert.Null(PhoneWebcamWebRtcPeer.CopyAudioRtpPacket(nint.Zero, size));

    [Theory]
    [InlineData("m=video 9 UDP/TLS/RTP/SAVPF 102\na=rtpmap:102 H264/90000\na=sendonly\n", false, true)]
    [InlineData("m=video 9 UDP/TLS/RTP/SAVPF 102\na=rtpmap:102 H264/90000\na=sendonly\nm=audio 9 UDP/TLS/RTP/SAVPF 111\na=rtpmap:111 opus/48000/2\na=sendonly\n", true, true)]
    [InlineData("m=video 9 UDP/TLS/RTP/SAVPF 102\na=rtpmap:102 H264/90000\na=sendonly\nm=audio 9 UDP/TLS/RTP/SAVPF 111\na=rtpmap:111 opus/48000/2\na=sendonly\n", false, false)]
    [InlineData("m=video 9 UDP/TLS/RTP/SAVPF 102 96\na=rtpmap:102 H264/90000\na=rtpmap:96 VP8/90000\na=sendonly\n", false, false)]
    [InlineData("m=video 9 UDP/TLS/RTP/SAVPF 102\na=rtpmap:102 H264/90000\na=sendonly\nm=audio 9 UDP/TLS/RTP/SAVPF 111 0\na=rtpmap:111 opus/48000/2\na=rtpmap:0 PCMU/8000\na=sendonly\n", true, false)]
    [InlineData("m=video 9 UDP/TLS/RTP/SAVPF 96\na=rtpmap:102 H264/90000\na=sendonly\n", false, false)]
    [InlineData("m=video 9 UDP/TLS/RTP/SAVPF 102\na=rtpmap:102 H264/90000\na=sendonly\nm=audio 0 UDP/TLS/RTP/SAVPF 111\na=rtpmap:111 opus/48000/2\na=sendonly\n", true, false)]
    [InlineData("m=video 9 UDP/TLS/RTP/SAVPF 102\na=rtpmap:102 H264/90000\na=sendonly\nm=audio 9 UDP/TLS/RTP/SAVPF 111\na=rtpmap:111 opus/48000/2\na=inactive\n", true, false)]
    [InlineData("m=video 9 UDP/TLS/RTP/SAVPF 102\na=rtpmap:102 H264/90000\na=sendonly\na=inactive\n", false, false)]
    [InlineData("m=video 9 UDP/TLS/RTP/SAVPF 102\na=rtpmap:102 H264/90000\na=sendonly\na=recvonly\n", false, false)]
    [InlineData("m=video 9 UDP/TLS/RTP/SAVPF 102\na=rtpmap:102 H264/90000\na=sendonly\nm=video 9 UDP/TLS/RTP/SAVPF 102\na=rtpmap:102 H264/90000\na=sendonly\n", false, false)]
    public void RequiresExactRequestedMedia(string sdp, bool useMicrophone, bool expected) =>
        Assert.Equal(expected, PhoneWebcamWebRtcPeer.HasExpectedMedia(sdp, useMicrophone));

    [Theory]
    [InlineData("m=video 9 UDP/TLS/RTP/SAVPF 102\na=ssrc:10 cname:video\nm=audio 9 UDP/TLS/RTP/SAVPF 111\na=ssrc:20 cname:audio\n", true, true)]
    [InlineData("m=video 9 UDP/TLS/RTP/SAVPF 102\na=ssrc:10 cname:video\na=ssrc:10 msid:stream video\nm=audio 9 UDP/TLS/RTP/SAVPF 111\na=ssrc:20 cname:audio\na=ssrc:20 msid:stream audio\n", true, true)]
    [InlineData("m=video 9 UDP/TLS/RTP/SAVPF 102\na=ssrc:10 cname:video\nm=audio 9 UDP/TLS/RTP/SAVPF 111\na=ssrc:10 cname:audio\n", true, false)]
    [InlineData("m=video 9 UDP/TLS/RTP/SAVPF 102\na=ssrc:0 cname:video\nm=audio 9 UDP/TLS/RTP/SAVPF 111\na=ssrc:20 cname:audio\n", true, false)]
    [InlineData("m=video 9 UDP/TLS/RTP/SAVPF 102\na=ssrc:10 cname:video\n", false, true)]
    public void RequiresOneDistinctNonzeroSsrcPerBundledMediaSection(string sdp, bool useMicrophone, bool expected) =>
        Assert.Equal(expected, PhoneWebcamWebRtcPeer.HasDistinctMediaSsrcs(sdp, useMicrophone));

    private sealed class InstalledSetup : IPhoneWebcamSetup
    {
        private static readonly PhoneWebcamFeatureStatus Installed = new(PhoneWebcamFeatureState.Installed, "Installed.");
        public Task<PhoneWebcamFeatureStatus> GetStatusAsync(CancellationToken cancellationToken) => Task.FromResult(Installed);
        public Task<PhoneWebcamFeatureStatus> InstallAsync(CancellationToken cancellationToken) => Task.FromResult(Installed);
        public Task<PhoneWebcamFeatureStatus> RemoveAsync(CancellationToken cancellationToken) => Task.FromResult(Installed);
    }
}
