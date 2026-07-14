using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed class PointerHighlightForegroundMonitorTests
{
    [Theory]
    [InlineData(true, 0x2000u, 0x2000u, false)]
    [InlineData(true, 0x2000u, 0x3000u, true)]
    [InlineData(true, 0x3000u, 0x2000u, false)]
    [InlineData(false, 0x2000u, 0u, true)]
    public void SuppressesOverlayOnlyWhenForegroundIntegrityIsUnknownOrHigher(
        bool integrityLevelKnown,
        uint hostIntegrityLevel,
        uint foregroundIntegrityLevel,
        bool expected)
    {
        Assert.Equal(
            expected,
            PointerHighlightForegroundMonitor.ShouldSuppressOverlay(
                integrityLevelKnown,
                hostIntegrityLevel,
                foregroundIntegrityLevel));
    }

    [Theory]
    [InlineData(20, 20, true)]
    [InlineData(10, 20, true)]
    [InlineData(110, 20, false)]
    [InlineData(20, 60, false)]
    public void TaskbarBoundsContainOnlyTheirVisibleArea(int x, int y, bool expected)
    {
        var bounds = new PointerHighlightService.NativeRect
        {
            Left = 10,
            Top = 20,
            Right = 110,
            Bottom = 60
        };
        var point = new PointerHighlightService.Point { X = x, Y = y };

        Assert.Equal(expected, PointerHighlightService.IsPointInsideBounds(point, bounds));
    }
}
