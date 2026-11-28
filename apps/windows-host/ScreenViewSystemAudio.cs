using System.Threading.Channels;
using Concentus;
using Concentus.Enums;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace VolturaAir.Host;

internal readonly record struct ScreenViewEncodedAudioFrame(byte[] Bytes, uint RtpTimestamp);

internal readonly record struct ScreenViewAudioAvailability(bool Available, string Code, string Message);

internal enum ScreenViewAudioRunEnd
{
    CaptureStopped,
    DefaultDeviceChanged
}

internal interface IScreenViewSystemAudioCapture : IAsyncDisposable
{
    Task RunAsync(
        Func<ScreenViewEncodedAudioFrame, bool> send,
        Action<ScreenViewAudioAvailability> reportAvailability,
        Func<ScreenViewSoundQuality> getSoundQuality,
        CancellationToken cancellationToken);
}

internal interface IScreenViewSystemAudioCaptureFactory
{
    IScreenViewSystemAudioCapture Create();
}

internal sealed class ScreenViewSystemAudioCaptureFactory : IScreenViewSystemAudioCaptureFactory
{
    public IScreenViewSystemAudioCapture Create() => new ScreenViewSystemAudioCapture();
}

internal sealed class UnavailableScreenViewSystemAudioCaptureFactory : IScreenViewSystemAudioCaptureFactory
{
    public IScreenViewSystemAudioCapture Create() => new UnavailableScreenViewSystemAudioCapture();

