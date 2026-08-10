using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using SharpGen.Runtime;
using Vortice;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using static Vortice.Direct3D11.D3D11;
using static Vortice.DXGI.DXGI;
using D3DMapFlags = Vortice.Direct3D11.MapFlags;
using DrawingInterpolationMode = System.Drawing.Drawing2D.InterpolationMode;
using DxgiResultCode = Vortice.DXGI.ResultCode;

namespace VolturaAir.Host;

internal sealed partial class DxgiScreenViewCaptureSource : IScreenViewCaptureSource
{
    private const int MaxPatchCount = 24;
    private readonly Lock _gate = new();
    private CaptureSession? _session;

    internal static bool ShouldCaptureVisual(bool needsResynchronization, long lastPresentTime, int dirtyRectangleCount) =>
        dirtyRectangleCount > 0 || (needsResynchronization && lastPresentTime != 0);

    internal static List<Rectangle> NormalizeChangedRectangles(
        long lastPresentTime,
        int width,
        int height,
        IEnumerable<Rectangle> rectangles)
    {
        var bounds = new Rectangle(0, 0, width, height);
        List<Rectangle> usable = [.. rectangles
            .Select(rectangle => Rectangle.Intersect(bounds, rectangle))
            .Where(rectangle => rectangle.Width > 0 && rectangle.Height > 0)];
        if (usable.Count == 0 && lastPresentTime != 0) usable.Add(bounds);
        return usable;
    }

    public IReadOnlyList<ScreenViewSource> GetSources()
    {
        lock (_gate)
        {
            try
            {
                return [.. EnumerateOutputs().Select(item => new ScreenViewSource(
                    item.Id,
                    item.Label,
                    item.Width,
                    item.Height,
                    item.IsPrimary,
                    item.Left,
                    item.Top,
                    item.Rotation switch
                    {
                        ModeRotation.Rotate90 => ScreenViewRotation.Rotate90,
                        ModeRotation.Rotate180 => ScreenViewRotation.Rotate180,
                        ModeRotation.Rotate270 => ScreenViewRotation.Rotate270,
                        _ => ScreenViewRotation.Identity
                    }))];
            }
            catch (ScreenViewCaptureException)
            {
                throw;
            }
            catch (Exception ex) when (ex is SharpGenException or ExternalException or InvalidOperationException or NotSupportedException)
            {
                throw new ScreenViewCaptureException("capture-unavailable", "Windows desktop capture is unavailable.", ex);
            }
        }
    }


