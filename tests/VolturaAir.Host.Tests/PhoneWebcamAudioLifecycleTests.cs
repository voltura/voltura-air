using NAudio.Wave;
using Concentus;
using Concentus.Enums;
using VolturaAir.Host.Features.PhoneWebcam;

namespace VolturaAir.Host.Tests;

public sealed class PhoneWebcamAudioLifecycleTests
{
    [Fact]
    public async Task MonitorPartialConstructionReleasesTheRecorder()
    {
        var recorder = new FakeRecorder();
        var factory = new FakeDeviceFactory(recorder)
        {
            PlayerFailure = new InvalidOperationException("Injected player construction failure.")
        };

        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => PhoneWebcamAudioMonitor.CreateAsync(static _ => { }, factory));

        Assert.Contains("Injected player construction failure", failure.Message, StringComparison.Ordinal);
        Assert.Equal(1, recorder.DisposeCalls);
    }

    [Fact]
    public async Task MonitorStartFailureStopsAndReleasesEveryCreatedEndpoint()
    {
        var recorder = new FakeRecorder { StartFailure = new InvalidOperationException("Injected start failure.") };
        var player = new FakePlayer();
        await using IPhoneWebcamAudioMonitor monitor = PhoneWebcamAudioMonitor.CreateForTest(static _ => { }, recorder, player);

        Assert.Throws<InvalidOperationException>(monitor.Start);

        Assert.Equal(1, player.PlayCalls);
        Assert.Equal(1, recorder.StartCalls);
        Assert.Equal(1, recorder.StopCalls);
        Assert.Equal(1, player.StopCalls);
        await monitor.DisposeAsync();
        Assert.Equal(1, recorder.DisposeCalls);
        Assert.Equal(1, player.DisposeCalls);
    }

    [Fact]
    public async Task MonitorStartsOnlyOnceAndReportsUnexpectedStopOnlyOnce()
    {
        var recorder = new FakeRecorder();
        var player = new FakePlayer();
        var failures = new List<string>();
        await using IPhoneWebcamAudioMonitor monitor = PhoneWebcamAudioMonitor.CreateForTest(failures.Add, recorder, player);

        monitor.Start();
        monitor.Start();
        recorder.RaiseStopped();
        player.RaiseStopped(new InvalidOperationException("Injected playback failure."));

        Assert.Equal(1, player.PlayCalls);
        Assert.Equal(1, recorder.StartCalls);
        Assert.Single(failures);
        Assert.Equal("Audio test stopped because Windows could not continue monitoring CABLE Output.", failures[0]);
    }

    [Fact]
    public async Task MonitorNormalStopDoesNotReportFailure()
    {
        var recorder = new FakeRecorder { RaiseStoppedWhenStopped = true };
        var player = new FakePlayer { RaiseStoppedWhenStopped = true };
        var failures = new List<string>();
        IPhoneWebcamAudioMonitor monitor = PhoneWebcamAudioMonitor.CreateForTest(failures.Add, recorder, player);
        monitor.Start();

        await monitor.DisposeAsync();

        Assert.Empty(failures);
        Assert.Equal(1, recorder.DisposeCalls);
        Assert.Equal(1, player.DisposeCalls);
    }

    [Fact]
    public async Task MonitorDisposalExceptionsDoNotPreventOtherEndpointRelease()
    {
        var recorder = new FakeRecorder { DisposeFailure = new InvalidOperationException("Recorder dispose failure.") };
        var player = new FakePlayer { DisposeFailure = new InvalidOperationException("Player dispose failure.") };
        IPhoneWebcamAudioMonitor monitor = PhoneWebcamAudioMonitor.CreateForTest(static _ => { }, recorder, player);

        await monitor.DisposeAsync();
        await monitor.DisposeAsync();

        Assert.Equal(1, recorder.DisposeCalls);
        Assert.Equal(1, player.DisposeCalls);
    }

    [Fact]
    public async Task PipelineReportsPlaybackFailureAndIgnoresNormalDisposalStop()
    {
        var player = new FakePlayer { RaiseStoppedWhenStopped = true };
        PhoneWebcamAudioPipeline pipeline = PhoneWebcamAudioPipeline.CreateForTest(player);
        int failures = 0;
        pipeline.Failed += (_, _) => failures++;
        pipeline.Start();
        pipeline.Start();

        player.RaiseStopped(new InvalidOperationException("Injected playback failure."));
        await pipeline.DisposeAsync();
        await pipeline.DisposeAsync();

        Assert.Equal(1, failures);
        Assert.Equal(1, player.PlayCalls);
        Assert.Equal(1, player.DisposeCalls);
    }

    [Fact]
    public async Task PipelineCopiesOnlyDecodedSamplesBeforeReusingItsPcmBuffer()
    {
        var factory = new FakeDeviceFactory(new FakeRecorder());
        await using PhoneWebcamAudioPipeline pipeline = await PhoneWebcamAudioPipeline.CreateAsync(new FakeTarget(), factory);
        var buffer = Assert.IsType<BufferedWaveProvider>(factory.CableSource);
        using IOpusEncoder encoder = OpusCodecFactory.CreateEncoder(48000, 2, OpusApplication.OPUS_APPLICATION_AUDIO);
        using IOpusDecoder decoder = OpusCodecFactory.CreateDecoder(48000, 2);
        pipeline.Start();
        var expected = new List<byte>();
        foreach (int samples in new[] { 960, 480, 2880 })
        {
            short[] pcm = Enumerable.Range(0, samples * 2).Select(index => (short)(Math.Sin(index * 0.1) * 12000)).ToArray();
            byte[] encoded = new byte[1275];
            int length = encoder.Encode(pcm, samples, encoded, encoded.Length);
            short[] decoded = new short[5760 * 2];
            int decodedSamples = decoder.Decode(encoded.AsSpan(0, length), decoded, 5760, false);
            byte[] expectedFrame = new byte[decodedSamples * 2 * sizeof(short)];
            Buffer.BlockCopy(decoded, 0, expectedFrame, 0, expectedFrame.Length);
            expected.AddRange(expectedFrame);
            byte[] rtp = new byte[12 + length];
            rtp[0] = 0x80;
            rtp[1] = 111;
            encoded.AsSpan(0, length).CopyTo(rtp.AsSpan(12));
            pipeline.Submit(rtp);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (buffer.BufferedBytes < expected.Count)
                await Task.Delay(10, timeout.Token);
        }
        byte[] actual = new byte[expected.Count];
        Assert.Equal(actual.Length, buffer.Read(actual));
        Assert.Equal(expected.ToArray(), actual);
        Assert.Equal(0, buffer.BufferedBytes);
    }

    [Fact]
    public async Task PipelineConstructionAwaitsTheInjectedPlayerBoundary()
    {
        var playerReady = new TaskCompletionSource<IPhoneWebcamAudioPlayer>(TaskCreationOptions.RunContinuationsAsynchronously);
        var factory = new FakeDeviceFactory(new FakeRecorder()) { CablePlayer = playerReady.Task };
        Task<PhoneWebcamAudioPipeline> construction = PhoneWebcamAudioPipeline.CreateAsync(new FakeTarget(), factory);
        Assert.False(construction.IsCompleted);

        var player = new FakePlayer();
        playerReady.SetResult(player);
        await using PhoneWebcamAudioPipeline pipeline = await construction;

        Assert.Equal(1, factory.CablePlayerCalls);
    }

    private sealed class FakeDeviceFactory(FakeRecorder recorder) : IPhoneWebcamAudioDeviceFactory
    {
        internal Exception? PlayerFailure { get; init; }
        internal Task<IPhoneWebcamAudioPlayer>? CablePlayer { get; init; }
        internal int CablePlayerCalls { get; private set; }
        internal IWaveProvider? CableSource { get; private set; }

        public Task<IPhoneWebcamAudioPlayer> CreateCablePlayerAsync(
            IPhoneWebcamAudioTarget target,
            IWaveProvider source)
        {
            CablePlayerCalls++;
            CableSource = source;
            return CablePlayer ?? Task.FromResult<IPhoneWebcamAudioPlayer>(new FakePlayer());
        }

        public Task<IPhoneWebcamAudioRecorder> CreateCableRecorderAsync() =>
            Task.FromResult<IPhoneWebcamAudioRecorder>(recorder);

        public Task<IPhoneWebcamAudioPlayer> CreateDefaultPlayerAsync(IWaveProvider source) =>
            PlayerFailure is null
                ? Task.FromResult<IPhoneWebcamAudioPlayer>(new FakePlayer())
                : Task.FromException<IPhoneWebcamAudioPlayer>(PlayerFailure);
    }

    private sealed class FakePlayer : IPhoneWebcamAudioPlayer
    {
        internal Exception? DisposeFailure { get; init; }
        internal bool RaiseStoppedWhenStopped { get; init; }
        internal int PlayCalls { get; private set; }
        internal int StopCalls { get; private set; }
        internal int DisposeCalls { get; private set; }
        public event EventHandler<PhoneWebcamAudioStoppedEventArgs>? Stopped;
        public void Play() => PlayCalls++;
        public void Stop()
        {
            StopCalls++;
            if (RaiseStoppedWhenStopped) RaiseStopped();
        }
        internal void RaiseStopped(Exception? exception = null) =>
            Stopped?.Invoke(this, new PhoneWebcamAudioStoppedEventArgs(exception));
        public ValueTask DisposeAsync()
        {
            DisposeCalls++;
            return DisposeFailure is null ? ValueTask.CompletedTask : ValueTask.FromException(DisposeFailure);
        }
    }

    private sealed class FakeRecorder : IPhoneWebcamAudioRecorder
    {
        private PhoneWebcamAudioDataAvailableHandler? _dataAvailable;
        internal Exception? StartFailure { get; init; }
        internal Exception? DisposeFailure { get; init; }
        internal bool RaiseStoppedWhenStopped { get; init; }
        internal int StartCalls { get; private set; }
        internal int StopCalls { get; private set; }
        internal int DisposeCalls { get; private set; }
        public WaveFormat WaveFormat { get; } = new(48000, 16, 2);
        public event PhoneWebcamAudioDataAvailableHandler? DataAvailable
        {
            add => _dataAvailable += value;
            remove => _dataAvailable -= value;
        }
        public event EventHandler<PhoneWebcamAudioStoppedEventArgs>? Stopped;
        public void Start()
        {
            StartCalls++;
            if (StartFailure is not null) throw StartFailure;
        }
        public void Stop()
        {
            StopCalls++;
            if (RaiseStoppedWhenStopped) RaiseStopped();
        }
        internal void RaiseStopped(Exception? exception = null) =>
            Stopped?.Invoke(this, new PhoneWebcamAudioStoppedEventArgs(exception));
        public ValueTask DisposeAsync()
        {
            DisposeCalls++;
            return DisposeFailure is null ? ValueTask.CompletedTask : ValueTask.FromException(DisposeFailure);
        }
    }

    private sealed class FakeTarget : IPhoneWebcamAudioTarget
    {
        public PhoneWebcamAudioTargetStatus Status => new(PhoneWebcamAudioTargetState.Ready, "Ready.", "fake");
        public event EventHandler? StatusChanged { add { } remove { } }
        public PhoneWebcamAudioTargetStatus Refresh() => Status;
        public PhoneWebcamAudioTargetStatus ReportDetectionFailure() => Status;
        public void InvalidateRefresh() { }
        public NAudio.CoreAudioApi.MMDevice OpenReadyEndpoint() => throw new NotSupportedException();
    }
}
