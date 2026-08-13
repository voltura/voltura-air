using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using SharpGen.Runtime;
using Vortice.MediaFoundation;
using static Vortice.MediaFoundation.MediaFactory;

namespace VolturaAir.Host.Features.PhoneWebcam;

internal sealed class PhoneWebcamPreviewFrame : IDisposable
{
    private byte[]? _buffer;

    internal PhoneWebcamPreviewFrame(byte[] buffer)
    {
        _buffer = buffer;
    }

    internal byte[] Buffer => _buffer ?? throw new ObjectDisposedException(nameof(PhoneWebcamPreviewFrame));

    public void Dispose()
    {
        byte[]? buffer = Interlocked.Exchange(ref _buffer, null);
        if (buffer is not null)
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}

internal interface IPhoneWebcamPreviewSession : IDisposable
{
    Task StopAsync();
}

[SuppressMessage("Usage", "CA2213:Disposable fields should be disposed", Justification = "Dispose cancels capture and a worker continuation disposes the cancellation source after the worker stops using its token.")]
internal sealed class PhoneWebcamPreviewSession : IPhoneWebcamPreviewSession
{
    private const string CameraName = "Voltura Air Webcam";
    private const int SourceWidth = 1920;
    private const int SourceHeight = 1080;
    private const int SourceBytes = SourceWidth * SourceHeight * 3 / 2;
    internal const int PreviewWidth = 640;
    internal const int PreviewHeight = 360;
    internal const int PreviewStride = PreviewWidth * 4;
    private const int PreviewBytes = PreviewStride * PreviewHeight;

    private readonly Action<PhoneWebcamPreviewFrame> _publish;
    private readonly Action<string> _reportFailure;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Lock _sourceLock = new();
    private readonly Lock _stopLock = new();
    private readonly Task _worker;
    private IMFMediaSource? _source;
    private Task? _stopTask;

    internal PhoneWebcamPreviewSession(
        Action<PhoneWebcamPreviewFrame> publish,
        Action<string> reportFailure)
    {
        _publish = publish;
        _reportFailure = reportFailure;
        _worker = Task.Run(Capture);
    }

    public void Dispose() => _ = StopAsync();

    public Task StopAsync()
    {
        lock (_stopLock)
        {
            if (_stopTask is not null)
            {
                return _stopTask;
            }

            _stopTask = CompleteStopAsync();
            return _stopTask;
        }
    }

    private async Task CompleteStopAsync()
    {
        await _shutdown.CancelAsync().ConfigureAwait(false);
        lock (_sourceLock)
        {
            TryShutdown(_source);
        }
        await _worker.ConfigureAwait(false);
        _shutdown.Dispose();
    }

