using System.Collections.Concurrent;
using System.Diagnostics;
using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed class AwakeServiceTests
{
    [Fact]
    public async Task AppliesEveryExecutionStateOnOneDedicatedThread()
    {
        var bridge = new RecordingBridge();
        var saved = new List<AwakeState>();
        await using var service = new AwakeService(
            bridge,
            new AwakeState(AwakeMode.Off, false, 60, null),
            saved.Add);

        Assert.True((await service.SetIndefiniteAsync()).Succeeded);
        Assert.True((await service.SetKeepScreenOnAsync(true)).Succeeded);
        Assert.True((await service.SetOffAsync()).Succeeded);

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
        await using var service = new AwakeService(
            bridge,
            new AwakeState(AwakeMode.Off, false, 60, null),
            _ => { });

        Assert.True((await service.SetExpirationAsync(DateTimeOffset.Now.AddMilliseconds(100))).Succeeded);
        await WaitForAsync(() => service.State.Mode == AwakeMode.Off);

        Assert.Equal(AwakeExecutionState.Continuous, bridge.States[^1]);
    }

    [Fact]
    public async Task NativeFailureDoesNotPublishRequestedState()
    {
        var bridge = new RecordingBridge { Succeeds = false };
        await using var service = new AwakeService(
            bridge,
            new AwakeState(AwakeMode.Off, false, 60, null),
            _ => { });

        var result = await service.SetIndefiniteAsync();

        Assert.False(result.Succeeded);
        Assert.Equal(AwakeMode.Off, service.State.Mode);
    }

    [Fact]
    public async Task ExpiredPersistedModeStartsOff()
    {
        var bridge = new RecordingBridge();
        await using var service = new AwakeService(
            bridge,
            new AwakeState(AwakeMode.Timed, true, 30, DateTimeOffset.Now.AddMinutes(-1)),
            _ => { });

        Assert.Equal(AwakeMode.Off, service.State.Mode);
        Assert.Empty(bridge.States);
    }

    [Fact]
    public async Task TimedOutNativeCallDoesNotPublishAndRestoresCommittedStateWhenItReturns()
    {
        var bridge = new BlockingBridge();
        await using var service = new AwakeService(
            bridge,
            new AwakeState(AwakeMode.Off, false, 60, null),
            _ => { },
            operationTimeout: TimeSpan.FromMilliseconds(50));

        var operation = service.SetIndefiniteAsync();
        Assert.True(bridge.Entered.Wait(TimeSpan.FromSeconds(1)));

        var result = await operation;

        Assert.False(result.Succeeded);
        Assert.Equal(AwakeOperationFailure.TimedOut, result.Failure);
        Assert.Equal(AwakeMode.Off, service.State.Mode);

        bridge.Release.Set();
        await WaitForAsync(() => bridge.States.Count >= 2);

        Assert.Equal(AwakeExecutionState.SystemRequired | AwakeExecutionState.Continuous, bridge.States[0]);
        Assert.Equal(AwakeExecutionState.Continuous, bridge.States[1]);
        Assert.Equal(AwakeMode.Off, service.State.Mode);
    }

    [Fact]
    public async Task SuccessfulOperationCompletesAfterStateAndPersistenceAreCommitted()
    {
        var bridge = new BlockingBridge();
        Task<AwakeOperationResult>? operation = null;
        var completionObservedDuringSave = false;
        await using var service = new AwakeService(
            bridge,
            new AwakeState(AwakeMode.Off, false, 60, null),
            _ => completionObservedDuringSave = operation?.IsCompleted == true);

        operation = service.SetIndefiniteAsync();
        Assert.True(bridge.Entered.Wait(TimeSpan.FromSeconds(1)));
        bridge.Release.Set();

        var result = await operation;

        Assert.True(result.Succeeded);
        Assert.False(completionObservedDuringSave);
        Assert.Equal(AwakeMode.Indefinite, service.State.Mode);
    }

    [Fact]
    public async Task ThrowingStateChangedSubscriberDoesNotStopWorkerOrOtherSubscribers()
    {
        var notifications = 0;
        await using var service = new AwakeService(
            new RecordingBridge(),
            new AwakeState(AwakeMode.Off, false, 60, null),
            _ => { });
        service.StateChanged += (_, _) => throw new InvalidOperationException("Expected subscriber failure.");
        service.StateChanged += (_, _) => notifications += 1;

        Assert.True((await service.SetIndefiniteAsync()).Succeeded);
        Assert.True((await service.SetOffAsync()).Succeeded);

        Assert.Equal(2, notifications);
        Assert.Equal(AwakeMode.Off, service.State.Mode);
    }

    [Fact]
    public async Task CancelledQueuedRequestIsSkipped()
    {
        var bridge = new BlockingBridge();
        await using var service = new AwakeService(
            bridge,
            new AwakeState(AwakeMode.Off, false, 60, null),
            _ => { });

        var first = service.SetIndefiniteAsync();
        Assert.True(bridge.Entered.Wait(TimeSpan.FromSeconds(1)));
        using var cancellation = new CancellationTokenSource();
        var cancelled = service.SetKeepScreenOnAsync(true, cancellation.Token);
        await cancellation.CancelAsync();

        var cancelledResult = await cancelled;
        bridge.Release.Set();
        Assert.True((await first).Succeeded);
        await Task.Delay(20);

        Assert.Equal(AwakeOperationFailure.Cancelled, cancelledResult.Failure);
        Assert.DoesNotContain(
            AwakeExecutionState.SystemRequired | AwakeExecutionState.DisplayRequired | AwakeExecutionState.Continuous,
            bridge.States);
    }

    [Fact]
    public async Task BoundedQueueRejectsExcessRequestsWithoutBlocking()
    {
        var bridge = new BlockingBridge();
        var service = new AwakeService(
            bridge,
            new AwakeState(AwakeMode.Off, false, 60, null),
            _ => { },
            operationTimeout: TimeSpan.FromSeconds(2),
            shutdownTimeout: TimeSpan.FromMilliseconds(50));
        try
        {
            var first = service.SetIndefiniteAsync();
            Assert.True(bridge.Entered.Wait(TimeSpan.FromSeconds(1)));
            var queued = Enumerable.Range(0, 8)
                .Select(index => service.SetKeepScreenOnAsync(index % 2 == 0))
                .ToArray();

            var excess = await service.SetOffAsync();

            Assert.Equal(AwakeOperationFailure.Busy, excess.Failure);
            bridge.Release.Set();
            Assert.True((await first).Succeeded);
            Assert.All(await Task.WhenAll(queued), result => Assert.True(result.Succeeded));
        }
        finally
        {
            bridge.Release.Set();
            await service.DisposeAsync();
        }
    }

    [Fact]
    public async Task DisposalReturnsWithinItsBoundWhenNativeCallIsBlocked()
    {
        var bridge = new BlockingBridge();
        var service = new AwakeService(
            bridge,
            new AwakeState(AwakeMode.Off, false, 60, null),
            _ => { },
            operationTimeout: TimeSpan.FromSeconds(2),
            shutdownTimeout: TimeSpan.FromMilliseconds(50));
        var operation = service.SetIndefiniteAsync();
        Assert.True(bridge.Entered.Wait(TimeSpan.FromSeconds(1)));

        var stopwatch = Stopwatch.StartNew();
        await service.DisposeAsync();
        stopwatch.Stop();

        Assert.InRange(stopwatch.Elapsed, TimeSpan.Zero, TimeSpan.FromSeconds(1));
        Assert.Equal(AwakeOperationFailure.ShuttingDown, (await operation).Failure);

        bridge.Release.Set();
        await WaitForAsync(() => bridge.States.Count >= 2);
        Assert.Equal(AwakeExecutionState.Continuous, bridge.States[^1]);
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

    private sealed class BlockingBridge : IAwakeExecutionStateBridge
    {
        private readonly object _gate = new();
        private int _calls;

        public ManualResetEventSlim Entered { get; } = new();

        public ManualResetEventSlim Release { get; } = new();

        public List<AwakeExecutionState> States { get; } = [];

        public bool TrySet(AwakeExecutionState state)
        {
            lock (_gate)
            {
                States.Add(state);
            }

            if (Interlocked.Increment(ref _calls) == 1)
            {
                Entered.Set();
                Release.Wait();
            }

            return true;
        }
    }
}
