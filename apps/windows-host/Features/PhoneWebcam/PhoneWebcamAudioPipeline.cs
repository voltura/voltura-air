using System.Threading.Channels;
using Concentus;
using NAudio.CoreAudioApi;
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
    private readonly MMDevice _endpoint;
    private readonly WasapiOut _output;
    private readonly BufferedWaveProvider _buffer;
    private Task? _worker;
    private int _failed;
    private int _invalidPacketStreak;
    private int _disposed;

    internal PhoneWebcamAudioPipeline(IPhoneWebcamAudioTarget target)
    {
        _endpoint = target.OpenReadyEndpoint();
        _buffer = new BufferedWaveProvider(new WaveFormat(SampleRate, 16, Channels))
        {
            BufferDuration = MaximumBufferedAudio,
            DiscardOnBufferOverflow = true,
            ReadFully = true
        };
        try
        {
            _output = new WasapiOut(_endpoint, AudioClientShareMode.Shared, useEventSync: true, latency: 40);
            _output.PlaybackStopped += OnPlaybackStopped;
            _output.Init(_buffer);
        }
        catch
        {
            _output?.Dispose();
            _endpoint.Dispose();
            throw;
        }
    }

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

    private void OnPlaybackStopped(object? sender, StoppedEventArgs args)
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
        try
        {
            _output.PlaybackStopped -= OnPlaybackStopped;
            try
            {
                _output.Stop();
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
            }
            try
            {
                _output.Dispose();
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
            }
        }
        finally
        {
            _endpoint.Dispose();
            _cancellation.Dispose();
        }
    }
}
