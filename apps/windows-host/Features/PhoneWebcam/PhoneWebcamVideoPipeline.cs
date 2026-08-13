using System.Buffers;
using System.Threading.Channels;
using System.Diagnostics.CodeAnalysis;

namespace VolturaAir.Host.Features.PhoneWebcam;

internal sealed record PhoneWebcamVideoQuality(int Width, int Height);

[SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "IPhoneWebcamFeature.Publish takes ownership of every PhoneWebcamFrame and either queues or disposes it.")]
internal sealed class PhoneWebcamVideoPipeline : IAsyncDisposable
{
    private readonly Channel<EncodedAccessUnit> _accessUnits = Channel.CreateBounded<EncodedAccessUnit>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    private readonly IPhoneWebcamFeature _feature;
    private readonly PhoneWebcamFrameSequence _frameSequence;
    private readonly CancellationTokenSource _shutdown = new();
    private Task _worker = Task.CompletedTask;
    private byte[]? _sequenceParameterSet;
    private byte[]? _pictureParameterSet;
    private int _lastWidth;
    private int _lastHeight;
    private int _recoveryRequested;
    private bool _waitingForKeyFrame = true;
    private int _disposeState;
    private int _started;

    internal PhoneWebcamVideoPipeline(IPhoneWebcamFeature feature, PhoneWebcamFrameSequence frameSequence)
    {
        _feature = feature;
        _frameSequence = frameSequence;
    }

    internal event EventHandler? KeyFrameRequested;
    internal event EventHandler? Failed;
    internal event Action<PhoneWebcamVideoQuality>? QualityChanged;

    internal ulong AllocateFrameSequence() => _frameSequence.Next();

    internal void Start()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);
        if (Interlocked.Exchange(ref _started, 1) != 0)
        {
            throw new InvalidOperationException("The Phone webcam decoder pipeline is already running.");
        }

        _worker = Task.Run(() => DecodeAsync(_shutdown.Token));
    }

    internal void Submit(byte[] accessUnit, uint rtpTimestamp)
    {
        ArgumentNullException.ThrowIfNull(accessUnit);
        if (!_accessUnits.Writer.TryWrite(new EncodedAccessUnit(accessUnit, rtpTimestamp)))
        {
            Interlocked.Exchange(ref _recoveryRequested, 1);
            KeyFrameRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        _accessUnits.Writer.TryComplete();
        await _shutdown.CancelAsync().ConfigureAwait(false);
        try
        {
            await _worker.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        _shutdown.Dispose();
    }

    private async Task DecodeAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var decoder = new MediaFoundationH264Decoder();
            while (await _accessUnits.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (Interlocked.Exchange(ref _recoveryRequested, 0) != 0)
                {
                    RecoverDecoder(decoder);
                    continue;
                }

                while (_accessUnits.Reader.TryRead(out EncodedAccessUnit? accessUnit))
                {
                    UpdateParameterSets(accessUnit.Bytes);
                    byte[] decoderInput = accessUnit.Bytes;
                    if (_waitingForKeyFrame)
                    {
                        if (!ContainsNalType(decoderInput, 5))
                        {
                            continue;
                        }

                        byte[]? configured = AddRequiredParameterSets(decoderInput);
                        if (configured is null)
                        {
                            KeyFrameRequested?.Invoke(this, EventArgs.Empty);
                            continue;
                        }

                        decoderInput = configured;
                    }

                    try
                    {
                        byte[]? frame = decoder.Decode(decoderInput);
                        _waitingForKeyFrame = false;
                        if (frame is not null)
                        {
                            var visible = decoder.VisibleSize;
                            if (visible.Width != _lastWidth || visible.Height != _lastHeight)
                            {
                                _lastWidth = visible.Width;
                                _lastHeight = visible.Height;
                                QualityChanged?.Invoke(new PhoneWebcamVideoQuality(visible.Width, visible.Height));
                            }
                            _feature.Publish(new PhoneWebcamFrame(
                                AllocateFrameSequence(),
                                accessUnit.RtpTimestamp,
                                new PooledFrameOwner(frame, MediaFoundationH264Decoder.FrameBytes)));
                        }
                    }
                    catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or SharpGen.Runtime.SharpGenException)
                    {
                        RecoverDecoder(decoder);
                        KeyFrameRequested?.Invoke(this, EventArgs.Empty);
                        break;
                    }

                    if (Volatile.Read(ref _recoveryRequested) != 0)
                    {
                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _ = exception;
            Failed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void RecoverDecoder(MediaFoundationH264Decoder decoder)
    {
        while (_accessUnits.Reader.TryRead(out _))
        {
        }

        decoder.Flush();
        _waitingForKeyFrame = true;
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
                if (nalType == 7)
                {
                    _sequenceParameterSet = accessUnit[offset..nalEnd].ToArray();
                }
                else if (nalType == 8)
                {
                    _pictureParameterSet = accessUnit[offset..nalEnd].ToArray();
                }
            }

            offset = next;
        }
    }

    private byte[]? AddRequiredParameterSets(ReadOnlySpan<byte> accessUnit)
    {
        if (_sequenceParameterSet is null || _pictureParameterSet is null)
        {
            return null;
        }

        bool hasSps = ContainsNalType(accessUnit, 7);
        bool hasPps = ContainsNalType(accessUnit, 8);
        if (hasSps && hasPps)
        {
            return accessUnit.ToArray();
        }

        int prefixLength = (hasSps ? 0 : _sequenceParameterSet.Length) +
            (hasPps ? 0 : _pictureParameterSet.Length);
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

    internal static bool ContainsNalType(ReadOnlySpan<byte> accessUnit, int expectedType)
    {
        int offset = FindStartCode(accessUnit, 0);
        while (offset >= 0)
        {
            int nalStart = offset + 4;
            if (nalStart < accessUnit.Length && (accessUnit[nalStart] & 0x1f) == expectedType)
            {
                return true;
            }

            offset = FindStartCode(accessUnit, nalStart);
        }

        return false;
    }

    private static int FindStartCode(ReadOnlySpan<byte> value, int start)
    {
        for (int index = start; index <= value.Length - 4; ++index)
        {
            if (value[index] == 0 && value[index + 1] == 0 &&
                value[index + 2] == 0 && value[index + 3] == 1)
            {
                return index;
            }
        }

        return -1;
    }

    private sealed record EncodedAccessUnit(byte[] Bytes, uint RtpTimestamp);

    private sealed class PooledFrameOwner(byte[] frame, int length) : IMemoryOwner<byte>
    {
        private byte[]? _frame = frame;
        public Memory<byte> Memory => (_frame ?? throw new ObjectDisposedException(nameof(PooledFrameOwner))).AsMemory(0, length);
        public void Dispose()
        {
            byte[]? released = Interlocked.Exchange(ref _frame, null);
            MediaFoundationH264Decoder.ReturnFrame(released);
        }
    }
}
