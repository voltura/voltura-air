namespace VolturaAir.Host.Tests;

using System.Drawing;
using SharpGen.Runtime;

public sealed class DxgiScreenViewCaptureSourceTests
{
    [Theory]
    [InlineData(0x887A0026u, true)] // Access lost on desktop/display mode transition
    [InlineData(0x887A0005u, true)] // Device removed
    [InlineData(0x887A0007u, true)] // Device reset
    [InlineData(0x887A0004u, true)] // Mode temporarily unsupported
    [InlineData(0x887A0022u, true)] // Duplication temporarily unavailable
    [InlineData(0x887A0028u, false)] // Session disconnected
    [InlineData(0x80070005u, false)] // Access denied
    [InlineData(0x887A0001u, false)] // Invalid call
    public void OnlyRecoverableNativeTransitionsAllowCaptureRecreation(uint result, bool expected)
    {
        Assert.Equal(expected, DxgiScreenViewCaptureSource.IsRecoverableCaptureResult(unchecked((int)result)));
    }

    [Fact]
    public void GpuConversionDeviceResetAllowsRecoveryButUnsupportedFormatsStop()
    {
        var deviceReset = new SharpGenException(new Result(unchecked((int)0x887A0007u)));
        ScreenViewCaptureException recoverable = DxgiScreenViewCaptureSource.CreateVideoConversionFailure(deviceReset);
        Assert.Equal("capture-device-lost", recoverable.Code);
        Assert.Same(deviceReset, recoverable.InnerException);
        Assert.True(recoverable.CanRetryCapture);

        var unsupported = new NotSupportedException();
        ScreenViewCaptureException permanent = DxgiScreenViewCaptureSource.CreateVideoConversionFailure(unsupported);
        Assert.Equal("encoder-unavailable", permanent.Code);
        Assert.Same(unsupported, permanent.InnerException);
        Assert.False(permanent.CanRetryCapture);
    }

    [Fact]
    public void ScreenshotEncodingStreamStopsOnCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        using var stream = new DxgiScreenViewCaptureSource.BoundedScreenshotStream(64, cancellation.Token);
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => stream.Write([1, 2, 3]));
        Assert.Equal(0, stream.Length);
    }

    [Fact]
    public void ScreenshotEncodingStreamRejectsTheFirstByteBeyondItsBound()
    {
        using var stream = new DxgiScreenViewCaptureSource.BoundedScreenshotStream(3, CancellationToken.None);

        stream.Write([1, 2, 3]);
        Assert.Throws<IOException>(() => stream.WriteByte(4));
        Assert.True(stream.LimitExceeded);
        Assert.Equal(3, stream.Length);
    }

    [Theory]
    [InlineData(true, 0, 0, false)]
    [InlineData(true, 1, 0, true)]
    [InlineData(false, 0, 1, true)]
    [InlineData(false, 1, 0, false)]
    public void CursorOnlyUpdatesCannotSatisfyARequiredDesktopResynchronization(
        bool needsResynchronization,
        long lastPresentTime,
        int dirtyRectangleCount,
        bool expected)
    {
        Assert.Equal(
            expected,
            DxgiScreenViewCaptureSource.ShouldCaptureVisual(
                needsResynchronization,
                lastPresentTime,
                dirtyRectangleCount));
    }

    [Fact]
    public void DesktopPresentationFallsBackToFullFrameWhenMetadataHasNoUsableRectangles()
    {
        List<Rectangle> rectangles = DxgiScreenViewCaptureSource.NormalizeChangedRectangles(
            lastPresentTime: 1,
            width: 1920,
            height: 1080,
            [Rectangle.Empty, new Rectangle(2000, 1200, 20, 20)]);

        Assert.Equal([new Rectangle(0, 0, 1920, 1080)], rectangles);
    }

    [Fact]
    public void DesktopPresentationKeepsUsableChangedRectangles()
    {
        List<Rectangle> rectangles = DxgiScreenViewCaptureSource.NormalizeChangedRectangles(
            lastPresentTime: 1,
            width: 1920,
            height: 1080,
            [new Rectangle(10, 20, 30, 40)]);

        Assert.Equal([new Rectangle(10, 20, 30, 40)], rectangles);
    }

    [Fact]
    public void CursorOnlyFrameDoesNotInventAVisualUpdate()
    {
        List<Rectangle> rectangles = DxgiScreenViewCaptureSource.NormalizeChangedRectangles(
            lastPresentTime: 0,
            width: 1920,
            height: 1080,
            []);

        Assert.Empty(rectangles);
    }

    [Theory]
    [InlineData(768, 1024, 768, 1024, 768, 1024)]
    [InlineData(1080, 1920, 720, 720, 404, 720)]
    [InlineData(2561, 1441, 2561, 1441, 2560, 1440)]
    public void VideoFitsTheOrientedDesktopBoundsAndEncoderAlignment(
        int desktopWidth, int desktopHeight, int capWidth, int capHeight, int width, int height)
    {
        Assert.Equal(new Size(width, height), DxgiScreenViewCaptureSource.FitVideoSize(
            desktopWidth, desktopHeight, new ScreenViewCaptureProfile(capWidth, capHeight)));
    }

    [Fact]
    public void PortraitCursorKeepsItsOutputLocalGeometryAndOriginalPng()
    {
        byte[] shape = [1, 2, 3];
        var cursor = new ScreenViewCursorUpdate(true, -4, 200, 3, 7, 16, 24, shape);
        var projection = new ScreenViewCursorProjection(768, 1024);
        Assert.Null(projection.Update(cursor, cursor));
        ScreenViewCursorUpdate initial = projection.Update(null, cursor, new Size(768, 1024))!;
        Assert.Equal(cursor, initial);
        Assert.Same(shape, initial.PngBytes);
        ScreenViewCursorUpdate moved = projection.Update(cursor with { Y = 240, PngBytes = null }, cursor with { Y = 240 })!;
        Assert.Equal((-4, 240, 16, 24), (moved.X, moved.Y, moved.Width, moved.Height));
        Assert.Null(moved.PngBytes);
    }

    [Fact]
    public void AStationaryCursorIsRescaledOnlyWhenEncodedDimensionsChange()
    {
        var cursor = new ScreenViewCursorUpdate(true, 304, 514, 6, 8, 16, 24, [1, 2, 3]);
        var projection = new ScreenViewCursorProjection(768, 1024);
        Assert.NotNull(projection.Update(cursor, cursor, new Size(768, 1024)));
        Assert.Null(projection.Update(null, cursor));
        ScreenViewCursorUpdate movedBeforeResize = projection.Update(cursor with { X = 300, PngBytes = null }, cursor)!;
        Assert.Equal(300, movedBeforeResize.X);

        ScreenViewCursorUpdate resized = projection.Update(null, cursor, new Size(384, 510))!;
        Assert.Equal((152, 256, 3, 4, 8, 12), (resized.X, resized.Y, resized.HotSpotX, resized.HotSpotY, resized.Width, resized.Height));
        Assert.Null(resized.PngBytes);
        Assert.Null(projection.Update(null, cursor, new Size(384, 510)));
    }
}