    private sealed class UnavailableScreenViewSystemAudioCapture : IScreenViewSystemAudioCapture
    {
        public async Task RunAsync(
            Func<ScreenViewEncodedAudioFrame, bool> send,
            Action<ScreenViewAudioAvailability> reportAvailability,
            Func<ScreenViewSoundQuality> getSoundQuality,
            CancellationToken cancellationToken)
        {
            _ = send;
            _ = getSoundQuality;
            reportAvailability(new(false, "audio-unavailable", "PC sound is unavailable in isolated test mode."));
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

internal sealed class ScreenViewSystemAudioCapture : IScreenViewSystemAudioCapture
{
    internal const int SampleRate = 48_000;
    internal const int Channels = 2;
    internal const int FrameSamples = 960;
    internal const int TransportAllowance = 128_000;
    private const int SampleBytes = Channels * sizeof(short);
    private const int FrameBytes = FrameSamples * SampleBytes;
    private const int MaximumQueuedFrames = 5;
    private const int MaximumOpusPacketBytes = 1275;
    private const int CaptureGapTolerance = FrameSamples;
    private readonly Channel<bool> _defaultDeviceChanges = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    private readonly AudioFrameClock _frameClock = new();
    private int _disposed;

    internal static long TimestampFromPosition(long qpcPosition)
    {
        var (seconds, remainder) = Math.DivRem(qpcPosition, TimeSpan.TicksPerSecond);
        return seconds * SampleRate + remainder * SampleRate / TimeSpan.TicksPerSecond;
    }

    internal static AudioBlock? CopyAudioBlock(ReadOnlySpan<byte> data, AudioClientBufferFlags flags, long qpcPosition, long samplePosition)
    {
        int alignedLength = data.Length - data.Length % SampleBytes;
        int length = Math.Min(alignedLength, FrameBytes * MaximumQueuedFrames);
        if (length == 0) return null;

        int discarded = alignedLength - length;
        byte[] bytes = new byte[length];
        if ((flags & AudioClientBufferFlags.Silent) == 0)
            data.Slice(discarded, length).CopyTo(bytes);
        long? timestamp = qpcPosition >= 0 && (flags & AudioClientBufferFlags.TimestampError) == 0
            ? TimestampFromPosition(qpcPosition) + discarded / SampleBytes
            : null;
        bool discontinuity = discarded > 0 || (flags & AudioClientBufferFlags.DataDiscontinuity) != 0;
        return new(bytes, timestamp, samplePosition + discarded / SampleBytes, discontinuity);
    }

    internal sealed record AudioBlock(byte[] Bytes, long? Timestamp, long SamplePosition, bool Discontinuity);

    // The same RTP track survives output-device changes, so each new run keeps this floor.
    internal sealed class AudioFrameClock
    {
        private long _nextTimestamp;

        public void StartRun(long? captureTimestamp) => _nextTimestamp = Math.Max(_nextTimestamp,
            captureTimestamp ?? TimestampFromPosition(TimeProvider.System.GetElapsedTime(0).Ticks));

        public void Skip(long samples) => _nextTimestamp += samples;

        public uint TakeFrame()
        {
            uint timestamp = unchecked((uint)_nextTimestamp);
            _nextTimestamp += FrameSamples;
            return timestamp;
        }
    }

    internal static Channel<AudioBlock> CreatePendingAudioQueue() =>
        Channel.CreateBounded<AudioBlock>(new BoundedChannelOptions(MaximumQueuedFrames)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true
        });

    internal static IOpusEncoder CreateEncoder(ScreenViewSoundQuality soundQuality)
    {
        IOpusEncoder encoder = OpusCodecFactory.CreateEncoder(SampleRate, Channels, OpusApplication.OPUS_APPLICATION_AUDIO);
        encoder.UseVBR = true;
        encoder.UseConstrainedVBR = true;
        ApplyEncodingProfile(encoder, soundQuality);
        return encoder;
    }

    internal static void ApplyEncodingProfile(IOpusEncoder encoder, ScreenViewSoundQuality soundQuality)
    {
        ScreenViewSoundEncodingProfile profile = ScreenViewSoundQualityProfile.Encoding(soundQuality);
        encoder.Bitrate = profile.Bitrate;
        encoder.ForceChannels = profile.Channels;
    }

    internal static async Task EncodeAsync(
        ChannelReader<AudioBlock> blocks,
        AudioFrameClock frameClock,
        Func<ScreenViewEncodedAudioFrame, bool> send,
        Func<ScreenViewSoundQuality> getSoundQuality,
        CancellationToken cancellationToken)
    {
        ScreenViewSoundQuality appliedSoundQuality = getSoundQuality();
        using IOpusEncoder encoder = CreateEncoder(appliedSoundQuality);
        // A full capture block can follow up to one unfinished Opus frame.
        byte[] pending = new byte[FrameBytes * MaximumQueuedFrames + FrameBytes - SampleBytes];
        short[] pcm = new short[FrameSamples * Channels];
        byte[] encodedPacket = new byte[MaximumOpusPacketBytes];
        int pendingLength = 0;
        long? expectedCaptureTimestamp = null;
        long? expectedSamplePosition = null;
        await foreach (AudioBlock block in blocks.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            // Count dropped PCM from the capture sample positions. Use QPC only for gaps in
            // the capture itself, comparing adjacent blocks so ordinary clock drift stays local.
            long droppedSamples = (block.SamplePosition - expectedSamplePosition) ?? 0;
            long? predictedCaptureTimestamp = expectedCaptureTimestamp + droppedSamples;
            long captureGap = (block.Timestamp - predictedCaptureTimestamp) ?? 0;
            if (expectedSamplePosition is null)
            {
                frameClock.StartRun(block.Timestamp);
            }
            else if (block.Discontinuity || droppedSamples > 0 || captureGap >= CaptureGapTolerance)
            {
                frameClock.Skip(pendingLength / SampleBytes + droppedSamples + Math.Max(captureGap, 0));
                pendingLength = 0;
                encoder.ResetState();
            }

            // Keep predicting from the last usable observation when WASAPI reports a bad timestamp.
            int samples = block.Bytes.Length / SampleBytes;
            long? observedTimestamp = captureGap >= -CaptureGapTolerance ? block.Timestamp : null;
            expectedCaptureTimestamp = (observedTimestamp ?? predictedCaptureTimestamp) + samples;
            expectedSamplePosition = block.SamplePosition + samples;
            block.Bytes.CopyTo(pending.AsSpan(pendingLength));
            pendingLength += block.Bytes.Length;

            int consumed = 0;
            while (pendingLength - consumed >= FrameBytes)
            {
                ScreenViewSoundQuality soundQuality = getSoundQuality();
                if (soundQuality != appliedSoundQuality)
                {
                    ApplyEncodingProfile(encoder, soundQuality);
                    appliedSoundQuality = soundQuality;
                }
                Buffer.BlockCopy(pending, consumed, pcm, 0, FrameBytes);
                int encoded = encoder.Encode(pcm, FrameSamples, encodedPacket, MaximumOpusPacketBytes);
                uint timestamp = frameClock.TakeFrame();
                if (encoded > 0)
                {
                    byte[] packet = encodedPacket.AsSpan(0, encoded).ToArray();
                    _ = send(new(packet, timestamp));
                }
                consumed += FrameBytes;
            }
            pendingLength -= consumed;
            if (consumed > 0 && pendingLength > 0) Buffer.BlockCopy(pending, consumed, pending, 0, pendingLength);
        }
    }

    public async Task RunAsync(
        Func<ScreenViewEncodedAudioFrame, bool> send,
        Action<ScreenViewAudioAvailability> reportAvailability,
        Func<ScreenViewSoundQuality> getSoundQuality,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(send);
        ArgumentNullException.ThrowIfNull(reportAvailability);
        ArgumentNullException.ThrowIfNull(getSoundQuality);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        using var enumerator = new MMDeviceEnumerator();
        using MMDeviceNotificationClient notifications = enumerator.CreateNotificationClient(false);
        notifications.DefaultDeviceChanged += OnDefaultDeviceChanged;
        bool recovered = false;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                while (_defaultDeviceChanges.Reader.TryRead(out _)) { }
                try
                {
                    ScreenViewAudioRunEnd runEnd = await RunCurrentDeviceAsync(
                        enumerator,
                        send,
                        reportAvailability,
                        getSoundQuality,
                        recovered,
                        cancellationToken)
                        .ConfigureAwait(false);
                    recovered = true;
                    if (runEnd == ScreenViewAudioRunEnd.CaptureStopped)
                    {
                        reportAvailability(new(false, "audio-unavailable", "PC sound stopped. Video is still available."));
                        await _defaultDeviceChanges.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    reportAvailability(new(false, "audio-unavailable", "PC sound is unavailable. Video is still available."));
                    await _defaultDeviceChanges.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                    recovered = true;
                }
            }
        }
        finally
        {
            notifications.DefaultDeviceChanged -= OnDefaultDeviceChanged;
        }
    }

