using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using SharpGen.Runtime;
using Vortice.MediaFoundation;
using static Vortice.MediaFoundation.MediaFactory;

namespace WebRtcSpike.Host;

internal sealed class MediaFoundationH264Decoder : IDisposable
{
    internal const int Width = 1920;
    internal const int Height = 1080;
    internal const int FrameBytes = Width * Height * 3 / 2;
    private const int Maximum1080pDecoderBytes = Width * 1088 * 3 / 2;
    private const uint ClsctxInprocServer = 1;
    private static readonly Guid MicrosoftH264Decoder = new("62CE7E72-4C71-4D20-B15D-452831A87D9D");
    private static readonly Guid H264VideoFormat = new(0x34363248, 0x0000, 0x0010, 0x80, 0x00, 0x00, 0xaa, 0x00, 0x38, 0x9b, 0x71);
    private readonly IMFTransform _decoder;
    private readonly int _minimumInputBytes;
    private long _sampleTime;
    private int _decodedWidth = Width;
    private int _decodedHeight = Height;
    private int _decodedStride = Width;
    private int _visibleWidth = Width;
    private int _visibleHeight = Height;
    private int _visibleLeft;
    private int _visibleTop;
    private bool _decodedLayoutReported;
    private byte[] _decodedBuffer = [];
    private bool _disposed;

    internal (int Width, int Height) DecodedSize => (_decodedWidth, _decodedHeight);
    internal (int Width, int Height) VisibleSize => (_visibleWidth, _visibleHeight);

    internal MediaFoundationH264Decoder()
    {
        MFStartup(true).CheckError();
        try
        {
            _decoder = CreateMicrosoftH264Decoder();
            using (IMFAttributes decoderAttributes = _decoder.Attributes)
                decoderAttributes.Set(SinkWriterAttributeKeys.LowLatency, 1u).CheckError();
            using IMFMediaType inputType = CreateH264InputType();
            _decoder.SetInputType(0, inputType, 0);
            using IMFMediaType outputType = GetNv12OutputType();
            _decoder.SetOutputType(0, outputType, 0);
            _minimumInputBytes = Math.Max(0, _decoder.GetInputStreamInfo(0).Size);
            _decoder.ProcessMessage(TMessageType.MessageNotifyBeginStreaming, UIntPtr.Zero);
            _decoder.ProcessMessage(TMessageType.MessageNotifyStartOfStream, UIntPtr.Zero);
        }
        catch
        {
            MFShutdown();
            throw;
        }
    }

    internal byte[]? Decode(ReadOnlySpan<byte> annexBAccessUnit)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (annexBAccessUnit.IsEmpty || annexBAccessUnit.Length > H264RtpDepacketizer.MaximumAccessUnitBytes)
            throw new ArgumentException("The H.264 access unit is empty or too large.", nameof(annexBAccessUnit));

        byte[]? latestFrame = null;

