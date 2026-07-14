using System.Windows.Threading;
using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed class SystemPowerControllerTests
{
    [Fact]
    public async Task InactiveBlackoutCheckDoesNotEnterWpfDispatcher()
    {
        var controller = new WindowsDisplayActionController(Dispatcher.CurrentDispatcher, NullAppLog.Instance);

        var dismissed = await Task.Run(controller.DismissBlackoutIfActive).WaitAsync(TimeSpan.FromSeconds(1));

        Assert.False(dismissed);
    }

    [Fact]
    public void LockReturnsAcceptedWhenNativeApiReturnsTrue()
    {
        var controller = new SystemPowerController(() => true, () => true, () => 123);

        var result = controller.TryExecute(SystemPowerActions.Lock);

        Assert.True(result.Succeeded);
        Assert.Null(result.Win32Error);
    }

    [Fact]
    public void LockReturnsWin32ErrorWhenNativeApiReturnsFalse()
    {
        var controller = new SystemPowerController(() => false, () => true, () => 5);

        var result = controller.TryExecute(SystemPowerActions.Lock);

        Assert.False(result.Succeeded);
        Assert.Equal(5, result.Win32Error);
    }

    [Fact]
    public void DisplayOffReturnsAcceptedWhenWindowsAcceptsMonitorPowerMessage()
    {
        var controller = new SystemPowerController(() => true, () => true, () => 0);

        var result = controller.TryExecute(SystemPowerActions.DisplayOff);

        Assert.True(result.Succeeded);
        Assert.Null(result.Win32Error);
    }

    [Fact]
    public void DisplayOffReturnsWin32ErrorWhenWindowsRejectsMonitorPowerMessage()
    {
        var controller = new SystemPowerController(() => true, () => false, () => 5);

        var result = controller.TryExecute(SystemPowerActions.DisplayOff);

        Assert.False(result.Succeeded);
        Assert.Equal(5, result.Win32Error);
    }

    [Fact]
    public void DisplayActionsDelegateBlackoutScreenSaverAvailabilityAndDismissal()
    {
        var displayActions = new FakeWindowsDisplayActionController { IsScreenSaverAvailable = false, BlackoutActive = true };
        using var controller = new SystemPowerController(() => true, () => true, () => 0, displayActions);

        var blackout = controller.TryExecute(SystemPowerActions.BlackoutDisplay);
        var dismissed = controller.DismissBlackoutIfActive();

        Assert.True(blackout.Succeeded);
        Assert.True(dismissed);
        Assert.False(controller.IsActionAvailable(SystemPowerActions.ScreenSaver));
        Assert.Equal(1, displayActions.BlackoutCalls);
        Assert.Equal(1, displayActions.DismissCalls);
    }

    private sealed class FakeWindowsDisplayActionController : IWindowsDisplayActionController
    {
        public bool IsScreenSaverAvailable { get; set; }

        public bool BlackoutActive { get; set; }

        public int BlackoutCalls { get; private set; }

        public int DismissCalls { get; private set; }

        public SystemPowerExecutionResult TryShowBlackout()
        {
            BlackoutCalls += 1;
            BlackoutActive = true;
            return SystemPowerExecutionResult.Success;
        }

        public SystemPowerExecutionResult TryStartScreenSaver() => SystemPowerExecutionResult.Success;

        public bool DismissBlackoutIfActive()
        {
            if (!BlackoutActive)
            {
                return false;
            }

            DismissCalls += 1;
            BlackoutActive = false;
            return true;
        }

        public void Dispose()
        {
        }
    }
}
