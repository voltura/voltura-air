using System.Windows;
using WpfSize = System.Windows.Size;

namespace VolturaAir.Host.Tests;

public sealed class WindowWorkAreaPlacementTests
{
    [Fact]
    public void OversizedWindowIsConstrainedToWorkArea()
    {
        var bounds = WindowWorkAreaPlacement.CalculateBounds(
            new Rect(0, 0, 800, 600),
            new WpfSize(1160, 760));

        Assert.Equal(new Rect(0, 0, 800, 600), bounds);
    }

    [Fact]
    public void SmallerWindowRetainsItsRequestedSize()
    {
        var bounds = WindowWorkAreaPlacement.CalculateBounds(
            new Rect(0, 0, 1600, 900),
            new WpfSize(1160, 760));

        Assert.Equal(new WpfSize(1160, 760), bounds.Size);
    }

    [Fact]
    public void SmallerWindowIsCenteredWithinWorkArea()
    {
        var bounds = WindowWorkAreaPlacement.CalculateBounds(
            new Rect(100, 50, 1600, 900),
            new WpfSize(1160, 760));

        Assert.Equal(new Rect(320, 120, 1160, 760), bounds);
    }

    [Fact]
    public void NegativeWorkAreaOriginIsPreservedWhenCentering()
    {
        var bounds = WindowWorkAreaPlacement.CalculateBounds(
            new Rect(-1920, -100, 1920, 1080),
            new WpfSize(1160, 760));

        Assert.Equal(new Rect(-1540, 60, 1160, 760), bounds);
    }

    [Fact]
    public void VisibleWindowPositionIsUnchanged()
    {
        var position = WindowWorkAreaPlacement.CalculateVisibleTopLeft(
            new Rect(200, 100, 1160, 760),
            new Rect(0, 0, 1920, 1040));

        Assert.Equal(new Point(200, 100), position);
    }

    [Fact]
    public void WindowOutsideRightAndBottomEdgesIsMovedIntoWorkArea()
    {
        var position = WindowWorkAreaPlacement.CalculateVisibleTopLeft(
            new Rect(1000, 500, 1160, 760),
            new Rect(0, 0, 1920, 1040));

        Assert.Equal(new Point(760, 280), position);
    }

    [Fact]
    public void WindowOutsideNegativeOriginWorkAreaIsMovedIntoWorkArea()
    {
        var position = WindowWorkAreaPlacement.CalculateVisibleTopLeft(
            new Rect(-2100, -300, 1160, 760),
            new Rect(-1920, -100, 1920, 1040));

        Assert.Equal(new Point(-1920, -100), position);
    }

    [Fact]
    public void WindowLargerThanWorkAreaKeepsItsControlsReachable()
    {
        var position = WindowWorkAreaPlacement.CalculateVisibleTopLeft(
            new Rect(600, 400, 2610, 1710),
            new Rect(0, 0, 1920, 1040));

        Assert.Equal(new Point(0, 0), position);
    }
}
