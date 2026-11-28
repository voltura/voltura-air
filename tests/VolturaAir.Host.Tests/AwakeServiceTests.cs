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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectedExpirationRetriesOnlyForItsCommittedSetting(bool replaceSetting)
    {
        var bridge = new RecordingBridge();
        var clock = new ManualAwakeClock();
        await using var service = new AwakeService(
            bridge,
            new AwakeState(AwakeMode.Off, false, 60, null),
            _ => { },
            clock: clock);
        Assert.True((await service.SetTimedAsync(TimeSpan.FromMinutes(1))).Succeeded);
        var first = clock.Latest;
        var deadline = service.State.ExpiresAt;

        bridge.Succeeds = false;
        clock.Advance(TimeSpan.FromMinutes(1));
        first.InvokeCallback();
        await WaitForAsync(() => !ReferenceEquals(first, clock.Latest));
        Assert.Equal(AwakeMode.Timed, service.State.Mode);
        var retry = clock.Latest;
        Assert.InRange(retry.Due, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(10));

        bridge.Succeeds = true;
        if (replaceSetting)
        {
            Assert.True((await service.SetKeepScreenOnAsync(true)).Succeeded);
            Assert.Equal(deadline, service.State.ExpiresAt);
            Assert.True(retry.Disposed);
            retry.InvokeCallback();
            Assert.True((await service.SetKeepScreenOnAsync(true)).Succeeded);
            Assert.Equal(AwakeMode.Timed, service.State.Mode);
        }

        clock.Advance(retry.Due);
        clock.Latest.InvokeCallback();
        await WaitForAsync(() => service.State.Mode == AwakeMode.Off);
        Assert.Equal(replaceSetting, service.State.KeepScreenOn);
        Assert.Equal(AwakeExecutionState.Continuous, bridge.States[^1]);
    }

    [Fact]
    public async Task OneYearTimerRearmsUntilTheRealDeadline()
    {
        var bridge = new RecordingBridge();
        var clock = new ManualAwakeClock();
        await using var service = new AwakeService(
            bridge,
            new AwakeState(AwakeMode.Off, false, 60, null),
            _ => { },
            clock: clock);
        var interval = TimeSpan.FromDays(365);
        var expectedDeadline = clock.GetUtcNow() + interval;

        Assert.True((await service.SetTimedAsync(interval).WaitAsync(TimeSpan.FromSeconds(2))).Succeeded);
        Assert.Equal(expectedDeadline, service.State.ExpiresAt);
        var first = clock.Latest;
        Assert.InRange(first.Due, TimeSpan.FromDays(1), TimeSpan.FromDays(49));

        clock.Advance(TimeSpan.FromDays(100));
        first.InvokeCallback();
        Assert.Equal(AwakeMode.Timed, service.State.Mode);
        Assert.True(first.Disposed);
        Assert.NotSame(first, clock.Latest);

        clock.Advance(TimeSpan.FromDays(265));
        clock.Latest.InvokeCallback();
        await WaitForAsync(() => service.State.Mode == AwakeMode.Off);
        Assert.Equal(AwakeExecutionState.Continuous, bridge.States[^1]);
    }

    [Fact]
    public async Task CallbackAlreadyQueuedFromAnOldTimerCannotTurnOffNewSetting()
    {
        var clock = new ManualAwakeClock();
        await using var service = new AwakeService(
            new RecordingBridge(),
            new AwakeState(AwakeMode.Off, false, 60, null),
            _ => { },
            clock: clock);
        Assert.True((await service.SetTimedAsync(TimeSpan.FromMinutes(1))).Succeeded);
        var retiring = clock.Latest;

        Assert.True((await service.SetTimedAsync(TimeSpan.FromHours(1))).Succeeded);
        var currentDeadline = service.State.ExpiresAt;
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.True(retiring.Disposed);
        retiring.InvokeCallback();
        Assert.True((await service.SetKeepScreenOnAsync(true)).Succeeded);

        Assert.Equal(AwakeMode.Timed, service.State.Mode);
        Assert.Equal(currentDeadline, service.State.ExpiresAt);
        Assert.True(service.State.KeepScreenOn);
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
    public async Task PersistedLongDeadlineStartsWithTheSameExpiration()
    {
        var deadline = DateTimeOffset.UtcNow.AddMonths(6);
        await using var service = new AwakeService(
            new RecordingBridge(),
            new AwakeState(AwakeMode.Expiration, false, 60, deadline),
            _ => { });

        Assert.Equal(AwakeMode.Expiration, service.State.Mode);
        Assert.Equal(deadline, service.State.ExpiresAt);
        Assert.True((await service.SetOffAsync()).Succeeded);
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

    private sealed class ManualAwakeClock : TimeProvider
    {
        private readonly ConcurrentQueue<ScheduledExpiration> _timers = new();
        private long _ticks = new DateTime(2026, 11, 28, 12, 0, 0, DateTimeKind.Utc).Ticks;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);

        internal ScheduledExpiration Latest => _timers.Last();

        internal void Advance(TimeSpan duration) => Interlocked.Add(ref _ticks, duration.Ticks);

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Assert.Equal(Timeout.InfiniteTimeSpan, period);
            var timer = new ScheduledExpiration(callback, state, dueTime);
            _timers.Enqueue(timer);
            return timer;
        }

        internal sealed class ScheduledExpiration(TimerCallback callback, object? state, TimeSpan due) : ITimer
        {
            internal TimeSpan Due { get; } = due;
            internal bool Disposed { get; private set; }
            internal void InvokeCallback() => callback(state);
            public bool Change(TimeSpan dueTime, TimeSpan period) => throw new NotSupportedException();
            public void Dispose() => Disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
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
