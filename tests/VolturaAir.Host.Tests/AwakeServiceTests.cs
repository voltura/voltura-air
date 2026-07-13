using System.Collections.Concurrent;
using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed class AwakeServiceTests
{
    [Fact]
    public void AppliesEveryExecutionStateOnOneDedicatedThread()
    {
        var bridge = new RecordingBridge();
        var saved = new List<AwakeState>();
        using var service = new AwakeService(
            bridge,
            new AwakeState(AwakeMode.Off, false, 60, null),
            saved.Add);

        Assert.True(service.SetIndefinite().Succeeded);
        Assert.True(service.SetKeepScreenOn(true).Succeeded);
        Assert.True(service.SetOff().Succeeded);

        Assert.Equal(AwakeMode.Off, service.State.Mode);
        Assert.True(service.State.KeepScreenOn);
        Assert.Equal(3, saved.Count);
        Assert.Single(bridge.ThreadIds.Distinct());
        Assert.Contains(AwakeExecutionState.SystemRequired | AwakeExecutionState.Continuous, bridge.States);
        Assert.Contains(AwakeExecutionState.SystemRequired | AwakeExecutionState.DisplayRequired | AwakeExecutionState.Continuous, bridge.States);
        Assert.Equal(AwakeExecutionState.Continuous, bridge.States[^1]);
    }

    [Fact]
    public async Task ExpirationReturnsToSelectedPowerPlan()
    {
        var bridge = new RecordingBridge();
        using var service = new AwakeService(
            bridge,
            new AwakeState(AwakeMode.Off, false, 60, null),
            _ => { });

        Assert.True(service.SetExpiration(DateTimeOffset.Now.AddMilliseconds(100)).Succeeded);
        await WaitForAsync(() => service.State.Mode == AwakeMode.Off);

        Assert.Equal(AwakeExecutionState.Continuous, bridge.States[^1]);
    }

    [Fact]
    public void NativeFailureDoesNotPublishRequestedState()
    {
        var bridge = new RecordingBridge { Succeeds = false };
        using var service = new AwakeService(
            bridge,
            new AwakeState(AwakeMode.Off, false, 60, null),
            _ => { });

        var result = service.SetIndefinite();

        Assert.False(result.Succeeded);
        Assert.Equal(AwakeMode.Off, service.State.Mode);
    }

    [Fact]
    public void ExpiredPersistedModeStartsOff()
    {
        var bridge = new RecordingBridge();
        using var service = new AwakeService(
            bridge,
            new AwakeState(AwakeMode.Timed, true, 30, DateTimeOffset.Now.AddMinutes(-1)),
            _ => { });

        Assert.Equal(AwakeMode.Off, service.State.Mode);
        Assert.Empty(bridge.States);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!condition())
        {
            await Task.Delay(20, timeout.Token);
        }
    }

    private sealed class RecordingBridge : IAwakeExecutionStateBridge
    {
        private readonly object _gate = new();

        public bool Succeeds { get; set; } = true;

        public List<AwakeExecutionState> States { get; } = [];

        public ConcurrentBag<int> ThreadIds { get; } = [];

        public bool TrySet(AwakeExecutionState state)
        {
            ThreadIds.Add(Environment.CurrentManagedThreadId);
            lock (_gate)
            {
                States.Add(state);
            }

            return Succeeds;
        }
    }
}