    private async Task<ScreenViewAudioRunEnd> RunCurrentDeviceAsync(
        MMDeviceEnumerator enumerator,
        Func<ScreenViewEncodedAudioFrame, bool> send,
        Action<ScreenViewAudioAvailability> reportAvailability,
        Func<ScreenViewSoundQuality> getSoundQuality,
        bool recovered,
        CancellationToken cancellationToken)
    {
        await using CaptureRun capture = await CaptureRun.CreateAsync(
            enumerator,
            _frameClock,
            send,
            getSoundQuality,
            cancellationToken).ConfigureAwait(false);
        reportAvailability(new(true, recovered ? "audio-recovered" : "audio-ready",
            recovered ? "PC sound is available again." : "PC sound is available."));

        return await WaitForCaptureOrDeviceChangeAsync(
            capture.Completion,
            _defaultDeviceChanges.Reader,
            cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<ScreenViewAudioRunEnd> WaitForCaptureOrDeviceChangeAsync(
        Task captureCompletion,
        ChannelReader<bool> defaultDeviceChanges,
        CancellationToken cancellationToken)
    {
        using var changeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task<bool> changed = defaultDeviceChanges.WaitToReadAsync(changeCancellation.Token).AsTask();
        try
        {
            Task completed = await Task.WhenAny(captureCompletion, changed).ConfigureAwait(false);
            if (ReferenceEquals(completed, changed))
            {
                if (!await changed.ConfigureAwait(false) || !defaultDeviceChanges.TryRead(out _))
                    throw new ChannelClosedException();
                return ScreenViewAudioRunEnd.DefaultDeviceChanged;
            }

            await captureCompletion.ConfigureAwait(false);
            return ScreenViewAudioRunEnd.CaptureStopped;
        }
        finally
        {
            await changeCancellation.CancelAsync().ConfigureAwait(false);
            try { await changed.ConfigureAwait(false); }
            catch (Exception exception) when (exception is OperationCanceledException or ChannelClosedException) { }
        }
    }

    private void OnDefaultDeviceChanged(object? sender, DefaultDeviceChangedEventArgs args)
    {
        if (args.Flow == DataFlow.Render && args.Role == Role.Multimedia)
            _defaultDeviceChanges.Writer.TryWrite(true);
    }

    public ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _disposed, 1);
        _defaultDeviceChanges.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }

