using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed class CustomScreenHostActionsTests
{
    [Theory]
    [InlineData("power.sleep", SystemSuspendActions.Sleep)]
    [InlineData("power.hibernate", SystemSuspendActions.Hibernate)]
    public void SuspendActionsAreUnavailableWhenWindowsDoesNotExposeTheState(
        string hostAction,
        string suspendAction)
    {
        var suspend = new FakeSuspendController(available: false);

        var available = CustomScreenHostActions.IsAvailable(
            hostAction,
            new NoOpSystemPowerController(),
            new FakeWorkstationLockPolicy(),
            suspend);
        var result = CustomScreenHostActions.Execute(
            hostAction,
            new NoOpSystemPowerController(),
            suspend);

        Assert.False(available);
        Assert.False(result.Succeeded);
        Assert.Equal([suspendAction, suspendAction], suspend.AvailabilityChecks);
        Assert.Empty(suspend.ExecutedActions);
    }

    [Theory]
    [InlineData("power.sleep", SystemSuspendActions.Sleep)]
    [InlineData("power.hibernate", SystemSuspendActions.Hibernate)]
    public void AvailableSuspendActionsExecuteThroughTheCapabilityOwner(
        string hostAction,
        string suspendAction)
    {
        var suspend = new FakeSuspendController(available: true);

        Assert.True(CustomScreenHostActions.IsAvailable(
            hostAction,
            new NoOpSystemPowerController(),
            new FakeWorkstationLockPolicy(),
            suspend));
        Assert.True(CustomScreenHostActions.Execute(
            hostAction,
            new NoOpSystemPowerController(),
            suspend).Succeeded);
        Assert.Equal([suspendAction], suspend.ExecutedActions);
    }

    private sealed class FakeSuspendController(bool available) : ISystemSuspendController
    {
        public List<string> AvailabilityChecks { get; } = [];

        public List<string> ExecutedActions { get; } = [];

        public bool IsAvailable(string action)
        {
            AvailabilityChecks.Add(action);
            return available;
        }

        public SystemPowerExecutionResult TryExecute(string action)
        {
            ExecutedActions.Add(action);
            return SystemPowerExecutionResult.Success;
        }
    }

    private sealed class FakeWorkstationLockPolicy : IWorkstationLockPolicy
    {
        public event EventHandler? Changed
        {
            add { }
            remove { }
        }

        public WorkstationLockPolicyStatus GetStatus() =>
            new(WorkstationLockPolicyState.NotExplicitlyDisabled);

        public WorkstationLockEnableResult TryEnable() =>
            new(true, "Windows locking is enabled for this user.");
    }
}
