namespace VolturaAir.Host.Tests;

using System.Drawing;

public sealed class DxgiScreenViewCaptureSourceTests
{
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

    [Fact]
    public void PositionOnlyCursorUpdateOmitsThePreviouslySentShape()
    {
        byte[] shape = [1, 2, 3];

        ScreenViewCursorUpdate positionOnly = DxgiScreenViewCaptureSource.CreateCursorUpdate(
            true, 100, 200, 2, 3, 16, 16, shape, shapeChanged: false);
        ScreenViewCursorUpdate changedShape = DxgiScreenViewCaptureSource.CreateCursorUpdate(
            true, 100, 200, 2, 3, 16, 16, shape, shapeChanged: true);

        Assert.Null(positionOnly.PngBytes);
        Assert.Same(shape, changedShape.PngBytes);
    }

}