    private sealed class CaptureRun : IAsyncDisposable
    {
        private readonly MMDevice _endpoint;
        private readonly WasapiRecorder _recorder;
        private readonly CancellationTokenSource _stop;
        private readonly Channel<AudioBlock> _blocks;
        private readonly TaskCompletionSource _recordingStopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private long _samplePosition;
        private int _disposed;

        private CaptureRun(
            MMDevice endpoint,
            WasapiRecorder recorder,
            AudioFrameClock frameClock,
            Func<ScreenViewEncodedAudioFrame, bool> send,
            Func<ScreenViewSoundQuality> getSoundQuality,
            CancellationToken cancellationToken)
        {
            _endpoint = endpoint;
            _recorder = recorder;
            _stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _blocks = CreatePendingAudioQueue();
            _recorder.DataAvailable += OnDataAvailable;
            _recorder.RecordingStopped += OnRecordingStopped;
            Completion = RunEncoderAsync(frameClock, send, getSoundQuality, _stop.Token);
        }

        public Task Completion { get; }

        public static async Task<CaptureRun> CreateAsync(
            MMDeviceEnumerator enumerator,
            AudioFrameClock frameClock,
            Func<ScreenViewEncodedAudioFrame, bool> send,
            Func<ScreenViewSoundQuality> getSoundQuality,
            CancellationToken cancellationToken)
        {
            MMDevice endpoint = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            WasapiRecorder? recorder = null;
            try
            {
                recorder = await new WasapiRecorderBuilder()
                    .WithDevice(endpoint)
                    .WithSharedMode()
                    .WithEventSync()
                    .WithLoopbackCapture()
                    .WithFormat(new WaveFormat(SampleRate, 16, Channels))
                    .WithBufferLength(20)
                    .WithMmcssThreadPriority("Audio")
                    .BuildAsync()
                    .ConfigureAwait(false);
                var run = new CaptureRun(endpoint, recorder, frameClock, send, getSoundQuality, cancellationToken);
                recorder.StartRecording();
                return run;
            }
            catch
            {
                if (recorder is not null) await recorder.DisposeAsync().ConfigureAwait(false);
                endpoint.Dispose();
                throw;
            }
        }

        private void OnDataAvailable(ReadOnlySpan<byte> data, AudioClientBufferFlags flags, long devicePosition, long qpcPosition)
        {
            _ = devicePosition;
            AudioBlock? block = CopyAudioBlock(data, flags, qpcPosition, _samplePosition);
            _samplePosition += data.Length / SampleBytes;
            if (block is not null)
                _blocks.Writer.TryWrite(block);
        }

        private void OnRecordingStopped(object? sender, StoppedEventArgs args)
        {
            if (args.Exception is null) _recordingStopped.TrySetResult();
            else _recordingStopped.TrySetException(args.Exception);
            _blocks.Writer.TryComplete(args.Exception);
        }

        private async Task RunEncoderAsync(
            AudioFrameClock frameClock,
            Func<ScreenViewEncodedAudioFrame, bool> send,
            Func<ScreenViewSoundQuality> getSoundQuality,
            CancellationToken cancellationToken)
        {
            await EncodeAsync(_blocks.Reader, frameClock, send, getSoundQuality, cancellationToken).ConfigureAwait(false);
            await _recordingStopped.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _recorder.DataAvailable -= OnDataAvailable;
            _recorder.RecordingStopped -= OnRecordingStopped;
            await _stop.CancelAsync().ConfigureAwait(false);
            _blocks.Writer.TryComplete();
            try { _recorder.StopRecording(); }
            catch (Exception exception) when (exception is not OutOfMemoryException) { }
            try { await Completion.ConfigureAwait(false); }
            catch (Exception exception) when (exception is not OutOfMemoryException) { }
            try { await _recorder.DisposeAsync().ConfigureAwait(false); }
            finally
            {
                _endpoint.Dispose();
                _stop.Dispose();
            }
        }

    }
}
