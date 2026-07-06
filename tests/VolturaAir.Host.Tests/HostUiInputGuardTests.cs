using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed class HostUiInputGuardTests
{
    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(8, true)]
    [InlineData(9, true)]
    [InlineData(20, true)]
    public void WindowCommandHitTestAllowsCaptionButtonsOnly(int hitTest, bool expected)
    {
        Assert.Equal(expected, HostUiInputGuard.IsWindowCommandHitTest(hitTest));
    }
}
