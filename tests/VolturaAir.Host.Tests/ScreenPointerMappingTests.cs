using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed class ScreenPointerMappingTests
{
    [Theory]
    [InlineData(ScreenViewRotation.Identity, 0, 0, 0, 0)]
    [InlineData(ScreenViewRotation.Rotate90, 0, 0, 0, 1023)]
    [InlineData(ScreenViewRotation.Rotate180, 0, 0, 767, 1023)]
    [InlineData(ScreenViewRotation.Rotate270, 0, 0, 767, 0)]
    [InlineData(ScreenViewRotation.Rotate90, 1, 1, 767, 0)]
    public void InvertsTheCaptureRotationBeforeMappingToDesktop(
        ScreenViewRotation rotation,
        double x,
        double y,
        int expectedX,
        int expectedY)
    {
        var source = new ScreenViewSource("display", "Display", 768, 1024, true, -768, 40, rotation);
        var virtualDesktop = new VirtualDesktopBounds(-768, 40, 768, 1024);

        ScreenPointerPosition mapped = ScreenViewCoordinator.MapPointer(source, virtualDesktop, x, y);

        Assert.Equal(-768 + expectedX, mapped.DesktopX);
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
