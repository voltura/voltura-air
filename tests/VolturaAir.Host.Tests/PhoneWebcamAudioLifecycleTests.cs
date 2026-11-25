using System.Buffers.Binary;
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PipelineReportsUnexpectedPlaybackStopAndIgnoresDisposal(bool hasError)
    {
        var player = new FakePlayer { RaiseStoppedWhenStopped = true };
        PhoneWebcamAudioPipeline pipeline = PhoneWebcamAudioPipeline.CreateForTest(player);
        int failures = 0;
        pipeline.Failed += (_, _) => failures++;
        pipeline.Start();
        pipeline.Start();

        player.RaiseStopped(hasError ? new InvalidOperationException("Injected playback failure.") : null);
        await pipeline.DisposeAsync();
        await pipeline.DisposeAsync();

        Assert.Equal(1, failures);
        Assert.Equal(1, player.PlayCalls);
        Assert.Equal(1, player.DisposeCalls);
    }

    [Fact]
    public async Task ConcurrentPipelineStopsWaitForTheSameOutputRetirement()
    {
        var releasePlayer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var player = new FakePlayer { DisposeGate = releasePlayer.Task };
        var factory = new FakeDeviceFactory(new FakeRecorder())
        {
            CablePlayer = Task.FromResult<IPhoneWebcamAudioPlayer>(player)
        };
        await using PhoneWebcamAudioPipeline pipeline = await PhoneWebcamAudioPipeline.CreateAsync(new FakeTarget(), factory);
        var buffer = Assert.IsType<BufferedWaveProvider>(factory.CableSource);
        buffer.AddSamples(new byte[buffer.WaveFormat.BlockAlign]);
        pipeline.Start();

        Task first = pipeline.DisposeAsync().AsTask();
        Task concurrent = pipeline.DisposeAsync().AsTask();
        try
        {
            await player.DisposeEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Same(first, concurrent);
            Assert.False(concurrent.IsCompleted);
            Assert.Equal(0, buffer.BufferedBytes);
        }
        finally
        {
            releasePlayer.TrySetResult();
        }
        await Task.WhenAll(first, concurrent);
        Assert.Equal(1, player.StopCalls);
        Assert.Equal(1, player.DisposeCalls);
    }

    [Fact]
    public async Task PlaybackFailureDoesNotStopTheDeviceInsideItsOwnCallback()
    {
        int callbackThread = Environment.CurrentManagedThreadId;
        int callbackReturned = 0;
        bool stoppedInline = false;
        var player = new FakePlayer
        {
            OnStop = () => stoppedInline =
                Environment.CurrentManagedThreadId == callbackThread && Volatile.Read(ref callbackReturned) == 0
        };
        await using PhoneWebcamAudioPipeline pipeline = PhoneWebcamAudioPipeline.CreateForTest(player);
        Task? retirement = null;
        pipeline.Failed += (_, _) => retirement = pipeline.DisposeAsync().AsTask();

        player.RaiseStopped();
        Volatile.Write(ref callbackReturned, 1);
        Assert.NotNull(retirement);
        await retirement.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(stoppedInline);
        Assert.Equal(1, player.StopCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PipelineCopiesOnlyDecodedSamplesBeforeReusingItsPcmBuffer(bool startNearCapacity)
    {
        var factory = new FakeDeviceFactory(new FakeRecorder());
        await using PhoneWebcamAudioPipeline pipeline = await PhoneWebcamAudioPipeline.CreateAsync(new FakeTarget(), factory);
        var buffer = Assert.IsType<BufferedWaveProvider>(factory.CableSource);
        using IOpusEncoder encoder = OpusCodecFactory.CreateEncoder(48000, 2, OpusApplication.OPUS_APPLICATION_AUDIO);
        using IOpusDecoder decoder = OpusCodecFactory.CreateDecoder(48000, 2);
        if (startNearCapacity)
        {
            buffer.AddSamples(new byte[buffer.BufferLength - buffer.WaveFormat.BlockAlign]);
        }
        pipeline.Start();
        var expected = new List<byte>();
        ushort sequence = 0;
        uint timestamp = 0;
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
            pipeline.Submit(CreateRtpPacket(sequence++, timestamp, encoded.AsSpan(0, length)));
            timestamp += (uint)samples;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (buffer.BufferedBytes != expected.Count)
                await Task.Delay(10, timeout.Token);
        }
        byte[] actual = new byte[expected.Count];
        Assert.Equal(actual.Length, buffer.Read(actual));
        Assert.Equal(expected.ToArray(), actual);
        Assert.Equal(0, buffer.BufferedBytes);
    }

    [Fact]
    public async Task PaddingProbesDoNotCountAsInvalidAudioPackets()
    {
        var player = new FakePlayer();
        await using PhoneWebcamAudioPipeline pipeline = PhoneWebcamAudioPipeline.CreateForTest(player);
        int failures = 0;
        pipeline.Failed += (_, _) => failures++;
        byte[] padding = [0xa0, 111, 0, 1, 0, 0, 0, 0, 0, 0, 0, 1, 0, 2];
        for (int index = 0; index < 60; index++) pipeline.Submit(padding);
        Assert.Equal(0, failures);
    }

    [Theory]
    [InlineData(false, false, 3)]
    [InlineData(true, false, 2)]
    [InlineData(false, true, 1)]
    public async Task PipelineConcealsOnlyUnplayedShortLossAcrossRtpRollover(
        bool paddingInsteadOfLoss, bool playbackAlreadyDrained, int expectedFrames)
    {
        const int samples = 960;
        const int frameBytes = samples * 2 * sizeof(short);
        const uint initialTimestamp = uint.MaxValue - samples + 1;
        var factory = new FakeDeviceFactory(new FakeRecorder());
        await using PhoneWebcamAudioPipeline pipeline = await PhoneWebcamAudioPipeline.CreateAsync(new FakeTarget(), factory);
        var buffer = Assert.IsType<BufferedWaveProvider>(factory.CableSource);
        using IOpusEncoder encoder = OpusCodecFactory.CreateEncoder(48000, 2, OpusApplication.OPUS_APPLICATION_AUDIO);
        byte[] firstFrame = Encode(encoder, samples);
        byte[] nextFrame = Encode(encoder, samples);
        pipeline.Start();
        pipeline.Submit(CreateRtpPacket(ushort.MaxValue, initialTimestamp, firstFrame));
        await AssertBufferedAudioAsync(buffer, frameBytes);
        if (playbackAlreadyDrained) Assert.Equal(frameBytes, buffer.Read(new byte[frameBytes]));

        if (paddingInsteadOfLoss)
            pipeline.Submit(CreateRtpPacket(0, 0, [0, 2], padding: true));
        pipeline.Submit(CreateRtpPacket(1, samples, nextFrame));

        await AssertBufferedAudioAsync(buffer, frameBytes * expectedFrames);
    }

    [Fact]
    public async Task RecentDuplicateOrLatePacketsDoNotReplaySpeech()
    {
        const int samples = 960;
        const int frameBytes = samples * 2 * sizeof(short);
        var factory = new FakeDeviceFactory(new FakeRecorder());
        await using PhoneWebcamAudioPipeline pipeline = await PhoneWebcamAudioPipeline.CreateAsync(new FakeTarget(), factory);
        var buffer = Assert.IsType<BufferedWaveProvider>(factory.CableSource);
        using IOpusEncoder encoder = OpusCodecFactory.CreateEncoder(48000, 2, OpusApplication.OPUS_APPLICATION_AUDIO);
        byte[] first = CreateRtpPacket(ushort.MaxValue, 0, Encode(encoder, samples));
        byte[] next = CreateRtpPacket(0, samples, Encode(encoder, samples * 2));
        pipeline.Submit(first);
        pipeline.Submit(next);
        pipeline.Submit(next);
        pipeline.Submit(first);
        pipeline.Start();
        await AssertBufferedAudioAsync(buffer, frameBytes * 2);
    }

    [Fact]
    public async Task ALongDiscontinuityDiscardsOldAudioAndRestartsFromTheNewestFrame()
    {
        const int samples = 960;
        const int frameBytes = samples * 2 * sizeof(short);
        var factory = new FakeDeviceFactory(new FakeRecorder());
        await using PhoneWebcamAudioPipeline pipeline = await PhoneWebcamAudioPipeline.CreateAsync(new FakeTarget(), factory);
        var buffer = Assert.IsType<BufferedWaveProvider>(factory.CableSource);
        using IOpusEncoder encoder = OpusCodecFactory.CreateEncoder(48000, 2, OpusApplication.OPUS_APPLICATION_AUDIO);
        pipeline.Start();
        pipeline.Submit(CreateRtpPacket(1, 20_000, Encode(encoder, samples)));
        await AssertBufferedAudioAsync(buffer, frameBytes);

        byte[] newest = Encode(encoder, samples * 2);
        pipeline.Submit(CreateRtpPacket(6, 29_600, newest));
        await AssertBufferedAudioAsync(buffer, frameBytes * 2);

        using IOpusDecoder fresh = OpusCodecFactory.CreateDecoder(48000, 2);
        var expected = new short[5760 * 2];
        int decodedSamples = fresh.Decode(newest, expected, 5760, false);
        Assert.Equal(samples * 2, decodedSamples);
        var expectedBytes = new byte[frameBytes * 2];
        Buffer.BlockCopy(expected, 0, expectedBytes, 0, expectedBytes.Length);
        var actual = new byte[expectedBytes.Length];
        Assert.Equal(actual.Length, buffer.Read(actual));
        Assert.Equal(expectedBytes, actual);
    }

    private static async Task AssertBufferedAudioAsync(BufferedWaveProvider buffer, int length)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (buffer.BufferedBytes < length) await Task.Delay(10, timeout.Token);
        Assert.Equal(length, buffer.BufferedBytes);
    }

    private static byte[] Encode(IOpusEncoder encoder, int samples)
    {
        short[] tone = Enumerable.Range(0, samples * 2).Select(index => (short)(Math.Sin(index * 0.1) * 12000)).ToArray();
        byte[] encoded = new byte[1275];
        int length = encoder.Encode(tone, samples, encoded, encoded.Length);
        return encoded[..length];
    }

    private static byte[] CreateRtpPacket(ushort sequence, uint timestamp, ReadOnlySpan<byte> payload, bool padding = false)
    {
        byte[] rtp = new byte[12 + payload.Length];
        rtp[0] = padding ? (byte)0xa0 : (byte)0x80;
        rtp[1] = 111;
        BinaryPrimitives.WriteUInt16BigEndian(rtp.AsSpan(2), sequence);
        BinaryPrimitives.WriteUInt32BigEndian(rtp.AsSpan(4), timestamp);
        payload.CopyTo(rtp.AsSpan(12));
        return rtp;
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
        internal Task? DisposeGate { get; init; }
        internal Action? OnStop { get; init; }
        internal TaskCompletionSource DisposeEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool RaiseStoppedWhenStopped { get; init; }
        internal int PlayCalls { get; private set; }
        internal int StopCalls { get; private set; }
        internal int DisposeCalls { get; private set; }
        public event EventHandler<PhoneWebcamAudioStoppedEventArgs>? Stopped;
        public void Play() => PlayCalls++;
        public void Stop()
        {
            StopCalls++;
            OnStop?.Invoke();
            if (RaiseStoppedWhenStopped) RaiseStopped();
        }
        internal void RaiseStopped(Exception? exception = null) =>
            Stopped?.Invoke(this, new PhoneWebcamAudioStoppedEventArgs(exception));
        public ValueTask DisposeAsync()
        {
            DisposeCalls++;
            DisposeEntered.TrySetResult();
            if (DisposeFailure is not null) return ValueTask.FromException(DisposeFailure);
            return DisposeGate is null ? ValueTask.CompletedTask : new ValueTask(DisposeGate);
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
