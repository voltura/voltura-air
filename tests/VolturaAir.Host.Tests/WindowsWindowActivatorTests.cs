using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed class WindowsWindowActivatorTests
{
    [Fact]
    public void ForegroundVerificationRequiresARealRequestedAndForegroundWindow()
    {
        Assert.False(WindowsWindowActivator.IsRequestedForegroundWindow(IntPtr.Zero, new(42)));
        Assert.False(WindowsWindowActivator.IsRequestedForegroundWindow(new(42), IntPtr.Zero));
        Assert.True(WindowsWindowActivator.IsRequestedForegroundWindow(new(42), new(42)));
    }
}
