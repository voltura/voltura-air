using System.Threading.Channels;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Concentus;
using NAudio.Wave;

namespace VolturaAir.Host.Features.PhoneWebcam;

[SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "The shared retirement task disposes the cancellation source after the decoder and device stop.")]
internal sealed class PhoneWebcamAudioPipeline : IAsyncDisposable
{
    internal const int MaximumOpusPacketBytes = 1275;
    private const int SampleRate = 48000;
    private const int Channels = 2;
    private const int MaximumFrameSamplesPerChannel = 5760;
    private const int OpusFrameStep = SampleRate / 400; // 2.5 milliseconds
    private const int MaximumConcealmentSamples = SampleRate * 60 / 1000;
    private static readonly TimeSpan MaximumBufferedAudio = TimeSpan.FromMilliseconds(200);
    private readonly Channel<EncodedAudioPacket> _packets = Channel.CreateBounded<EncodedAudioPacket>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropOldest,
        SingleReader = true,
        SingleWriter = false
    });
    private readonly Lock _packetGate = new();
    private readonly CancellationTokenSource _cancellation = new();
    private readonly IPhoneWebcamAudioPlayer _output;
    private readonly BufferedWaveProvider _buffer;
    private Task? _worker;
    private Task? _disposal;
    private int _failed;
    private int _invalidPacketStreak;
    private int _disposed;
    private ushort? _expectedSequence;
    private long _receivedAudioPackets;
    private bool _transportGap;

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
        if (!RtpPacket.TryRead(packet, out RtpPacketHeader header) || header.PayloadLength > MaximumOpusPacketBytes)
        {
            if (Interlocked.Increment(ref _invalidPacketStreak) >= 50) ReportFailure();
            return;
        }
        // The peer supplies an owned copy of the native RTP datagram.
        ReadOnlyMemory<byte> payload = packet.AsMemory(header.PayloadOffset, header.PayloadLength);
        lock (_packetGate)
        {
            if (_disposed != 0) return;
            int offset = RtpPacket.SequenceOffset(header.Sequence, _expectedSequence);
            if (RtpPacket.IsLate(offset)) return;
            _expectedSequence = unchecked((ushort)(header.Sequence + 1));
            _transportGap |= offset != 0;
            if (payload.IsEmpty) return;

            Interlocked.Exchange(ref _invalidPacketStreak, 0);
            _packets.Writer.TryWrite(new EncodedAudioPacket(payload, header.Timestamp, ++_receivedAudioPackets, _transportGap));
            _transportGap = false;
        }
    }

    private async Task ProcessAsync()
    {
        try
        {
            using IOpusDecoder decoder = OpusCodecFactory.CreateDecoder(SampleRate, Channels);
            var pcm = new short[MaximumFrameSamplesPerChannel * Channels];
            long previousPacket = 0;
            uint expectedTimestamp = 0;
            await foreach (EncodedAudioPacket packet in _packets.Reader.ReadAllAsync(_cancellation.Token).ConfigureAwait(false))
            {
                if (Volatile.Read(ref _disposed) != 0) break;
                if (previousPacket != 0)
                {
                    int gap = unchecked((int)(packet.Timestamp - expectedTimestamp));
                    bool skippedLocally = packet.Number != previousPacket + 1;
                    if (skippedLocally || gap < 0 || gap > MaximumConcealmentSamples || gap % OpusFrameStep != 0)
                    {
                        decoder.ResetState();
                        _buffer.ClearBuffer();
                    }
                    else if (gap > 0 && packet.TransportGap)
                    {
                        int concealedSamples = decoder.Decode([], pcm, gap, false);
                        // An empty device buffer has already begun supplying silence for the gap.
                        if (_buffer.BufferedBytes > 0) WritePcm(pcm, concealedSamples);
                    }
                }

                int samplesPerChannel = decoder.Decode(packet.Payload.Span, pcm, MaximumFrameSamplesPerChannel, false);
                if (samplesPerChannel <= 0) continue;
                WritePcm(pcm, samplesPerChannel);
                previousPacket = packet.Number;
                expectedTimestamp = unchecked(packet.Timestamp + (uint)samplesPerChannel);
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

    private void WritePcm(ReadOnlySpan<short> pcm, int samplesPerChannel)
    {
        if (samplesPerChannel <= 0 || Volatile.Read(ref _disposed) != 0) return;
        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(pcm[..(samplesPerChannel * Channels)]);
        if (bytes.Length > _buffer.BufferLength - _buffer.BufferedBytes) _buffer.ClearBuffer();
        _buffer.AddSamples(bytes);
    }

    private readonly record struct EncodedAudioPacket(
        ReadOnlyMemory<byte> Payload, uint Timestamp, long Number, bool TransportGap);

    private void OnPlaybackStopped(object? sender, PhoneWebcamAudioStoppedEventArgs args)
    {
        if (Volatile.Read(ref _disposed) == 0)
        {
            ReportFailure();
        }
    }

    private void ReportFailure()
    {
        if (Volatile.Read(ref _disposed) == 0 && Interlocked.Exchange(ref _failed, 1) == 0)
        {
            Failed?.Invoke(this, EventArgs.Empty);
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_packetGate)
        {
            if (_disposal is null)
            {
                Volatile.Write(ref _disposed, 1);
                _packets.Writer.TryComplete();
                // Playback may report its failure from a native callback.
                _disposal = Task.Run(DisposeCoreAsync);
            }
            return new ValueTask(_disposal);
        }
    }

    private async Task DisposeCoreAsync()
    {
        try
        {
            await _cancellation.CancelAsync().ConfigureAwait(false);
            if (_worker is not null) await _worker.ConfigureAwait(false);
        }
        finally
        {
            _packets.Reader.TryRead(out _);
            _output.Stopped -= OnPlaybackStopped;
            _buffer.ClearBuffer();
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
            finally
            {
                _cancellation.Dispose();
            }
        }
    }
}
