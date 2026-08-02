using Microsoft.Win32;

namespace VolturaAir.Host.Tests;

public sealed class ScreenViewSessionLifecycleTests
{
    [Theory]
    [InlineData(SessionSwitchReason.SessionLock, true)]
    [InlineData(SessionSwitchReason.SessionLogoff, true)]
    [InlineData(SessionSwitchReason.ConsoleDisconnect, true)]
    [InlineData(SessionSwitchReason.RemoteDisconnect, true)]
    [InlineData(SessionSwitchReason.SessionUnlock, false)]
    [InlineData(SessionSwitchReason.ConsoleConnect, false)]
    public void SessionTransitionsHaveExplicitScreenCapturePolicy(SessionSwitchReason reason, bool stops)
    {
        Assert.Equal(stops, WpfHostRuntime.StopsScreenViewForSessionSwitch(reason));
    }
}
