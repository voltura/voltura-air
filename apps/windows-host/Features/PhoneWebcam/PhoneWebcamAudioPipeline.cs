using System.Threading.Channels;
using Concentus;
using NAudio.Wave;

namespace VolturaAir.Host.Features.PhoneWebcam;

internal sealed class PhoneWebcamAudioPipeline : IAsyncDisposable
{
    internal const int MaximumOpusPacketBytes = 1275;
    private const int SampleRate = 48000;
    private const int Channels = 2;
    private const int MaximumFrameSamplesPerChannel = 5760;
    private static readonly TimeSpan MaximumBufferedAudio = TimeSpan.FromMilliseconds(200);
    private readonly Channel<byte[]> _packets = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        SingleWriter = false
    });
    private readonly CancellationTokenSource _cancellation = new();
    private readonly IPhoneWebcamAudioPlayer _output;
    private readonly BufferedWaveProvider _buffer;
    private Task? _worker;
    private int _failed;
    private int _invalidPacketStreak;
    private int _disposed;

    private PhoneWebcamAudioPipeline(
        IPhoneWebcamAudioPlayer output,
        BufferedWaveProvider buffer)
    {
        _output = output;
        _buffer = buffer;
        _output.Stopped += OnPlaybackStopped;
    }

    internal static async Task<PhoneWebcamAudioPipeline> CreateAsync(
        IPhoneWebcamAudioTarget target,
        IPhoneWebcamAudioDeviceFactory? deviceFactory = null)
    {
        var buffer = new BufferedWaveProvider(
            new WaveFormat(SampleRate, 16, Channels),
            MaximumBufferedAudio)
        {
            DiscardOnBufferOverflow = true,
            ReadFully = true
        };
        IPhoneWebcamAudioPlayer output = await (deviceFactory ?? PhoneWebcamAudioDeviceFactory.Instance)
            .CreateCablePlayerAsync(target, buffer)
            .ConfigureAwait(false);
        return new PhoneWebcamAudioPipeline(output, buffer);
    }

    internal static PhoneWebcamAudioPipeline CreateForTest(IPhoneWebcamAudioPlayer output) =>
        new(output, new BufferedWaveProvider(
            new WaveFormat(SampleRate, 16, Channels),
            MaximumBufferedAudio)
        {
            DiscardOnBufferOverflow = true,
            ReadFully = true
        });

    internal event EventHandler? Failed;

    internal void Start()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (_worker is not null) return;
        _output.Play();
        _worker = Task.Run(ProcessAsync);
    }

    internal void Submit(byte[] packet)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        if (!RtpPacket.TryRead(packet, out _, out _, out _, out ReadOnlySpan<byte> payload) ||
            payload.IsEmpty || payload.Length > MaximumOpusPacketBytes)
        {
            if (Interlocked.Increment(ref _invalidPacketStreak) >= 50) ReportFailure();
            return;
        }

        Interlocked.Exchange(ref _invalidPacketStreak, 0);
        _packets.Writer.TryWrite(payload.ToArray());
    }

    private async Task ProcessAsync()
    {
        try
        {
            using IOpusDecoder decoder = OpusCodecFactory.CreateDecoder(SampleRate, Channels);
            var pcm = new short[MaximumFrameSamplesPerChannel * Channels];
            await foreach (byte[] packet in _packets.Reader.ReadAllAsync(_cancellation.Token).ConfigureAwait(false))
            {
                int samplesPerChannel = decoder.Decode(packet, pcm, MaximumFrameSamplesPerChannel, false);
                if (samplesPerChannel <= 0) continue;
                int byteCount = checked(samplesPerChannel * Channels * sizeof(short));
                var bytes = new byte[byteCount];
                Buffer.BlockCopy(pcm, 0, bytes, 0, byteCount);
                if (_buffer.BufferedDuration >= MaximumBufferedAudio)
                {
                    _buffer.ClearBuffer();
                }
                _buffer.AddSamples(bytes, 0, bytes.Length);
            }
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ReportFailure();
        }
    }

    private void OnPlaybackStopped(object? sender, PhoneWebcamAudioStoppedEventArgs args)
    {
        if (Volatile.Read(ref _disposed) == 0 && args.Exception is not null)
        {
            ReportFailure();
        }
    }

    private void ReportFailure()
    {
        if (Interlocked.Exchange(ref _failed, 1) == 0)
        {
            Failed?.Invoke(this, EventArgs.Empty);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _packets.Writer.TryComplete();
        await _cancellation.CancelAsync().ConfigureAwait(false);
        if (_worker is not null)
        {
            try { await _worker.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        _output.Stopped -= OnPlaybackStopped;
        try
        {
            _output.Stop();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
        }
        try
        {
            await _output.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
        }
        _cancellation.Dispose();
    }
}
