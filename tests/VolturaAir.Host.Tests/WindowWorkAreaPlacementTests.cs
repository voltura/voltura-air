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
}
