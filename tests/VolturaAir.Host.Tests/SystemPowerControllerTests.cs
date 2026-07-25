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

    [Theory]
    [InlineData(100, 200, 100, 200, false)]
    [InlineData(100, 200, 101, 200, true)]
    [InlineData(100, 200, 100, 201, true)]
    public void OverlayIgnoresSyntheticMouseMoveUntilPhysicalCursorPositionChanges(
        int initialX,
        int initialY,
        int currentX,
        int currentY,
        bool expected)
    {
        Assert.Equal(
            expected,
            WindowsDisplayActionController.HasPointerMoved(
                (initialX, initialY),
                (currentX, currentY)));
    }

    [Fact]
    public void OverlayDoesNotTreatUnavailableCursorPositionAsMovement()
    {
        Assert.False(WindowsDisplayActionController.HasPointerMoved(null, (100, 200)));
        Assert.False(WindowsDisplayActionController.HasPointerMoved((100, 200), null));
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
        var displayActions = new FakeWindowsDisplayActionController { IsScreenSaverAvailable = false };
        using var controller = new SystemPowerController(() => true, () => true, () => 0, displayActions);

        var blackout = controller.TryExecute(SystemPowerActions.BlackoutDisplay);
        var dismissed = controller.DismissBlackoutIfActive();

        Assert.True(blackout.Succeeded);
        Assert.True(dismissed);
        Assert.False(controller.IsActionAvailable(SystemPowerActions.ScreenSaver));
        Assert.Equal(1, displayActions.BlackoutCalls);
        Assert.Equal(1, displayActions.DismissCalls);
    }

    [Fact]
    public void BlackoutActionTogglesAnActiveBlackoutOff()
    {
        var displayActions = new FakeWindowsDisplayActionController();
        using var controller = new SystemPowerController(() => true, () => true, () => 0, displayActions);

        Assert.True(controller.TryExecute(SystemPowerActions.BlackoutDisplay).Succeeded);
        Assert.True(controller.TryExecute(SystemPowerActions.BlackoutDisplay).Succeeded);
        Assert.False(displayActions.BlackoutActive);
        Assert.Equal(1, displayActions.BlackoutCalls);
        Assert.Equal(1, displayActions.DismissCalls);
    }

    [Fact]
    public void PresentationBlankOverlayDelegatesBlackAndWhiteToTheSharedDisplayOwner()
    {
        var displayActions = new FakeWindowsDisplayActionController();
        using var controller = new SystemPowerController(
            () => true,
            () => true,
            () => 0,
            displayActions);
        var presentationBlank = (IPresentationBlankOverlay)controller;
        var stateChanges = 0;
        presentationBlank.StateChanged += (_, _) => stateChanges += 1;

        Assert.True(presentationBlank.TryShowPresentationBlank("presentation-a", white: false).Succeeded);
        Assert.Equal("black", presentationBlank.Snapshot?.SlideShowState);
        Assert.True(presentationBlank.DismissPresentationBlankIfActive());
        Assert.Null(presentationBlank.Snapshot);
        Assert.True(presentationBlank.TryShowPresentationBlank("presentation-a", white: true).Succeeded);
        displayActions.SimulateInputDismiss();

        Assert.Equal(1, displayActions.BlackoutCalls);
        Assert.Equal(1, displayActions.WhiteoutCalls);
        Assert.False(displayActions.BlackoutActive);
        Assert.Null(presentationBlank.Snapshot);
        Assert.Equal(4, stateChanges);
    }

    private sealed class FakeWindowsDisplayActionController : IWindowsDisplayActionController
    {
        public event EventHandler? BlankOverlayChanged;

        public bool IsScreenSaverAvailable { get; set; }

        public bool BlackoutActive { get; set; }

        public bool IsBlankOverlayActive => BlackoutActive;

        public int BlackoutCalls { get; private set; }

        public int WhiteoutCalls { get; private set; }

        public int DismissCalls { get; private set; }

        public void SimulateInputDismiss()
        {
            BlackoutActive = false;
            BlankOverlayChanged?.Invoke(this, EventArgs.Empty);
        }

        public SystemPowerExecutionResult TryShowBlackout()
        {
            if (BlackoutActive)
            {
                DismissCalls += 1;
                BlackoutActive = false;
                BlankOverlayChanged?.Invoke(this, EventArgs.Empty);
                return SystemPowerExecutionResult.Success;
            }

            BlackoutCalls += 1;
            BlackoutActive = true;
            BlankOverlayChanged?.Invoke(this, EventArgs.Empty);
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
            BlankOverlayChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        public SystemPowerExecutionResult TryShowWhiteout()
        {
            if (BlackoutActive)
            {
                DismissCalls += 1;
                BlackoutActive = false;
                BlankOverlayChanged?.Invoke(this, EventArgs.Empty);
                return SystemPowerExecutionResult.Success;
            }

            WhiteoutCalls += 1;
            BlackoutActive = true;
            BlankOverlayChanged?.Invoke(this, EventArgs.Empty);
            return SystemPowerExecutionResult.Success;
        }

        public SystemPowerExecutionResult TryShowPresentationBreak(Func<TimeSpan> getElapsed)
        {
            BlackoutCalls += 1;
            BlackoutActive = true;
            BlankOverlayChanged?.Invoke(this, EventArgs.Empty);
            return SystemPowerExecutionResult.Success;
        }

        public bool DismissPresentationBreakIfActive() => DismissBlackoutIfActive();

        public void Dispose()
        {
        }
    }
}
