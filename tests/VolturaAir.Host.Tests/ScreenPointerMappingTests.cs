using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed class ScreenPointerMappingTests
{
    [Theory]
    [InlineData(ScreenViewRotation.Identity, 1024, 768, 0, 0, 0, 0)]
    [InlineData(ScreenViewRotation.Rotate90, 768, 1024, 0, 0, 0, 0)]
    [InlineData(ScreenViewRotation.Rotate180, 1024, 768, 0.25, 0.75, 256, 575)]
    [InlineData(ScreenViewRotation.Rotate270, 768, 1024, 0.25, 0.75, 192, 767)]
    [InlineData(ScreenViewRotation.Rotate90, 768, 1024, 1, 1, 767, 1023)]
    public void MapsDirectClicksToThePointsShownInTheOrientedDesktop(
        ScreenViewRotation rotation,
        int width,
        int height,
        double x,
        double y,
        int expectedX,
        int expectedY)
    {
        var source = new ScreenViewSource("display", "Display", width, height, true, -width, 40, rotation);
        var virtualDesktop = new VirtualDesktopBounds(-width, 40, width, height);

        ScreenPointerPosition mapped = ScreenViewCoordinator.MapPointer(source, virtualDesktop, x, y);

        Assert.Equal(-width + expectedX, mapped.DesktopX);
        Assert.Equal(40 + expectedY, mapped.DesktopY);
    }

    [Fact]
    public void NormalizesNegativeOriginDisplaysAcrossTheWholeVirtualDesktop()
    {
        var source = new ScreenViewSource("left", "Left", 1920, 1080, false, -1920, 0);
        var virtualDesktop = new VirtualDesktopBounds(-1920, 0, 3840, 1080);

        ScreenPointerPosition left = ScreenViewCoordinator.MapPointer(source, virtualDesktop, 0, 0);
        ScreenPointerPosition right = ScreenViewCoordinator.MapPointer(source, virtualDesktop, 1, 1);

        Assert.Equal((-1920, 0, 0, 0), (left.DesktopX, left.DesktopY, left.AbsoluteX, left.AbsoluteY));
        Assert.Equal((-1, 1079), (right.DesktopX, right.DesktopY));
        Assert.Equal(ushort.MaxValue, right.AbsoluteY);
        Assert.InRange(right.AbsoluteX, 32750, 32785);
    }
}
