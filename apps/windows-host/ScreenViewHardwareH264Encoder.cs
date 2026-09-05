using System.Buffers.Binary;
using System.Runtime.InteropServices;
using SharpGen.Runtime;
using Vortice.Direct3D11;
using Vortice.MediaFoundation;
using static Vortice.MediaFoundation.MediaFactory;

namespace VolturaAir.Host;

internal sealed class ScreenViewHardwareH264Encoder : IScreenViewFrameEncoder
{
    private static readonly Guid H264VideoFormat = new(0x34363248, 0x0000, 0x0010, 0x80, 0x00, 0x00, 0xaa, 0x00, 0x38, 0x9b, 0x71);
    private readonly IMFTransform _transform;
    private readonly IMFMediaEventGenerator _events;
    private readonly IMFDXGIDeviceManager _deviceManager;
    private readonly ScreenViewEncoderControls _controls;
    private readonly long _frameDuration;
    private int _inputRequests;
    private long _nextTimestamp;
    private byte[] _parameterSets = [];
    private bool _disposed;

    public ScreenViewHardwareH264Encoder(ID3D11Device device, int width, int height, int framesPerSecond, int bitrate, bool enableOptionalControls = true)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (width <= 0 || height <= 0 || framesPerSecond is < 1 or > 60 || bitrate <= 0)
            throw new ArgumentOutOfRangeException(nameof(width));
        Width = width & ~1;
        Height = height & ~1;
        FramesPerSecond = framesPerSecond;
        Bitrate = bitrate;
        _frameDuration = 10_000_000L / framesPerSecond;
        MFStartup(true).CheckError();
        IMFTransform? transform = null;
        IMFMediaEventGenerator? events = null;
        IMFDXGIDeviceManager? manager = null;
        ScreenViewEncoderControls? controls = null;
        try
        {
            manager = MFCreateDXGIDeviceManager();
            manager.ResetDevice(device).CheckError();
            transform = CreateTransform(manager, Width, Height, framesPerSecond, bitrate);
            controls = new ScreenViewEncoderControls(transform.NativePointer);
            if (enableOptionalControls) controls.Configure(bitrate);
            events = transform.QueryInterface<IMFMediaEventGenerator>();
            transform.ProcessMessage(TMessageType.MessageNotifyBeginStreaming, 0);
            transform.ProcessMessage(TMessageType.MessageNotifyStartOfStream, 0);
            _deviceManager = manager;
            _transform = transform;
            _events = events;
            _controls = controls;
            controls = null;
            manager = null;
            transform = null;
            events = null;
        }
        catch
        {
            controls?.Dispose();
            events?.Dispose();
            transform?.Dispose();
            manager?.Dispose();
            MFShutdown();
            throw;
        }
    }

    public int Width { get; }
    public int Height { get; }
    public int FramesPerSecond { get; }
    public int Bitrate { get; private set; }

    public bool TryRequestKeyFrame() => _controls.TryRequestKeyFrame();

    public bool TrySetBitrate(int bitrate)
    {
        if (!_controls.TrySetBitrate(bitrate)) return false;
        Bitrate = bitrate;
        return true;
    }

    public ScreenViewEncodedFrame Encode(ID3D11Texture2D source)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(source);
        Texture2DDescription description = source.Description;
        if (description.Width != Width || description.Height != Height)
            throw new ArgumentException("The GPU surface dimensions do not match the H.264 encoder.", nameof(source));

        ScreenViewDevelopmentTrace.Stage("encoder-input");
        WaitForInputRequest();
        using (IMFMediaBuffer buffer = MFCreateDXGISurfaceBuffer(typeof(ID3D11Texture2D).GUID, source, 0, false))
        {
            buffer.CurrentLength = checked(Width * Height * 3 / 2);
            using IMFSample sample = MFCreateSample();
            sample.AddBuffer(buffer);
            sample.SampleTime = _nextTimestamp;
            sample.SampleDuration = _frameDuration;
            _nextTimestamp += _frameDuration;
            _transform.ProcessInput(0, sample, 0);
            _inputRequests--;
        }

        while (true)
        {
            ScreenViewDevelopmentTrace.Stage("encoder-output");
            using IMFMediaEvent mediaEvent = _events.GetEvent(0);
            mediaEvent.Status.CheckError();
            if (mediaEvent.EventType == MediaEventTypes.TransformNeedInput)
            {
                _inputRequests++;
                continue;
            }
            if (mediaEvent.EventType != MediaEventTypes.TransformHaveOutput) continue;

            var output = new OutputDataBuffer { StreamID = 0 };
            Result result = _transform.ProcessOutput(ProcessOutputFlags.None, 1, ref output, out _);
            try
            {
                if (result == Vortice.MediaFoundation.ResultCode.TransformStreamChange)
                {
                    using IMFMediaType changedType = _transform.GetOutputAvailableType(0, 0);
                    _transform.SetOutputType(0, changedType, 0);
                    RefreshParameterSets();
                    continue;
                }
                result.CheckError();
                using IMFSample encodedSample = output.Sample
                    ?? throw new ScreenViewCaptureException("encoder-failed", "The Windows H.264 encoder returned no video sample.");
                byte[] bytes = NormalizeToAnnexB(ReadSample(encodedSample));
                if (ContainsNalType(bytes, 7)) bytes = ScreenViewH264ColorMetadata.Apply(bytes);
                if (_parameterSets.Length == 0) RefreshParameterSets();
                bool keyFrame = ContainsNalType(bytes, 5);
                if (keyFrame && _parameterSets.Length > 0 && !ContainsNalType(bytes, 7))
                {
                    byte[] complete = new byte[_parameterSets.Length + bytes.Length];
                    _parameterSets.CopyTo(complete, 0);
                    bytes.CopyTo(complete, _parameterSets.Length);
                    bytes = complete;
                }
                ScreenViewDevelopmentTrace.Encoded(keyFrame);
                return new ScreenViewEncodedFrame(bytes, Width, Height, FramesPerSecond, keyFrame);
            }
            finally
            {
                output.Events?.Dispose();
                if (result == Vortice.MediaFoundation.ResultCode.TransformStreamChange) output.Sample?.Dispose();
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _transform.ProcessMessage(TMessageType.MessageCommandFlush, 0); }
        catch (SharpGenException) { }
        try { _transform.ProcessMessage(TMessageType.MessageNotifyEndOfStream, 0); }
        catch (SharpGenException) { }
        try { _transform.ProcessMessage(TMessageType.MessageNotifyEndStreaming, 0); }
        catch (SharpGenException) { }
        _events.Dispose();
        _controls.Dispose();
        _transform.Dispose();
        _deviceManager.Dispose();
        MFShutdown();
    }

    private void WaitForInputRequest()
    {
        while (_inputRequests == 0)
        {
            using IMFMediaEvent mediaEvent = _events.GetEvent(0);
            mediaEvent.Status.CheckError();
            if (mediaEvent.EventType == MediaEventTypes.TransformNeedInput) _inputRequests++;
        }
    }

    private void RefreshParameterSets()
    {
        using IMFMediaType currentType = _transform.GetOutputCurrentType(0);
        byte[] configuration;
        try
        {
            configuration = currentType.GetBlob(MediaTypeAttributeKeys.MpegSequenceHeader);
        }
        catch (SharpGenException)
        {
            return;
        }
        byte[] parameterSets = ConvertAvcConfiguration(configuration);
        _parameterSets = parameterSets.Length == 0 ? [] : ScreenViewH264ColorMetadata.Apply(parameterSets);
    }

    private static IMFTransform CreateTransform(
        IMFDXGIDeviceManager manager,
        int width,
        int height,
        int framesPerSecond,
        int bitrate)
    {
        using IMFActivateCollection activations = EnumerateHardwareEncoders();
        IEnumerable<Func<IMFTransform>> candidates = activations.Select<IMFActivate, Func<IMFTransform>>(
            activation => () => activation.ActivateObject<IMFTransform>());
        return SelectFirstCompatible(
            candidates,
            candidate =>
            {
                using IMFAttributes attributes = candidate.Attributes;
                attributes.Set(TransformAttributeKeys.TransformAsyncUnlock, 1u).CheckError();
                attributes.Set(SinkWriterAttributeKeys.LowLatency, 1u).CheckError();
                candidate.ProcessMessage(TMessageType.MessageSetD3DManager, (nuint)manager.NativePointer);
                using IMFMediaType outputType = CreateVideoType(H264VideoFormat, width, height, framesPerSecond, bitrate);
                using IMFMediaType inputType = CreateVideoType(VideoFormatGuids.NV12, width, height, framesPerSecond, 0);
                candidate.SetOutputType(0, outputType, 0);
                candidate.SetInputType(0, inputType, 0);
            },
            lastFailure => new ScreenViewCaptureException(
                "encoder-unavailable",
                "This Windows graphics configuration has no compatible hardware H.264 screen encoder.",
                lastFailure));
    }

    internal static T SelectFirstCompatible<T>(
        IEnumerable<Func<T>> activators,
        Action<T> configure,
        Func<Exception?, Exception> createFailure)
        where T : class, IDisposable
    {
        Exception? lastFailure = null;
        foreach (Func<T> activate in activators)
        {
            T? candidate = default;
            try
            {
                candidate = activate();
                configure(candidate);
                return candidate;
            }
            catch (Exception ex) when (ex is SharpGenException or InvalidOperationException)
            {
                lastFailure = ex;
                candidate?.Dispose();
            }
        }

        throw createFailure(lastFailure);
    }

    private static IMFActivateCollection EnumerateHardwareEncoders()
    {
        var input = new RegisterTypeInfo
        {
            GuidMajorType = MediaTypeGuids.Video,
            GuidSubtype = VideoFormatGuids.NV12
        };
        var output = new RegisterTypeInfo
        {
            GuidMajorType = MediaTypeGuids.Video,
            GuidSubtype = H264VideoFormat
        };
        uint flags = (uint)(EnumFlag.EnumFlagAsyncmft | EnumFlag.EnumFlagHardware | EnumFlag.EnumFlagSortandfilter);
        return MFTEnumEx(TransformCategoryGuids.VideoEncoder, flags, input, output);
    }

    private static IMFMediaType CreateVideoType(Guid subtype, int width, int height, int framesPerSecond, int bitrate)
    {
        IMFMediaType type = MFCreateMediaType();
        type.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video).CheckError();
        type.Set(MediaTypeAttributeKeys.Subtype, subtype).CheckError();
        MFSetAttributeSize(type, MediaTypeAttributeKeys.FrameSize, (uint)width, (uint)height).CheckError();
        MFSetAttributeRatio(type, MediaTypeAttributeKeys.FrameRate, (uint)framesPerSecond, 1).CheckError();
        MFSetAttributeRatio(type, MediaTypeAttributeKeys.PixelAspectRatio, 1, 1).CheckError();
        type.SetEnumValue(MediaTypeAttributeKeys.InterlaceMode, VideoInterlaceMode.Progressive).CheckError();
        type.SetEnumValue(MediaTypeAttributeKeys.VideoPrimaries, VideoPrimaries.Bt709).CheckError();
        type.SetEnumValue(MediaTypeAttributeKeys.TransferFunction, VideoTransferFunction.FuncSRGB).CheckError();
        type.SetEnumValue(MediaTypeAttributeKeys.YuvMatrix, VideoTransferMatrix.Bt709).CheckError();
        type.SetEnumValue(MediaTypeAttributeKeys.VideoNominalRange, NominalRange.Range16_235).CheckError();
        if (bitrate > 0)
        {
            type.Set(MediaTypeAttributeKeys.AvgBitrate, (uint)bitrate).CheckError();
            type.Set(MediaTypeAttributeKeys.Mpeg2Profile, 66u).CheckError();
            type.Set(MediaTypeAttributeKeys.MaxKeyframeSpacing, (uint)(framesPerSecond * 2)).CheckError();
        }
        return type;
    }

    private static byte[] ReadSample(IMFSample sample)
    {
        using IMFMediaBuffer buffer = sample.ConvertToContiguousBuffer();
        buffer.Lock(out nint pointer, out _, out int length);
        try
        {
            if (length <= 0 || length > 16 * 1024 * 1024)
                throw new ScreenViewCaptureException("encoder-failed", "The Windows H.264 encoder returned an invalid video sample.");
            byte[] bytes = new byte[length];
            Marshal.Copy(pointer, bytes, 0, length);
            return bytes;
        }
        finally
        {
            buffer.Unlock();
        }
    }

    internal static bool ContainsNalType(ReadOnlySpan<byte> accessUnit, byte nalType)
    {
        bool startsWithAnnexB = accessUnit.Length >= 4 && accessUnit[0] == 0 && accessUnit[1] == 0 &&
            (accessUnit[2] == 1 || (accessUnit[2] == 0 && accessUnit[3] == 1));
        if (startsWithAnnexB)
        {
            for (int index = 0; index + 3 < accessUnit.Length; index++)
            {
                int header = accessUnit[index] == 0 && accessUnit[index + 1] == 0 && accessUnit[index + 2] == 1
                    ? index + 3
                    : index + 4 < accessUnit.Length && accessUnit[index] == 0 && accessUnit[index + 1] == 0 && accessUnit[index + 2] == 0 && accessUnit[index + 3] == 1
                        ? index + 4
                        : -1;
                if (header >= 0 && (accessUnit[header] & 0x1f) == nalType) return true;
            }
            return false;
        }
        int offset = 0;
        while (offset + 4 <= accessUnit.Length)
        {
            uint length = BinaryPrimitives.ReadUInt32BigEndian(accessUnit.Slice(offset, 4));
            offset += 4;
            if (length == 0 || length > accessUnit.Length - offset) return false;
            if ((accessUnit[offset] & 0x1f) == nalType) return true;
            offset += checked((int)length);
        }
        return false;
    }

    internal static byte[] ConvertAvcConfiguration(ReadOnlySpan<byte> configuration)
    {
        if (configuration.Length < 7 || configuration[0] != 1) return [];
        int offset = 5;
        int sequenceCount = configuration[offset++] & 0x1f;
        using var output = new MemoryStream();
        for (int index = 0; index < sequenceCount; index++)
        {
            if (!CopyNal(configuration, ref offset, output)) return [];
        }
        if (offset >= configuration.Length) return [];
        int pictureCount = configuration[offset++];
        for (int index = 0; index < pictureCount; index++)
        {
            if (!CopyNal(configuration, ref offset, output)) return [];
        }
        return output.ToArray();
    }

    internal static byte[] NormalizeToAnnexB(ReadOnlySpan<byte> accessUnit)
    {
        bool startsWithAnnexB = accessUnit.Length >= 4 && accessUnit[0] == 0 && accessUnit[1] == 0 &&
            (accessUnit[2] == 1 || (accessUnit[2] == 0 && accessUnit[3] == 1));
        if (startsWithAnnexB) return accessUnit.ToArray();

        using var output = new MemoryStream(accessUnit.Length);
        int offset = 0;
        while (offset + 4 <= accessUnit.Length)
        {
            uint length = BinaryPrimitives.ReadUInt32BigEndian(accessUnit.Slice(offset, 4));
            offset += 4;
            if (length == 0 || length > accessUnit.Length - offset)
                throw new ScreenViewCaptureException("encoder-failed", "The Windows H.264 encoder returned invalid NAL units.");
            output.Write([0, 0, 0, 1]);
            output.Write(accessUnit.Slice(offset, checked((int)length)));
            offset += checked((int)length);
        }
        if (offset != accessUnit.Length || output.Length == 0)
            throw new ScreenViewCaptureException("encoder-failed", "The Windows H.264 encoder returned invalid NAL units.");
        return output.ToArray();
    }

    private static bool CopyNal(ReadOnlySpan<byte> source, ref int offset, MemoryStream output)
    {
        if (offset + 2 > source.Length) return false;
        int length = BinaryPrimitives.ReadUInt16BigEndian(source.Slice(offset, 2));
        offset += 2;
        if (length <= 0 || offset + length > source.Length) return false;
        output.Write([0, 0, 0, 1]);
        output.Write(source.Slice(offset, length));
        offset += length;
        return true;
    }
}
