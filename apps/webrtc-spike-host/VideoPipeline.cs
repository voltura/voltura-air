using System.Threading.Channels;

namespace WebRtcSpike.Host;

internal sealed class VideoPipeline : IAsyncDisposable
{
    private const int EncodedQueueCapacity = 1;
    private readonly Channel<EncodedAccessUnit> _accessUnits = Channel.CreateBounded<EncodedAccessUnit>(new BoundedChannelOptions(EncodedQueueCapacity)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = true,
        SingleWriter = false
    });
    private readonly MediaFoundationH264Decoder _decoder = new();
    private readonly FramePipeServer _pipe = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _worker;
    private ulong _sequence;
    private int _recoveryRequested;
    private int _overflowReported;
    private bool _waitingForKeyFrame = true;
    private byte[]? _sequenceParameterSet;
    private byte[]? _pictureParameterSet;
    private Exception? _failure;
    private long _visibleSourceSize;

    internal VideoPipeline() => _worker = DecodeAsync(_shutdown.Token);
    internal Exception? Failure => Volatile.Read(ref _failure);
    internal (int Width, int Height) VisibleSourceSize
    {
        get
        {
            long packed = Volatile.Read(ref _visibleSourceSize);
            return ((int)(packed >> 32), (int)(packed & uint.MaxValue));
        }
    }
    internal event Action? KeyFrameRequested;

    internal void Submit(byte[] accessUnit, uint rtpTimestamp)
    {
        ArgumentNullException.ThrowIfNull(accessUnit);
        if (!_accessUnits.Writer.TryWrite(new EncodedAccessUnit(accessUnit, rtpTimestamp)))
        {
            Interlocked.Exchange(ref _recoveryRequested, 1);
            KeyFrameRequested?.Invoke();
            if (Interlocked.Exchange(ref _overflowReported, 1) == 0)
                Console.WriteLine("Video backlog dropped; requesting a fresh key frame.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        _accessUnits.Writer.TryComplete();
        _shutdown.Cancel();
        try { await _worker.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        await _pipe.DisposeAsync().ConfigureAwait(false);
        _decoder.Dispose();
        _shutdown.Dispose();
    }

    private async Task DecodeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await DecodeLoopAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            RecordFailure(exception);
            Console.Error.WriteLine($"Video pipeline stopped: {exception.Message}");
        }
    }

    private async Task DecodeLoopAsync(CancellationToken cancellationToken)
    {
        bool decodeErrorReported = false;
        while (await _accessUnits.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (Interlocked.Exchange(ref _recoveryRequested, 0) != 0)
            {
                RecoverDecoder();
                continue;
            }

            while (_accessUnits.Reader.TryRead(out EncodedAccessUnit? accessUnit))
            {
                UpdateParameterSets(accessUnit.Bytes);
                byte[] decoderInput = accessUnit.Bytes;
                if (_waitingForKeyFrame)
                {
                    if (!ContainsIdr(decoderInput)) continue;
                    byte[]? configuredKeyFrame = AddRequiredParameterSets(decoderInput);
                    if (configuredKeyFrame is null)
                    {
                        KeyFrameRequested?.Invoke();
                        continue;
                    }
                    decoderInput = configuredKeyFrame;
                }
                try
                {
                    byte[]? frame = _decoder.Decode(decoderInput);
                    _waitingForKeyFrame = false;
                    if (frame is not null)
                    {
                        try
                        {
                            (int visibleWidth, int visibleHeight) = _decoder.VisibleSize;
                            Volatile.Write(ref _visibleSourceSize, ((long)visibleWidth << 32) | (uint)visibleHeight);
                            _sequence = checked(_sequence + 1);
                            _pipe.Publish(new DecodedFrame(_sequence, accessUnit.RtpTimestamp, frame));
                            frame = null;
                            decodeErrorReported = false;
                        }
                        finally { MediaFoundationH264Decoder.ReturnFrame(frame); }
                    }
                }
                catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or SharpGen.Runtime.SharpGenException)
                {
                    if (!decodeErrorReported)
                    {
                        Console.Error.WriteLine($"H.264 decoder reset after a rejected access unit: {exception.Message}");
                        decodeErrorReported = true;
                    }
                    RecoverDecoder();
                    KeyFrameRequested?.Invoke();
                    break;
                }

                if (Volatile.Read(ref _recoveryRequested) != 0) break;
            }
        }
    }

    private void RecoverDecoder()
    {
        while (_accessUnits.Reader.TryRead(out _)) { }
        _decoder.Flush();
        _waitingForKeyFrame = true;
    }

    private void RecordFailure(Exception exception) =>
        Interlocked.CompareExchange(ref _failure, exception, null);

    internal static bool ContainsIdr(ReadOnlySpan<byte> accessUnit)
    {
        for (int index = 0; index <= accessUnit.Length - 5; ++index)
        {
            if (accessUnit[index] == 0 && accessUnit[index + 1] == 0 &&
                accessUnit[index + 2] == 0 && accessUnit[index + 3] == 1 &&
                (accessUnit[index + 4] & 0x1f) == 5)
            {
                return true;
            }
        }
        return false;
    }

    private void UpdateParameterSets(ReadOnlySpan<byte> accessUnit)
    {
        int offset = FindStartCode(accessUnit, 0);
        while (offset >= 0)
        {
            int nalStart = offset + 4;
            int next = FindStartCode(accessUnit, nalStart);
            int nalEnd = next >= 0 ? next : accessUnit.Length;
            if (nalStart < nalEnd)
            {
                int nalType = accessUnit[nalStart] & 0x1f;
                if (nalType == 7) _sequenceParameterSet = accessUnit[offset..nalEnd].ToArray();
                if (nalType == 8) _pictureParameterSet = accessUnit[offset..nalEnd].ToArray();
            }
            offset = next;
        }
    }

    private byte[]? AddRequiredParameterSets(ReadOnlySpan<byte> accessUnit)
    {
        if (_sequenceParameterSet is null || _pictureParameterSet is null) return null;
        bool hasSps = ContainsNalType(accessUnit, 7);
        bool hasPps = ContainsNalType(accessUnit, 8);
        if (hasSps && hasPps) return accessUnit.ToArray();

        int prefixLength = (hasSps ? 0 : _sequenceParameterSet.Length) + (hasPps ? 0 : _pictureParameterSet.Length);
        byte[] configured = GC.AllocateUninitializedArray<byte>(prefixLength + accessUnit.Length);
        int offset = 0;
        if (!hasSps)
        {
            _sequenceParameterSet.CopyTo(configured, offset);
            offset += _sequenceParameterSet.Length;
        }
        if (!hasPps)
        {
            _pictureParameterSet.CopyTo(configured, offset);
            offset += _pictureParameterSet.Length;
        }
        accessUnit.CopyTo(configured.AsSpan(offset));
        return configured;
    }

    private static bool ContainsNalType(ReadOnlySpan<byte> accessUnit, int expectedType)
    {
        int offset = FindStartCode(accessUnit, 0);
        while (offset >= 0)
        {
            int nalStart = offset + 4;
            if (nalStart < accessUnit.Length && (accessUnit[nalStart] & 0x1f) == expectedType) return true;
            offset = FindStartCode(accessUnit, nalStart);
        }
        return false;
    }

    private static int FindStartCode(ReadOnlySpan<byte> value, int start)
    {
        for (int index = start; index <= value.Length - 4; ++index)
        {
            if (value[index] == 0 && value[index + 1] == 0 && value[index + 2] == 0 && value[index + 3] == 1)
                return index;
        }
        return -1;
    }

    private sealed record EncodedAccessUnit(byte[] Bytes, uint RtpTimestamp);
}