    private void Capture()
    {
        try
        {
            CaptureCore(_shutdown.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (SharpGenException exception) when (
            _shutdown.IsCancellationRequested && exception.ResultCode == ResultCode.Shutdown)
        {
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _reportFailure($"Voltura Air could not open its Windows camera preview: {exception.Message}");
        }
    }

    private void CaptureCore(CancellationToken cancellationToken)
    {
        MFStartup(true).CheckError();
        try
        {
            using IMFActivateCollection devices = MFEnumVideoDeviceSources();
            IMFActivate activate = devices.FirstOrDefault(device =>
                    device.FriendlyName.StartsWith(CameraName, StringComparison.Ordinal))
                ?? throw new InvalidOperationException("Voltura Air Webcam is not available to Windows camera consumers.");
            using IMFMediaSource source = activate.ActivateObject<IMFMediaSource>();
            SetSource(source, cancellationToken);
            try
            {
                using IMFAttributes attributes = MFCreateAttributes(1);
                attributes.Set(SourceReaderAttributeKeys.EnableVideoProcessing, false).CheckError();
                using IMFSourceReader reader = MFCreateSourceReaderFromMediaSource(source, attributes);
                using IMFMediaType requestedType = CreateCaptureType();
                reader.SetCurrentMediaType(SourceReaderIndex.FirstVideoStream, requestedType);
                CaptureFrames(reader, cancellationToken);
            }
            finally
            {
                lock (_sourceLock)
                {
                    _source = null;
                }
                TryShutdown(source);
            }
        }
        finally
        {
            MFShutdown();
        }
    }

    private void SetSource(IMFMediaSource source, CancellationToken cancellationToken)
    {
        lock (_sourceLock)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _source = source;
        }
    }

    private void CaptureFrames(IMFSourceReader reader, CancellationToken cancellationToken)
    {
        int frameNumber = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using IMFSample? sample = reader.ReadSample(
                SourceReaderIndex.FirstVideoStream,
                SourceReaderControlFlag.None,
                out _,
                out SourceReaderFlag flags,
                out _);
            if ((flags & SourceReaderFlag.Error) != 0)
            {
                throw new InvalidOperationException("The Windows camera consumer reported a capture error.");
            }
            if (sample is null || ++frameNumber % 2 != 0)
            {
                continue;
            }

            using IMFMediaBuffer buffer = sample.ConvertToContiguousBuffer();
            if (buffer.CurrentLength != SourceBytes)
            {
                throw new InvalidOperationException(
                    $"The Windows camera returned {buffer.CurrentLength} NV12 bytes; expected {SourceBytes}.");
            }

            byte[]? preview = null;
            PhoneWebcamPreviewFrame? frame = null;
            buffer.Lock(out nint pointer, out _, out _);
            try
            {
                preview = ArrayPool<byte>.Shared.Rent(PreviewBytes);
                ConvertNv12ToPreview(pointer, preview);
                frame = new PhoneWebcamPreviewFrame(preview);
                _publish(frame);
                frame = null;
                preview = null;
            }
            finally
            {
                buffer.Unlock();
                frame?.Dispose();
                if (preview is not null && frame is null)
                {
                    ArrayPool<byte>.Shared.Return(preview);
                }
            }
        }
    }

    private static IMFMediaType CreateCaptureType()
    {
        IMFMediaType type = MFCreateMediaType();
        try
        {
            type.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video).CheckError();
            type.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.NV12).CheckError();
            MFSetAttributeSize(type, MediaTypeAttributeKeys.FrameSize, SourceWidth, SourceHeight).CheckError();
            MFSetAttributeRatio(type, MediaTypeAttributeKeys.FrameRate, 30, 1).CheckError();
            return type;
        }
        catch
        {
            type.Dispose();
            throw;
        }
    }

    internal static unsafe void ConvertNv12ToPreview(nint sourcePointer, byte[] target)
    {
        byte* source = (byte*)sourcePointer;
        fixed (byte* destinationStart = target)
        {
            byte* destination = destinationStart;
            for (int previewY = 0; previewY < PreviewHeight; previewY += 1)
            {
                int sourceY = previewY * 3;
                byte* yRow = source + sourceY * SourceWidth;
                byte* uvRow = source + SourceWidth * SourceHeight + (sourceY / 2) * SourceWidth;
                for (int previewX = 0; previewX < PreviewWidth; previewX += 1)
                {
                    int sourceX = previewX * 3;
                    int luminance = Math.Max(0, yRow[sourceX] - 16);
                    int chromaOffset = sourceX & ~1;
                    int blueChroma = uvRow[chromaOffset] - 128;
                    int redChroma = uvRow[chromaOffset + 1] - 128;
                    destination[0] = Clamp((298 * luminance + 516 * blueChroma + 128) >> 8);
                    destination[1] = Clamp((298 * luminance - 100 * blueChroma - 208 * redChroma + 128) >> 8);
                    destination[2] = Clamp((298 * luminance + 409 * redChroma + 128) >> 8);
                    destination[3] = byte.MaxValue;
                    destination += 4;
                }
            }
        }
    }

    private static byte Clamp(int value) => (byte)Math.Clamp(value, byte.MinValue, byte.MaxValue);

    private static void TryShutdown(IMFMediaSource? source)
    {
        if (source is null)
        {
            return;
        }

        try
        {
            source.Shutdown();
        }
        catch (SharpGenException exception) when (exception.ResultCode == ResultCode.Shutdown)
        {
        }
    }
}