        try
        {
            using IMFMediaBuffer inputBuffer = MFCreateMemoryBuffer(Math.Max(annexBAccessUnit.Length, _minimumInputBytes));
            inputBuffer.Lock(out nint inputPointer, out _, out _);
            try
            {
                unsafe { annexBAccessUnit.CopyTo(new Span<byte>(inputPointer.ToPointer(), annexBAccessUnit.Length)); }
            }
            finally { inputBuffer.Unlock(); }
            inputBuffer.CurrentLength = annexBAccessUnit.Length;

            using IMFSample inputSample = MFCreateSample();
            inputSample.AddBuffer(inputBuffer);
            inputSample.SampleTime = _sampleTime;
            inputSample.SampleDuration = 10_000_000 / 30;
            _sampleTime += inputSample.SampleDuration;
            try
            {
                _decoder.ProcessInput(0, inputSample, 0);
            }
            catch (SharpGenException exception) when (exception.ResultCode == ResultCode.Notaccepting)
            {
                latestFrame = DrainOutput();
                _decoder.ProcessInput(0, inputSample, 0);
            }

            byte[]? currentFrame = DrainOutput();
            if (currentFrame is null) return latestFrame;
            ReturnFrame(latestFrame);
            return currentFrame;
        }
        catch
        {
            ReturnFrame(latestFrame);
            throw;
        }
    }

    internal void Flush()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _decoder.ProcessMessage(TMessageType.MessageCommandFlush, UIntPtr.Zero);
        _decoder.ProcessMessage(TMessageType.MessageNotifyStartOfStream, UIntPtr.Zero);
    }

    private byte[]? DrainOutput()
    {
        byte[]? latestFrame = null;
        try
        {
            while (true)
            {
                OutputStreamInfo streamInfo = _decoder.GetOutputStreamInfo(0);
                bool decoderProvidesSample = (streamInfo.Flags & (int)OutputStreamInfoFlags.OutputStreamProvidesSamples) != 0;
                using IMFMediaBuffer? callerBuffer = decoderProvidesSample
                    ? null
                    : MFCreateMemoryBuffer(Math.Max(Maximum1080pDecoderBytes, streamInfo.Size));
                using IMFSample? callerSample = decoderProvidesSample ? null : MFCreateSample();
                callerSample?.AddBuffer(callerBuffer!);
                {
                    var output = new OutputDataBuffer { StreamID = 0, Sample = callerSample };
                    Result result = _decoder.ProcessOutput(ProcessOutputFlags.None, 1, ref output, out _);
                    using IMFCollection? events = output.Events;
                    if (result == ResultCode.TransformNeedMoreInput) return latestFrame;
                    if (result == ResultCode.TransformStreamChange)
                    {
                        if (decoderProvidesSample) output.Sample?.Dispose();
                        using IMFMediaType outputType = GetNv12OutputType();
                        _decoder.SetOutputType(0, outputType, 0);
                        UpdateDecodedLayout(outputType);
                        continue;
                    }
                    result.CheckError();

                    IMFSample completedSample = decoderProvidesSample
                        ? output.Sample ?? throw new InvalidOperationException("The H.264 decoder returned no output sample.")
                        : callerSample!;
                    try
                    {
                        using IMFMediaBuffer contiguous = completedSample.ConvertToContiguousBuffer();
                        int required = checked(_decodedStride * _decodedHeight * 3 / 2);
                        if (contiguous.CurrentLength < required)
                            throw new InvalidOperationException($"The H.264 decoder returned a truncated NV12 frame ({contiguous.CurrentLength} bytes; expected at least {required}).");
                        if (!_decodedLayoutReported)
                        {
                            string aperture = _visibleWidth == _decodedWidth && _visibleHeight == _decodedHeight
                                ? ""
                                : $"; visible {_visibleWidth}x{_visibleHeight}";
                            Console.WriteLine($"Decoder output: {_decodedWidth}x{_decodedHeight} NV12{aperture}; stride {_decodedStride}; sample bytes {contiguous.CurrentLength}.");
                            _decodedLayoutReported = true;
                        }
                        if (_decodedBuffer.Length < contiguous.CurrentLength)
                            _decodedBuffer = GC.AllocateUninitializedArray<byte>(contiguous.CurrentLength);
                        contiguous.Lock(out nint pointer, out _, out int currentLength);
                        try { Marshal.Copy(pointer, _decodedBuffer, 0, currentLength); }
                        finally { contiguous.Unlock(); }

                        byte[] frame = ArrayPool<byte>.Shared.Rent(FrameBytes);
                        try
                        {
                            Nv12FrameComposer.FitIntoCanvas(
                                _decodedBuffer.AsSpan(0, currentLength),
                                _visibleWidth,
                                _visibleHeight,
                                _decodedStride,
                                _visibleLeft,
                                _visibleTop,
                                _decodedHeight,
                                frame.AsSpan(0, FrameBytes),
                                Width,
                                Height);
                        }
                        catch
                        {
                            ReturnFrame(frame);
                            throw;
                        }
                        ReturnFrame(latestFrame);
                        latestFrame = frame;
                    }
                    finally
                    {
                        if (decoderProvidesSample) completedSample.Dispose();
                    }
                }
            }
        }
        catch
        {
            ReturnFrame(latestFrame);
            throw;
        }
    }

    internal static void ReturnFrame(byte[]? frame)
    {
        if (frame is not null) ArrayPool<byte>.Shared.Return(frame);
    }

    private void UpdateDecodedLayout(IMFMediaType outputType)
    {
        int previousWidth = _decodedWidth;
        int previousHeight = _decodedHeight;
        if (outputType.GetGUID(MediaTypeAttributeKeys.Subtype) != VideoFormatGuids.NV12)
            throw new InvalidOperationException("The H.264 decoder changed to a non-NV12 output type.");
        ulong packedSize = outputType.GetUInt64(MediaTypeAttributeKeys.FrameSize);
        _decodedWidth = checked((int)(packedSize >> 32));
        _decodedHeight = checked((int)(packedSize & uint.MaxValue));
        if (_decodedWidth <= 0 || _decodedHeight <= 0 || (_decodedWidth & 1) != 0 || (_decodedHeight & 1) != 0)
            throw new InvalidOperationException("The H.264 decoder returned invalid NV12 dimensions.");
        try
        {
            _decodedStride = checked((int)outputType.GetUInt32(MediaTypeAttributeKeys.DefaultStride));
        }
        catch (SharpGenException)
        {
            _decodedStride = _decodedWidth;
        }
        if (_decodedStride < _decodedWidth)
            throw new InvalidOperationException("The H.264 decoder returned an invalid NV12 stride.");
        (_visibleLeft, _visibleTop, _visibleWidth, _visibleHeight) = ReadDisplayAperture(outputType);
        if (_decodedWidth != previousWidth || _decodedHeight != previousHeight)
            _decodedLayoutReported = false;
    }

    private (int Left, int Top, int Width, int Height) ReadDisplayAperture(IMFMediaType outputType)
    {
        foreach (Guid key in new[]
                 {
                     MediaTypeAttributeKeys.MinimumDisplayAperture,
                     MediaTypeAttributeKeys.GeometricAperture,
                     MediaTypeAttributeKeys.PanScanAperture
                 })
        {
            try
            {
                byte[] value = outputType.GetBlob(key);
                if (value.Length < 16) continue;
                int left = BinaryPrimitives.ReadInt16LittleEndian(value.AsSpan(0));
                int top = BinaryPrimitives.ReadInt16LittleEndian(value.AsSpan(4));
                int width = BinaryPrimitives.ReadInt32LittleEndian(value.AsSpan(8));
                int height = BinaryPrimitives.ReadInt32LittleEndian(value.AsSpan(12));
                if (left >= 0 && top >= 0 && width > 0 && height > 0 &&
                    (left & 1) == 0 && (top & 1) == 0 && (width & 1) == 0 && (height & 1) == 0 &&
                    left + width <= _decodedWidth && top + height <= _decodedHeight)
                {
                    return (left, top, width, height);
                }
            }
            catch (SharpGenException) { }
        }
        return (0, 0, _decodedWidth, _decodedHeight);
    }

    private static IMFMediaType CreateH264InputType()
    {
        IMFMediaType type = MFCreateMediaType();
        try
        {
            type.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video).CheckError();
            type.Set(MediaTypeAttributeKeys.Subtype, H264VideoFormat).CheckError();
            MFSetAttributeRatio(type, MediaTypeAttributeKeys.FrameRate, 30, 1).CheckError();
            type.SetEnumValue(MediaTypeAttributeKeys.InterlaceMode, VideoInterlaceMode.MixedInterlaceOrProgressive).CheckError();
            MFSetAttributeRatio(type, MediaTypeAttributeKeys.PixelAspectRatio, 1, 1).CheckError();
            return type;
        }
        catch
        {
            type.Dispose();
            throw;
        }
    }

    private static IMFTransform CreateMicrosoftH264Decoder()
    {
        Guid interfaceId = typeof(IMFTransform).GUID;
        int result = CoCreateInstance(MicrosoftH264Decoder, 0, ClsctxInprocServer, interfaceId, out nint pointer);
        new Result(result).CheckError();
        return new IMFTransform(pointer);
    }

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoCreateInstance(
        in Guid classId,
        nint outer,
        uint context,
        in Guid interfaceId,
        out nint instance);

    private IMFMediaType GetNv12OutputType()
    {
        for (int typeIndex = 0; typeIndex < 32; ++typeIndex)
        {
            IMFMediaType candidate;
            try
            {
                candidate = _decoder.GetOutputAvailableType(0, typeIndex);
            }
            catch (SharpGenException)
            {
                break;
            }
            if (candidate.GetGUID(MediaTypeAttributeKeys.Subtype) == VideoFormatGuids.NV12)
                return candidate;
            candidate.Dispose();
        }
        throw new NotSupportedException("The Windows H.264 decoder exposes no NV12 output type.");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            _decoder.ProcessMessage(TMessageType.MessageNotifyEndOfStream, UIntPtr.Zero);
            _decoder.ProcessMessage(TMessageType.MessageNotifyEndStreaming, UIntPtr.Zero);
        }
        catch (SharpGenException) { }
        _decoder.Dispose();
        MFShutdown();
    }
}