    public Task<ScreenViewEncodedFrame?> CaptureVideoAsync(
        string sourceId,
        ScreenViewCaptureProfile profile,
        int bitrate,
        bool forceKeyFrame,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (_session is null || !string.Equals(_session.SourceId, sourceId, StringComparison.Ordinal))
                {
                    EndCaptureCore();
                    _session = CreateSession(sourceId);
                }
                return Task.FromResult(_session.CaptureVideo(profile, bitrate, forceKeyFrame));
            }
            catch (ScreenViewCaptureException)
            {
                EndCaptureCore();
                throw;
            }
            catch (Exception ex) when (ex is SharpGenException or ExternalException or InvalidOperationException or NotSupportedException)
            {
                EndCaptureCore();
                throw new ScreenViewCaptureException("capture-device-lost", "The selected display is no longer available.", ex);
            }
        }
    }

    public void EndCapture()
    {
        lock (_gate) EndCaptureCore();
    }

    private static CaptureSession CreateSession(string sourceId)
    {
        OutputLocation? location = EnumerateOutputs().FirstOrDefault(item => string.Equals(item.Id, sourceId, StringComparison.Ordinal));
        if (location is null)
            throw new ScreenViewCaptureException("display-unavailable", "The selected display is no longer available.");

        IDXGIFactory1? factory = null;
        IDXGIAdapter1? adapter = null;
        IDXGIOutput? output = null;
        ID3D11Device? device = null;
        ID3D11DeviceContext? context = null;
        IDXGIOutput1? output1 = null;
        IDXGIOutputDuplication? duplication = null;
        try
        {
            factory = CreateDXGIFactory1<IDXGIFactory1>();
            EnsureSuccess(factory.EnumAdapters1((uint)location.AdapterIndex, out adapter), "display-unavailable");
            EnsureSuccess(adapter.EnumOutputs((uint)location.OutputIndex, out output), "display-unavailable");
#pragma warning disable CA2000 // CaptureSession takes ownership after creation; the catch path disposes partial results.
            EnsureSuccess(D3D11CreateDevice(
                adapter,
                DriverType.Unknown,
                DeviceCreationFlags.BgraSupport,
                [FeatureLevel.Level_11_1, FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0],
                out device,
                out context), "capture-unavailable");
#pragma warning restore CA2000
            output1 = output.QueryInterface<IDXGIOutput1>();
            duplication = output1.DuplicateOutput(device);
            var session = new CaptureSession(location, device, context, duplication);
            device = null;
            context = null;
            duplication = null;
            return session;
        }
        catch
        {
            duplication?.Dispose();
            context?.Dispose();
            device?.Dispose();
            throw;
        }
        finally
        {
            output1?.Dispose();
            output?.Dispose();
            adapter?.Dispose();
            factory?.Dispose();
        }
    }

    private static List<OutputLocation> EnumerateOutputs()
    {
        List<OutputLocation> outputs = [];
        using IDXGIFactory1 factory = CreateDXGIFactory1<IDXGIFactory1>();
#pragma warning disable CA2000 // Every successful COM enumeration result is disposed by its immediately enclosing using statement.
        for (uint adapterIndex = 0; ; adapterIndex++)
        {
            Result adapterResult = factory.EnumAdapters1(adapterIndex, out IDXGIAdapter1 adapter);
            if (adapterResult == DxgiResultCode.NotFound) break;
            EnsureSuccess(adapterResult, "capture-unavailable");
            using (adapter)
            {
                for (uint outputIndex = 0; ; outputIndex++)
                {
                    Result outputResult = adapter.EnumOutputs(outputIndex, out IDXGIOutput output);
                    if (outputResult == DxgiResultCode.NotFound) break;
                    EnsureSuccess(outputResult, "capture-unavailable");
                    using (output)
                    {
                        OutputDescription description = output.Description;
                        if (!description.AttachedToDesktop) continue;
                        int width = description.DesktopCoordinates.Right - description.DesktopCoordinates.Left;
                        int height = description.DesktopCoordinates.Bottom - description.DesktopCoordinates.Top;
                        int ordinal = outputs.Count + 1;
                        outputs.Add(new OutputLocation(
                            $"display-{adapterIndex + 1}-{outputIndex + 1}",
                            $"Display {ordinal}",
                            (int)adapterIndex,
                            (int)outputIndex,
                            description.DesktopCoordinates.Left,
                            description.DesktopCoordinates.Top,
                            width,
                            height,
                            description.DesktopCoordinates.Left == 0 && description.DesktopCoordinates.Top == 0,
                            description.Rotation));
                    }
                }
            }
        }
#pragma warning restore CA2000
        return outputs;
    }

    private void EndCaptureCore()
    {
        _session?.Dispose();
        _session = null;
    }

    private static void EnsureSuccess(Result result, string code)
    {
        if (result.Failure)
            throw new ScreenViewCaptureException(code, "Windows desktop capture is unavailable.", new SharpGenException(result));
    }

    private sealed class CaptureSession(
        OutputLocation output,
        ID3D11Device device,
        ID3D11DeviceContext context,
        IDXGIOutputDuplication duplication) : IDisposable
    {
        private readonly OutputLocation _output = output;
        private readonly ID3D11Device _device = device;
        private readonly ID3D11DeviceContext _context = context;
        private readonly IDXGIOutputDuplication _duplication = duplication;
        private bool _needsResynchronization = true;
        private CursorSnapshot? _lastCursor;
        private ScreenViewCaptureProfile? _lastProfile;
        private ScreenViewVideoEncoder? _videoEncoder;
        private D3D11DesktopFrameConverter? _gpuFrameConverter;
        private bool _videoResetPending = true;
        private bool _videoUnavailable;
        private bool _gpuFrameConversionUnavailable;
        private ScreenViewHardwareH264Encoder? _webRtcEncoder;
        private ScreenViewCaptureProfile? _webRtcProfile;
        private int _webRtcBitrate;

        public string SourceId => _output.Id;

        public ScreenViewEncodedFrame? CaptureVideo(ScreenViewCaptureProfile profile, int bitrate, bool forceKeyFrame)
        {
            if (profile.FramesPerSecond is < 1 or > 30 || bitrate is < 250_000 or > 16_000_000)
                throw new ArgumentOutOfRangeException(nameof(profile));
            bool swapsDimensions = _output.Rotation is ModeRotation.Rotate90 or ModeRotation.Rotate270;
            int rotatedWidth = swapsDimensions ? _output.Height : _output.Width;
            int rotatedHeight = swapsDimensions ? _output.Width : _output.Height;
            double scale = Math.Min(1d, Math.Min((double)profile.MaxWidth / rotatedWidth, (double)profile.MaxHeight / rotatedHeight));
            int width = Math.Max(2, (int)Math.Round(rotatedWidth * scale)) & ~1;
            int height = Math.Max(2, (int)Math.Round(rotatedHeight * scale)) & ~1;
            if (_webRtcProfile != profile || _webRtcBitrate != bitrate || forceKeyFrame)
            {
                _webRtcEncoder?.Dispose();
                _webRtcEncoder = null;
                _webRtcProfile = profile;
                _webRtcBitrate = bitrate;
            }

#pragma warning disable CA2000 // desktopResource is disposed in the finally block for every successful acquisition.
            Result acquire = _duplication.AcquireNextFrame(_webRtcEncoder is null ? 250u : 34u, out OutduplFrameInfo frameInfo, out IDXGIResource desktopResource);
#pragma warning restore CA2000
            if (acquire == DxgiResultCode.WaitTimeout)
            {
                desktopResource?.Dispose();
                return null;
            }
            if (acquire == DxgiResultCode.AccessLost || acquire == DxgiResultCode.SessionDisconnected)
            {
                desktopResource?.Dispose();
                throw new ScreenViewCaptureException("capture-device-lost", "Windows stopped providing the selected display.");
            }
            if (acquire.Failure) desktopResource?.Dispose();
            EnsureSuccess(acquire, "capture-failed");
            try
            {
                using ID3D11Texture2D desktopTexture = desktopResource!.QueryInterface<ID3D11Texture2D>();
                if (frameInfo.ProtectedContentMaskedOut)
                    throw new ScreenViewCaptureException("protected-content", "Windows protected this display content, so screen viewing was stopped.");
                ScreenViewCursorUpdate? cursor = ScaleCursor(
                    TransformCursor(ReadCursor(frameInfo), _output.Width, _output.Height, _output.Rotation),
                    scale);
                bool visualChanged = frameInfo.LastPresentTime != 0 || _webRtcEncoder is null;
                if (!visualChanged)
                    return cursor is null ? null : new ScreenViewEncodedFrame([], width, height, profile.FramesPerSecond, false, cursor);
                if (!TryRenderGpuFrame(desktopTexture, width, height, out ID3D11Texture2D? surface))
                    throw new ScreenViewCaptureException("encoder-unavailable", "This graphics adapter cannot prepare the PC display for WebRTC video.");
                _webRtcEncoder ??= new ScreenViewHardwareH264Encoder(_device, width, height, profile.FramesPerSecond, bitrate);
                ScreenViewEncodedFrame encoded = _webRtcEncoder.Encode(surface!);
                return encoded with { Cursor = cursor };
            }
            finally
            {
                desktopResource?.Dispose();
                _duplication.ReleaseFrame();
            }
        }

        public ScreenViewFrame? Capture(long sequence, ScreenViewCaptureProfile profile)
        {
            if (_lastProfile != profile)
            {
                _lastProfile = profile;
                _needsResynchronization = true;
                _lastCursor = null;
                _videoEncoder?.Dispose();
                _videoEncoder = null;
                _videoResetPending = true;
            }
#pragma warning disable CA2000 // desktopResource is disposed in the finally block for every successful acquisition.
            Result acquire = _duplication.AcquireNextFrame(0, out OutduplFrameInfo frameInfo, out IDXGIResource desktopResource);
#pragma warning restore CA2000
            if (acquire == DxgiResultCode.WaitTimeout)
            {
                desktopResource?.Dispose();
                return _needsResynchronization ? CaptureDesktop(sequence, profile, null) : null;
            }
            if (acquire == DxgiResultCode.AccessLost || acquire == DxgiResultCode.SessionDisconnected)
            {
                desktopResource?.Dispose();
                throw new ScreenViewCaptureException("capture-device-lost", "Windows stopped providing the selected display.");
            }
            if (acquire.Failure) desktopResource?.Dispose();
            EnsureSuccess(acquire, "capture-failed");

            try
            {
                using (ID3D11Texture2D desktopTexture = desktopResource!.QueryInterface<ID3D11Texture2D>())
                {
                    if (frameInfo.ProtectedContentMaskedOut)
                    {
                        throw new ScreenViewCaptureException(
                            "protected-content",
                            "Windows protected this display content, so screen viewing was stopped.");
                    }
                    List<Rectangle> dirty = ReadChangedRectangles(frameInfo, desktopTexture.Description);
                    ScreenViewCursorUpdate? cursor = ReadCursor(frameInfo);
                    bool visualChanged = ShouldCaptureVisual(_needsResynchronization, frameInfo.LastPresentTime, dirty.Count);
                    if (!visualChanged && cursor is null) return null;

                    int sourceWidth = _output.Width;
                    int sourceHeight = _output.Height;
                    if (!visualChanged)
                    {
                        bool swapsDimensions = _output.Rotation is ModeRotation.Rotate90 or ModeRotation.Rotate270;
                        int rotatedWidth = swapsDimensions ? sourceHeight : sourceWidth;
                        int rotatedHeight = swapsDimensions ? sourceWidth : sourceHeight;
                        double cursorScale = Math.Min(1d, Math.Min((double)profile.MaxWidth / rotatedWidth, (double)profile.MaxHeight / rotatedHeight));
                        cursor = TransformCursor(cursor, sourceWidth, sourceHeight, _output.Rotation);
                        return new ScreenViewFrame(
                            sequence,
                            Math.Max(1, (int)Math.Round(rotatedWidth * cursorScale)),
                            Math.Max(1, (int)Math.Round(rotatedHeight * cursorScale)),
                            false,
                            [],
                            ScaleCursor(cursor, cursorScale));
                    }

                    double captureScale = profile.HighMotion
                        ? Math.Min(1d, Math.Min((double)profile.MaxWidth / _output.Width, (double)profile.MaxHeight / _output.Height))
                        : 1d;
                    if (profile.HighMotion)
                    {
                        int videoCanvasWidth = Math.Max(2, (int)Math.Round(_output.Width * captureScale)) & ~1;
                        int videoCanvasHeight = Math.Max(2, (int)Math.Round(_output.Height * captureScale)) & ~1;
                        bool videoHighMotion = frameInfo.LastPresentTime != 0
                            && ShouldSendFull(dirty, (int)desktopTexture.Description.Width, (int)desktopTexture.Description.Height);
                        cursor = ScaleCursor(TransformCursor(cursor, sourceWidth, sourceHeight, _output.Rotation), captureScale);
                        if (TryRenderGpuFrame(desktopTexture, videoCanvasWidth, videoCanvasHeight, out ID3D11Texture2D? gpuFrame)
                            && TryEncodeGpuVideo(
                                gpuFrame!,
                                sequence,
                                videoCanvasWidth,
                                videoCanvasHeight,
                                cursor,
                                videoHighMotion,
                                profile.FramesPerSecond,
                                out ScreenViewFrame? videoFrame))
                        {
                            _needsResynchronization = false;
                            return videoFrame;
                        }

                        using Bitmap fallback = ReadDesktopBitmap(captureScale);
                        _needsResynchronization = false;
                        return EncodeVideoFallback(fallback, sequence, videoCanvasWidth, videoCanvasHeight, cursor, 1d, videoHighMotion);
                    }

                    using Bitmap source = ReadDesktopBitmap();
                    if (_output.Rotation != ModeRotation.Identity && _output.Rotation != ModeRotation.Unspecified)
                    {
                        dirty = [new Rectangle(0, 0, source.Width, source.Height)];
                        _needsResynchronization = true;
                    }

                    double scale = Math.Min(1d, Math.Min((double)profile.MaxWidth / source.Width, (double)profile.MaxHeight / source.Height));
                    int canvasWidth = Math.Max(1, (int)Math.Round(source.Width * scale));
                    int canvasHeight = Math.Max(1, (int)Math.Round(source.Height * scale));
                    bool highMotion = frameInfo.LastPresentTime != 0 && ShouldSendFull(dirty, source.Width, source.Height);
                    cursor = TransformCursor(cursor, sourceWidth, sourceHeight, _output.Rotation);
                    _videoEncoder?.Dispose();
                    _videoEncoder = null;
                    _videoResetPending = true;
                    bool resync = _needsResynchronization || frameInfo.RectsCoalesced || highMotion;
                    IReadOnlyList<ScreenViewPatch> patches = visualChanged
                        ? resync
                            ? [EncodePatch(source, new Rectangle(0, 0, source.Width, source.Height), scale, true, profile.HighMotion)]
                            : [.. Coalesce(dirty, source.Width, source.Height).Select(rect => EncodePatch(source, rect, scale, false, false))]
                        : [];
                    _needsResynchronization = false;
                    return new ScreenViewFrame(sequence, canvasWidth, canvasHeight, resync, patches, ScaleCursor(cursor, scale), highMotion);
                }
            }
            finally
            {
                desktopResource?.Dispose();
                _duplication.ReleaseFrame();
            }
        }

        private ScreenViewFrame CaptureDesktop(long sequence, ScreenViewCaptureProfile profile, ScreenViewCursorUpdate? cursor)
        {
            double captureScale = profile.HighMotion
                ? Math.Min(1d, Math.Min((double)profile.MaxWidth / _output.Width, (double)profile.MaxHeight / _output.Height))
                : 1d;
            using Bitmap source = ReadDesktopBitmap(captureScale);
            double scale = profile.HighMotion
                ? 1d
                : Math.Min(1d, Math.Min((double)profile.MaxWidth / source.Width, (double)profile.MaxHeight / source.Height));
            int canvasWidth = Math.Max(1, (int)Math.Round(source.Width * scale));
            int canvasHeight = Math.Max(1, (int)Math.Round(source.Height * scale));
            _needsResynchronization = false;
            if (profile.HighMotion)
            {
                return EncodeVideo(source, sequence, canvasWidth, canvasHeight, ScaleCursor(cursor, captureScale), scale, true, profile.FramesPerSecond);
            }
            _videoEncoder?.Dispose();
            _videoEncoder = null;
            _videoResetPending = true;
            return new ScreenViewFrame(
                sequence,
                canvasWidth,
                canvasHeight,
                true,
                [EncodePatch(source, new Rectangle(Point.Empty, source.Size), scale, true, profile.HighMotion)],
                ScaleCursor(cursor, scale));
        }

        private ScreenViewFrame EncodeVideo(
            Bitmap source,
            long sequence,
            int canvasWidth,
            int canvasHeight,
            ScreenViewCursorUpdate? cursor,
            double scale,
            bool highMotion,
            int framesPerSecond)
        {
            if (_videoUnavailable)
            {
                return EncodeVideoFallback(source, sequence, canvasWidth, canvasHeight, cursor, scale, highMotion);
            }
            int videoWidth = Math.Max(2, canvasWidth & ~1);
            int videoHeight = Math.Max(2, canvasHeight & ~1);
            try
            {
                if (_videoEncoder is null
                    || _videoEncoder.Width != videoWidth
                    || _videoEncoder.Height != videoHeight
                    || _videoEncoder.FramesPerSecond != framesPerSecond
                    || _videoEncoder.SupportsGpuSurfaces
                    || _videoEncoder.ShouldRestart)
                {
                    _videoEncoder?.Dispose();
                    int bitrate = Math.Clamp(checked(videoWidth * videoHeight * 3), 600_000, 4_000_000);
                    _videoEncoder = new ScreenViewVideoEncoder(videoWidth, videoHeight, framesPerSecond, bitrate);
                    _videoResetPending = true;
                }

                byte[] bytes = _videoEncoder.Encode(source);
                ScreenViewVideoSegment? video = null;
                bool reset = false;
                if (bytes.Length > 0)
                {
                    reset = _videoResetPending;
                    video = new ScreenViewVideoSegment(_videoEncoder.MimeType, bytes, reset);
                    _videoResetPending = false;
                }
                return new ScreenViewFrame(
                    sequence,
                    _videoEncoder.Width,
                    _videoEncoder.Height,
                    reset,
                    [],
                    ScaleCursor(cursor, scale),
                    highMotion,
                    video);
            }
            catch (Exception ex) when (ex is SharpGenException or ExternalException or InvalidOperationException)
            {
                _videoEncoder?.Dispose();
                _videoEncoder = null;
                _videoUnavailable = true;
                return EncodeVideoFallback(source, sequence, canvasWidth, canvasHeight, cursor, scale, highMotion);
            }
        }

        private bool TryEncodeGpuVideo(
            ID3D11Texture2D source,
            long sequence,
            int canvasWidth,
            int canvasHeight,
            ScreenViewCursorUpdate? cursor,
            bool highMotion,
            int framesPerSecond,
            out ScreenViewFrame? frame)
        {
            frame = null;
            if (_videoUnavailable) return false;
            try
            {
                if (_videoEncoder is null
                    || _videoEncoder.Width != canvasWidth
                    || _videoEncoder.Height != canvasHeight
                    || _videoEncoder.FramesPerSecond != framesPerSecond
                    || !_videoEncoder.SupportsGpuSurfaces
                    || _videoEncoder.ShouldRestart)
                {
                    _videoEncoder?.Dispose();
                    int bitrate = Math.Clamp(checked(canvasWidth * canvasHeight * 3), 600_000, 4_000_000);
                    _videoEncoder = new ScreenViewVideoEncoder(canvasWidth, canvasHeight, framesPerSecond, bitrate, _device);
                    _videoResetPending = true;
                }

                byte[] bytes = _videoEncoder.Encode(source);
                ScreenViewVideoSegment? video = null;
                bool reset = false;
                if (bytes.Length > 0)
                {
                    reset = _videoResetPending;
                    video = new ScreenViewVideoSegment(_videoEncoder.MimeType, bytes, reset);
                    _videoResetPending = false;
                }
                frame = new ScreenViewFrame(
                    sequence,
                    _videoEncoder.Width,
                    _videoEncoder.Height,
                    reset,
                    [],
                    cursor,
                    highMotion,
                    video);
                return true;
            }
            catch (Exception ex) when (ex is SharpGenException or ExternalException or InvalidOperationException or ArgumentException)
            {
                _videoEncoder?.Dispose();
                _videoEncoder = null;
                _videoUnavailable = true;
                return false;
            }
        }

        private static ScreenViewFrame EncodeVideoFallback(
            Bitmap source,
            long sequence,
            int canvasWidth,
            int canvasHeight,
            ScreenViewCursorUpdate? cursor,
            double scale,
            bool highMotion)
        {
            return new ScreenViewFrame(
                sequence,
                canvasWidth,
                canvasHeight,
                true,
                [EncodePatch(source, new Rectangle(Point.Empty, source.Size), scale, true, true)],
                ScaleCursor(cursor, scale),
                highMotion);
        }

        private Bitmap ReadDesktopBitmap(double scale = 1d)
        {
            int width = Math.Max(2, (int)Math.Round(_output.Width * scale)) & ~1;
            int height = Math.Max(2, (int)Math.Round(_output.Height * scale)) & ~1;
            var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            if (width != _output.Width || height != _output.Height)
            {
                IntPtr screen = ScreenViewNativeMethods.GetDC(IntPtr.Zero);
                if (screen == IntPtr.Zero)
                {
                    bitmap.Dispose();
                    throw new InvalidOperationException("The desktop device context is unavailable.");
                }
                try
                {
                    using Graphics destination = Graphics.FromImage(bitmap);
                    IntPtr target = destination.GetHdc();
                    try
                    {
                        if (ScreenViewNativeMethods.SetStretchBltMode(target, 3) == 0)
                        {
                            throw new InvalidOperationException("The scaled desktop capture mode is unavailable.");
                        }
                        if (!ScreenViewNativeMethods.StretchBlt(target, 0, 0, width, height, screen, _output.Left, _output.Top, _output.Width, _output.Height, 0x00CC0020))
                        {
                            throw new InvalidOperationException($"The scaled desktop capture failed with Windows error {Marshal.GetLastWin32Error()}.");
                        }
                    }
                    finally
                    {
                        destination.ReleaseHdc(target);
                    }
                }
                catch
                {
                    bitmap.Dispose();
                    throw;
                }
                finally
                {
                    int released = ScreenViewNativeMethods.ReleaseDC(IntPtr.Zero, screen);
                    GC.KeepAlive(released);
                }
                return bitmap;
            }
            using Graphics graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(
                _output.Left,
                _output.Top,
                0,
                0,
                bitmap.Size,
                CopyPixelOperation.SourceCopy);
            return bitmap;
        }

        private bool TryRenderGpuFrame(ID3D11Texture2D desktopTexture, int width, int height, out ID3D11Texture2D? frame)
        {
            frame = null;
            if (!_gpuFrameConversionUnavailable)
            {
                try
                {
                    _gpuFrameConverter ??= new D3D11DesktopFrameConverter(_device, _context, _output.Rotation);
                    frame = _gpuFrameConverter.RenderNv12(desktopTexture, width, height);
                    return true;
                }
                catch (Exception ex) when (ex is SharpGenException or ExternalException or InvalidOperationException or NotSupportedException)
                {
                    _gpuFrameConverter?.Dispose();
                    _gpuFrameConverter = null;
                    _gpuFrameConversionUnavailable = true;
                }
            }
            return false;
        }

        private List<Rectangle> ReadChangedRectangles(OutduplFrameInfo frameInfo, Texture2DDescription description)
        {
            List<Rectangle> rectangles = [];
            if (frameInfo.TotalMetadataBufferSize == 0)
            {
                return NormalizeChangedRectangles(
                    frameInfo.LastPresentTime,
                    (int)description.Width,
                    (int)description.Height,
                    rectangles);
            }

            _duplication.GetFrameDirtyRects(0, [], out uint dirtyBytes);
            if (dirtyBytes > 0)
            {
                RawRect[] dirty = new RawRect[dirtyBytes / (uint)Marshal.SizeOf<RawRect>()];
                EnsureSuccess(_duplication.GetFrameDirtyRects(dirtyBytes, dirty, out _), "capture-metadata-failed");
                rectangles.AddRange(dirty.Select(ToRectangle));
            }

            _duplication.GetFrameMoveRects(0, [], out uint moveBytes);
            if (moveBytes > 0)
            {
                OutduplMoveRect[] moves = new OutduplMoveRect[moveBytes / (uint)Marshal.SizeOf<OutduplMoveRect>()];
                EnsureSuccess(_duplication.GetFrameMoveRects(moveBytes, moves, out _), "capture-metadata-failed");
                rectangles.AddRange(moves.Select(move => ToRectangle(move.DestinationRect)));
            }

            return NormalizeChangedRectangles(
                frameInfo.LastPresentTime,
                (int)description.Width,
                (int)description.Height,
                rectangles);
        }

        private ScreenViewCursorUpdate? ReadCursor(OutduplFrameInfo frameInfo)
        {
            CursorSnapshot next = _lastCursor ?? new CursorSnapshot(false, 0, 0, 0, 0, 0, 0, null);
            bool shapeChanged = false;
            if (frameInfo.LastMouseUpdateTime != 0)
            {
                next = next with
                {
                    Visible = frameInfo.PointerPosition.Visible,
                    X = frameInfo.PointerPosition.Position.X,
                    Y = frameInfo.PointerPosition.Position.Y
                };
            }

            if (frameInfo.PointerShapeBufferSize > 0)
            {
                shapeChanged = true;
                IntPtr buffer = Marshal.AllocHGlobal(checked((int)frameInfo.PointerShapeBufferSize));
                try
                {
                    EnsureSuccess(_duplication.GetFramePointerShape(frameInfo.PointerShapeBufferSize, buffer, out _, out OutduplPointerShapeInfo shape), "capture-pointer-failed");
                    byte[] bytes = new byte[frameInfo.PointerShapeBufferSize];
                    Marshal.Copy(buffer, bytes, 0, bytes.Length);
                    (int width, int height, byte[] png) = EncodeCursorShape(shape, bytes);
                    next = next with
                    {
                        HotSpotX = shape.HotSpot.X,
                        HotSpotY = shape.HotSpot.Y,
                        Width = width,
                        Height = height,
                        PngBytes = png
                    };
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }

            if (_lastCursor is not null && next.ContentEquals(_lastCursor)) return null;
            _lastCursor = next;
            return CreateCursorUpdate(
                next.Visible,
                next.X,
                next.Y,
                next.HotSpotX,
                next.HotSpotY,
                next.Width,
                next.Height,
                next.PngBytes,
                shapeChanged);
        }

        private static (int Width, int Height, byte[] Png) EncodeCursorShape(OutduplPointerShapeInfo shape, byte[] bytes)
        {
            int width = checked((int)shape.Width);
            int height = checked((int)shape.Height);
            if (shape.Type == 1) height /= 2;
            using var bitmap = new Bitmap(Math.Max(1, width), Math.Max(1, height), PixelFormat.Format32bppArgb);
            BitmapData data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                byte[] pixels = new byte[checked(bitmap.Width * bitmap.Height * 4)];
                if (shape.Type == 1)
                {
                    int pitch = checked((int)shape.Pitch);
                    for (int y = 0; y < height; y++)
                        for (int x = 0; x < width; x++)
                        {
                            int mask = 0x80 >> (x & 7);
                            bool andBit = (bytes[y * pitch + (x >> 3)] & mask) != 0;
                            bool xorBit = (bytes[(y + height) * pitch + (x >> 3)] & mask) != 0;
                            int offset = (y * width + x) * 4;
                            byte value = xorBit ? (byte)255 : (byte)0;
                            pixels[offset] = value;
                            pixels[offset + 1] = value;
                            pixels[offset + 2] = value;
                            pixels[offset + 3] = andBit && !xorBit ? (byte)0 : (byte)255;
                        }
                }
                else
                {
                    int sourcePitch = checked((int)shape.Pitch);
                    for (int y = 0; y < height; y++)
                    {
                        Buffer.BlockCopy(bytes, y * sourcePitch, pixels, y * width * 4, width * 4);
                        if (shape.Type == 4)
                            for (int x = 0; x < width; x++) pixels[(y * width + x) * 4 + 3] = 255;
                    }
                }
                for (int y = 0; y < height; y++)
                    Marshal.Copy(pixels, y * width * 4, IntPtr.Add(data.Scan0, y * data.Stride), width * 4);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            return (width, height, stream.ToArray());
        }

        private static ScreenViewPatch EncodePatch(Bitmap source, Rectangle sourceRect, double scale, bool fullFrame, bool highMotion)
        {
            Rectangle bounded = Rectangle.Intersect(new Rectangle(Point.Empty, source.Size), sourceRect);
            int x = (int)Math.Floor(bounded.X * scale);
            int y = (int)Math.Floor(bounded.Y * scale);
            int right = (int)Math.Ceiling(bounded.Right * scale);
            int bottom = (int)Math.Ceiling(bounded.Bottom * scale);
            int width = Math.Max(1, right - x);
            int height = Math.Max(1, bottom - y);
            using var patch = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(patch))
            {
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.InterpolationMode = DrawingInterpolationMode.HighQualityBilinear;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.DrawImage(source, new Rectangle(0, 0, width, height), bounded, GraphicsUnit.Pixel);
            }
            bool jpeg = fullFrame || IsHighEntropy(patch);
            using var stream = new MemoryStream();
            if (jpeg) SaveJpeg(patch, stream, highMotion ? 65L : fullFrame ? 82L : 78L); else patch.Save(stream, ImageFormat.Png);
            return new ScreenViewPatch(x, y, width, height, jpeg ? "image/jpeg" : "image/png", stream.ToArray());
        }

        private static bool IsHighEntropy(Bitmap bitmap)
        {
            if ((long)bitmap.Width * bitmap.Height < 90_000) return false;
            BitmapData data = bitmap.LockBits(new Rectangle(Point.Empty, bitmap.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int samples = 0;
                int changed = 0;
                byte[] row = new byte[Math.Abs(data.Stride)];
                int stepX = Math.Max(4, bitmap.Width / 48);
                int stepY = Math.Max(4, bitmap.Height / 32);
                for (int y = 0; y < bitmap.Height; y += stepY)
                {
                    Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, row.Length);
                    int previous = -1;
                    for (int x = 0; x < bitmap.Width; x += stepX)
                    {
                        int offset = x * 4;
                        int color = row[offset] | row[offset + 1] << 8 | row[offset + 2] << 16;
                        if (previous >= 0 && ColorDistance(previous, color) > 72) changed++;
                        previous = color;
                        samples++;
                    }
                }
                return samples > 0 && changed > samples * 0.42;
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }

        private static int ColorDistance(int left, int right) =>
            Math.Abs((left & 255) - (right & 255)) +
            Math.Abs(((left >> 8) & 255) - ((right >> 8) & 255)) +
            Math.Abs(((left >> 16) & 255) - ((right >> 16) & 255));

        private static bool ShouldSendFull(List<Rectangle> rectangles, int width, int height)
        {
            if (rectangles.Count == 0) return false;
            if (rectangles.Count > MaxPatchCount) return true;
            long area = rectangles.Sum(rectangle => (long)Math.Max(0, rectangle.Width) * Math.Max(0, rectangle.Height));
            return area >= (long)width * height * 45 / 100;
        }

        private static List<Rectangle> Coalesce(IEnumerable<Rectangle> rectangles, int width, int height)
        {
            List<Rectangle> result = [];
            Rectangle bounds = new(0, 0, width, height);
            foreach (Rectangle candidate in rectangles.Select(rectangle => Rectangle.Intersect(bounds, rectangle)).Where(rectangle => !rectangle.IsEmpty))
            {
                Rectangle expanded = candidate;
                expanded.Inflate(8, 8);
                int match = result.FindIndex(existing => expanded.IntersectsWith(existing));
                if (match >= 0) result[match] = Rectangle.Union(result[match], candidate); else result.Add(candidate);
            }
            if (result.Count <= MaxPatchCount) return result;
            Rectangle union = result[0];
            for (int index = 1; index < result.Count; index++) union = Rectangle.Union(union, result[index]);
            return [union];
        }

        private static Rectangle ToRectangle(RawRect rectangle) => new(rectangle.Left, rectangle.Top, rectangle.Right - rectangle.Left, rectangle.Bottom - rectangle.Top);

        private static ScreenViewCursorUpdate? ScaleCursor(ScreenViewCursorUpdate? cursor, double scale) => cursor is null ? null : cursor with
        {
            X = (int)Math.Round(cursor.X * scale),
            Y = (int)Math.Round(cursor.Y * scale),
            HotSpotX = (int)Math.Round(cursor.HotSpotX * scale),
            HotSpotY = (int)Math.Round(cursor.HotSpotY * scale),
            Width = Math.Max(1, (int)Math.Round(cursor.Width * scale)),
            Height = Math.Max(1, (int)Math.Round(cursor.Height * scale))
        };

        private static ScreenViewCursorUpdate? TransformCursor(ScreenViewCursorUpdate? cursor, int sourceWidth, int sourceHeight, ModeRotation rotation)
        {
            if (cursor is null || rotation is ModeRotation.Identity or ModeRotation.Unspecified) return cursor;
            byte[]? shape = cursor.PngBytes is null ? null : RotateCursorPng(cursor.PngBytes, rotation);
            var screenRotation = rotation switch
            {
                ModeRotation.Rotate90 => ScreenViewRotation.Rotate90,
                ModeRotation.Rotate180 => ScreenViewRotation.Rotate180,
                ModeRotation.Rotate270 => ScreenViewRotation.Rotate270,
                _ => ScreenViewRotation.Identity
            };
            return TransformCursorGeometry(cursor, sourceWidth, sourceHeight, screenRotation) with { PngBytes = shape };
        }

        private static byte[] RotateCursorPng(byte[] png, ModeRotation rotation)
        {
            using var input = new MemoryStream(png, writable: false);
            using var bitmap = new Bitmap(input);
            Rotate(bitmap, rotation);
            using var output = new MemoryStream();
            bitmap.Save(output, ImageFormat.Png);
            return output.ToArray();
        }

        private static void Rotate(Bitmap bitmap, ModeRotation rotation)
        {
            bitmap.RotateFlip(rotation switch
            {
                ModeRotation.Rotate90 => RotateFlipType.Rotate90FlipNone,
                ModeRotation.Rotate180 => RotateFlipType.Rotate180FlipNone,
                ModeRotation.Rotate270 => RotateFlipType.Rotate270FlipNone,
                _ => RotateFlipType.RotateNoneFlipNone
            });
        }

        private static void SaveJpeg(Image image, Stream stream, long quality)
        {
            ImageCodecInfo encoder = ImageCodecInfo.GetImageEncoders().First(item => item.FormatID == ImageFormat.Jpeg.Guid);
            using var parameters = new EncoderParameters(1);
            parameters.Param[0] = new EncoderParameter(Encoder.Quality, quality);
            image.Save(stream, encoder, parameters);
        }

        public void Dispose()
        {
            _webRtcEncoder?.Dispose();
            _webRtcEncoder = null;
            _videoEncoder?.Dispose();
            _videoEncoder = null;
            _gpuFrameConverter?.Dispose();
            _gpuFrameConverter = null;
            _duplication.Dispose();
            _context.Dispose();
            _device.Dispose();
        }
    }

    internal static ScreenViewCursorUpdate CreateCursorUpdate(
        bool visible,
        int x,
        int y,
        int hotSpotX,
        int hotSpotY,
        int width,
        int height,
        byte[]? pngBytes,
        bool shapeChanged) =>
        new(visible, x, y, hotSpotX, hotSpotY, width, height, shapeChanged ? pngBytes : null);

    internal static ScreenViewCursorUpdate TransformCursorGeometry(
        ScreenViewCursorUpdate cursor,
        int sourceWidth,
        int sourceHeight,
        ScreenViewRotation rotation) => rotation switch
        {
            ScreenViewRotation.Rotate90 => cursor with
            {
                X = sourceHeight - cursor.Y - cursor.Height,
                Y = cursor.X,
                HotSpotX = cursor.Height - 1 - cursor.HotSpotY,
                HotSpotY = cursor.HotSpotX,
                Width = cursor.Height,
                Height = cursor.Width
            },
            ScreenViewRotation.Rotate180 => cursor with
            {
                X = sourceWidth - cursor.X - cursor.Width,
                Y = sourceHeight - cursor.Y - cursor.Height,
                HotSpotX = cursor.Width - 1 - cursor.HotSpotX,
                HotSpotY = cursor.Height - 1 - cursor.HotSpotY
            },
            ScreenViewRotation.Rotate270 => cursor with
            {
                X = cursor.Y,
                Y = sourceWidth - cursor.X - cursor.Width,
                HotSpotX = cursor.HotSpotY,
                HotSpotY = cursor.Width - 1 - cursor.HotSpotX,
                Width = cursor.Height,
                Height = cursor.Width
            },
            _ => cursor
        };

    private static partial class ScreenViewNativeMethods
    {
        [LibraryImport("user32.dll")]
        internal static partial IntPtr GetDC(IntPtr window);

        [LibraryImport("user32.dll")]
        internal static partial int ReleaseDC(IntPtr window, IntPtr deviceContext);

        [LibraryImport("gdi32.dll")]
        internal static partial int SetStretchBltMode(IntPtr deviceContext, int mode);

        [LibraryImport("gdi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool StretchBlt(
            IntPtr destination,
            int destinationX,
            int destinationY,
            int destinationWidth,
            int destinationHeight,
            IntPtr source,
            int sourceX,
            int sourceY,
            int sourceWidth,
            int sourceHeight,
            uint operation);
    }

    private sealed record OutputLocation(string Id, string Label, int AdapterIndex, int OutputIndex, int Left, int Top, int Width, int Height, bool IsPrimary, ModeRotation Rotation);

    private sealed record CursorSnapshot(bool Visible, int X, int Y, int HotSpotX, int HotSpotY, int Width, int Height, byte[]? PngBytes)
    {
        public bool ContentEquals(CursorSnapshot other) =>
            Visible == other.Visible && X == other.X && Y == other.Y && HotSpotX == other.HotSpotX && HotSpotY == other.HotSpotY &&
            Width == other.Width && Height == other.Height && ((PngBytes is null && other.PngBytes is null) ||
                (PngBytes is not null && other.PngBytes is not null && PngBytes.AsSpan().SequenceEqual(other.PngBytes)));
    }
}
