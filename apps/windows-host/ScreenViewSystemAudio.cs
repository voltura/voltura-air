using System.Security.Cryptography;
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
    private const int FrameBytes = FrameSamples * Channels * sizeof(short);
    private const int MaximumQueuedFrames = 5;
    private const int MaximumOpusPacketBytes = 1275;
    private readonly Channel<bool> _defaultDeviceChanges = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    private int _disposed;

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
        private int _dropped;
        private int _disposed;

        private CaptureRun(
            MMDevice endpoint,
            WasapiRecorder recorder,
            Func<ScreenViewEncodedAudioFrame, bool> send,
            Func<ScreenViewSoundQuality> getSoundQuality,
            CancellationToken cancellationToken)
        {
            _endpoint = endpoint;
            _recorder = recorder;
            _stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _blocks = Channel.CreateBounded<AudioBlock>(new BoundedChannelOptions(MaximumQueuedFrames)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = false,
                SingleWriter = true
            });
            _recorder.DataAvailable += OnDataAvailable;
            _recorder.RecordingStopped += OnRecordingStopped;
            Completion = EncodeAsync(send, getSoundQuality, _stop.Token);
        }

        public Task Completion { get; }

        public static async Task<CaptureRun> CreateAsync(
            MMDeviceEnumerator enumerator,
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
                    .BuildAsync()
                    .ConfigureAwait(false);
                var run = new CaptureRun(endpoint, recorder, send, getSoundQuality, cancellationToken);
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
            if (data.IsEmpty) return;
            int length = Math.Min(data.Length, FrameBytes * MaximumQueuedFrames);
            byte[] copy = data[^length..].ToArray();
            if ((flags & AudioClientBufferFlags.Silent) != 0) Array.Clear(copy);
            var block = new AudioBlock(copy, qpcPosition);
            while (!_blocks.Writer.TryWrite(block))
            {
                if (!_blocks.Reader.TryRead(out _)) break;
                Interlocked.Exchange(ref _dropped, 1);
            }
        }

        private void OnRecordingStopped(object? sender, StoppedEventArgs args)
        {
            if (args.Exception is null) _recordingStopped.TrySetResult();
            else _recordingStopped.TrySetException(args.Exception);
            _blocks.Writer.TryComplete(args.Exception);
        }

        private async Task EncodeAsync(
            Func<ScreenViewEncodedAudioFrame, bool> send,
            Func<ScreenViewSoundQuality> getSoundQuality,
            CancellationToken cancellationToken)
        {
            ScreenViewSoundQuality appliedSoundQuality = getSoundQuality();
            using IOpusEncoder encoder = CreateEncoder(appliedSoundQuality);
            byte[] pending = new byte[FrameBytes * MaximumQueuedFrames];
            int pendingLength = 0;
            uint timestamp = NonZeroRandomTimestamp();
            await foreach (AudioBlock block in _blocks.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                if (Interlocked.Exchange(ref _dropped, 0) != 0)
                {
                    pendingLength = 0;
                    timestamp = TimestampFromPosition(block.QpcPosition);
                    encoder.ResetState();
                }
                if (pendingLength == 0) timestamp = TimestampFromPosition(block.QpcPosition);
                int available = pending.Length - pendingLength;
                if (block.Bytes.Length > available)
                {
                    pendingLength = 0;
                    timestamp = TimestampFromPosition(block.QpcPosition);
                    encoder.ResetState();
                }
                Buffer.BlockCopy(block.Bytes, Math.Max(0, block.Bytes.Length - (pending.Length - pendingLength)), pending,
                    pendingLength, Math.Min(block.Bytes.Length, pending.Length - pendingLength));
                pendingLength += Math.Min(block.Bytes.Length, pending.Length - pendingLength);

                while (pendingLength >= FrameBytes)
                {
                    ScreenViewSoundQuality soundQuality = getSoundQuality();
                    if (soundQuality != appliedSoundQuality)
                    {
                        ApplyEncodingProfile(encoder, soundQuality);
                        appliedSoundQuality = soundQuality;
                    }
                    short[] pcm = new short[FrameSamples * Channels];
                    Buffer.BlockCopy(pending, 0, pcm, 0, FrameBytes);
                    byte[] packet = new byte[MaximumOpusPacketBytes];
                    int encoded = encoder.Encode(pcm, FrameSamples, packet, MaximumOpusPacketBytes);
                    if (encoded > 0)
                    {
                        Array.Resize(ref packet, encoded);
                        _ = send(new(packet, timestamp));
                    }
                    timestamp = unchecked(timestamp + FrameSamples);
                    pendingLength -= FrameBytes;
                    if (pendingLength > 0) Buffer.BlockCopy(pending, FrameBytes, pending, 0, pendingLength);
                }
            }
            await _recordingStopped.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        private static uint TimestampFromPosition(long qpcPosition)
        {
            uint timestamp = unchecked((uint)(qpcPosition * SampleRate / 10_000_000L));
            return timestamp == 0 ? 1u : timestamp;
        }

        private static uint NonZeroRandomTimestamp()
        {
            uint value;
            do { value = unchecked((uint)RandomNumberGenerator.GetInt32(int.MinValue, int.MaxValue)); } while (value == 0);
            return value;
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

        private sealed record AudioBlock(byte[] Bytes, long QpcPosition);
    }
}
