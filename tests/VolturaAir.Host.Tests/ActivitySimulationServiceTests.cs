using System.Collections.Concurrent;
using System.Threading.Channels;
using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed class ActivitySimulationServiceTests
{
    [Fact]
    public async Task DisabledServiceDoesNotStartDelayOrPulse()
    {
        var sender = new RecordingPulseSender();
        var delay = new ControlledDelay();
        await using var service = new ActivitySimulationService(sender, enabled: false, save: _ => { }, delay: delay.WaitAsync);

        Assert.False(service.Enabled);
        Assert.Equal(0, delay.WaitCount);
        Assert.Equal(0, sender.CallCount);
    }

    [Fact]
    public async Task PersistedEnabledStateWaitsFullIntervalBeforeFirstPulse()
    {
        var sender = new RecordingPulseSender();
        var delay = new ControlledDelay();
        await using var service = new ActivitySimulationService(sender, enabled: true, save: _ => { }, delay: delay.WaitAsync);

        Assert.True(service.Enabled);
        Assert.Equal(ActivitySimulationService.PulseInterval, await delay.NextIntervalAsync());
        Assert.Equal(0, sender.CallCount);

        delay.Release();
        await WaitUntilAsync(() => sender.CallCount == 1);
        Assert.Equal(ActivitySimulationService.PulseInterval, await delay.NextIntervalAsync());
    }

    [Fact]
    public async Task PersistenceFailureKeepsPreviousStateAndDoesNotStartLoop()
    {
        var sender = new RecordingPulseSender();
        var delay = new ControlledDelay();
        await using var service = new ActivitySimulationService(
            sender,
            enabled: false,
            save: _ => throw new IOException("registry unavailable"),
            delay: delay.WaitAsync);

        var result = await service.SetEnabledAsync(true);

        Assert.False(result.Succeeded);
        Assert.Contains("registry unavailable", result.Error, StringComparison.Ordinal);
        Assert.False(service.Enabled);
        Assert.Equal(0, delay.WaitCount);
        Assert.Equal(0, sender.CallCount);
    }

    [Fact]
    public async Task DisablingCancelsWaitAndPreventsLaterPulse()
    {
        var sender = new RecordingPulseSender();
        var delay = new ControlledDelay();
        var saved = new List<bool>();
        await using var service = new ActivitySimulationService(sender, enabled: true, save: saved.Add, delay: delay.WaitAsync);
        _ = await delay.NextIntervalAsync();

        var result = await service.SetEnabledAsync(false);
        delay.Release();

        Assert.True(result.Succeeded);
        Assert.False(service.Enabled);
        Assert.Equal([false], saved);
        Assert.Equal(0, sender.CallCount);
    }

    [Fact]
    public async Task DisablingWaitsForActivePulseAndDoesNotRearm()
    {
        var sender = new BlockingPulseSender();
        var delay = new ControlledDelay();
        await using var service = new ActivitySimulationService(sender, enabled: true, save: _ => { }, delay: delay.WaitAsync);
        _ = await delay.NextIntervalAsync();
        delay.Release();
        await sender.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var disabling = service.SetEnabledAsync(false);
        Assert.False(disabling.IsCompleted);
        sender.Release.TrySetResult();

        Assert.True((await disabling.WaitAsync(TimeSpan.FromSeconds(2))).Succeeded);
        Assert.False(service.Enabled);
        Assert.Equal(1, sender.CallCount);
        Assert.Equal(1, delay.WaitCount);
    }

    [Fact]
    public async Task BusySkipsAreSilentAndFailureStreakLogsOnlyTransitions()
    {
        var sender = new SequencedPulseSender(
            () => ActivityPulseDispatchResult.Busy,
            () => throw new InputDispatchException("rejected", "activity.simulation", 1, 0, 5),
            () => throw new InputDispatchException("rejected", "activity.simulation", 1, 0, 5),
            () => ActivityPulseDispatchResult.Sent);
        var delay = new ControlledDelay();
        var log = new RecordingAppLogWriter();
        var warnings = 0;
        await using var service = new ActivitySimulationService(sender, enabled: true, save: _ => { }, appLog: log, delay: delay.WaitAsync);
        service.FailureStreakStarted += (_, _) => Interlocked.Increment(ref warnings);

        for (var expectedCalls = 1; expectedCalls <= 4; expectedCalls++)
        {
            _ = await delay.NextIntervalAsync();
            delay.Release();
            await WaitUntilAsync(() => sender.CallCount == expectedCalls);
        }

        await WaitUntilAsync(() => log.Entries.Count == 2);

        Assert.Equal(1, warnings);
        var entries = log.Entries.ToArray();
        Assert.Equal(["failed", "recovered"], entries.Select(entry => entry.Outcome));
        Assert.Equal(5, entries[0].Win32Error);
    }

    [Fact]
    public async Task ThrowingStateSubscriberDoesNotChangeCommittedResultOrBlockOtherSubscribers()
    {
        var sender = new RecordingPulseSender();
        var delay = new ControlledDelay();
        await using var service = new ActivitySimulationService(sender, enabled: false, save: _ => { }, delay: delay.WaitAsync);
        var notified = 0;
        service.StateChanged += (_, _) => throw new InvalidOperationException("observer failed");
        service.StateChanged += (_, _) => notified++;

        var result = await service.SetEnabledAsync(true);

        Assert.True(result.Succeeded);
        Assert.True(service.Enabled);
        Assert.Equal(1, notified);
    }

    [Fact]
    public async Task ThrowingFailureSubscriberDoesNotStopRetriesOrOtherSubscribers()
    {
        var sender = new SequencedPulseSender(
            () => throw new InputDispatchException("rejected", "activity.simulation", 1, 0, 5),
            () => ActivityPulseDispatchResult.Sent);
        var delay = new ControlledDelay();
        var notified = 0;
        await using var service = new ActivitySimulationService(sender, enabled: true, save: _ => { }, delay: delay.WaitAsync);
        service.FailureStreakStarted += (_, _) => throw new InvalidOperationException("observer failed");
        service.FailureStreakStarted += (_, _) => notified++;

        _ = await delay.NextIntervalAsync();
        delay.Release();
        await WaitUntilAsync(() => sender.CallCount == 1);
        _ = await delay.NextIntervalAsync();
        delay.Release();
        await WaitUntilAsync(() => sender.CallCount == 2);

        Assert.Equal(1, notified);
        Assert.True(service.Enabled);
    }

    [Fact]
    public async Task StateSubscriberCanSynchronouslyApplyAnotherChangeWithoutDeadlock()
    {
        var sender = new RecordingPulseSender();
        var delay = new ControlledDelay();
        await using var service = new ActivitySimulationService(sender, enabled: false, save: _ => { }, delay: delay.WaitAsync);
        var reentered = 0;
        ActivitySimulationOperationResult? nestedResult = null;
        service.StateChanged += (_, _) =>
        {
            if (Interlocked.Exchange(ref reentered, 1) == 0)
            {
                nestedResult = service.SetEnabledAsync(false).GetAwaiter().GetResult();
            }
        };

        var result = await service.SetEnabledAsync(true).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(result.Succeeded);
        Assert.True(nestedResult?.Succeeded);
        Assert.False(service.Enabled);
    }

    [Fact]
    public async Task BlockedStateSubscriberDoesNotHoldUpdateGate()
    {
        var sender = new RecordingPulseSender();
        var delay = new ControlledDelay();
        await using var service = new ActivitySimulationService(sender, enabled: false, save: _ => { }, delay: delay.WaitAsync);
        var firstObserverEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstObserver = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var notificationCount = 0;
        service.StateChanged += (_, _) =>
        {
            if (Interlocked.Increment(ref notificationCount) == 1)
            {
                firstObserverEntered.TrySetResult();
                releaseFirstObserver.Task.GetAwaiter().GetResult();
            }
        };

        var enabling = Task.Run(() => service.SetEnabledAsync(true));
        await firstObserverEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var disabling = service.SetEnabledAsync(false);

        Assert.True((await disabling.WaitAsync(TimeSpan.FromSeconds(2))).Succeeded);
        releaseFirstObserver.TrySetResult();
        Assert.True((await enabling.WaitAsync(TimeSpan.FromSeconds(2))).Succeeded);
        Assert.False(service.Enabled);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition())
        {
            await Task.Delay(10, cancellation.Token);
        }
    }

    private sealed class ControlledDelay
    {
        private readonly Channel<bool> _releases = Channel.CreateUnbounded<bool>();
        private readonly Channel<TimeSpan> _intervals = Channel.CreateUnbounded<TimeSpan>();
        private int _waitCount;

        public int WaitCount => Volatile.Read(ref _waitCount);

        public async Task WaitAsync(TimeSpan interval, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _waitCount);
            await _intervals.Writer.WriteAsync(interval, cancellationToken);
            _ = await _releases.Reader.ReadAsync(cancellationToken);
        }

        public void Release() => Assert.True(_releases.Writer.TryWrite(true));

        public async Task<TimeSpan> NextIntervalAsync() =>
            await _intervals.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
    }

    private sealed class RecordingPulseSender : IActivityPulseSender
    {
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);

        public ActivityPulseDispatchResult TrySendActivityPulse()
        {
            Interlocked.Increment(ref _callCount);
            return ActivityPulseDispatchResult.Sent;
        }
    }

    private sealed class BlockingPulseSender : IActivityPulseSender
    {
        private int _callCount;

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int CallCount => Volatile.Read(ref _callCount);

        public ActivityPulseDispatchResult TrySendActivityPulse()
        {
            Interlocked.Increment(ref _callCount);
            Entered.TrySetResult();
            Release.Task.GetAwaiter().GetResult();
            return ActivityPulseDispatchResult.Sent;
        }
    }

    private sealed class SequencedPulseSender(params Func<ActivityPulseDispatchResult>[] outcomes) : IActivityPulseSender
    {
        private readonly ConcurrentQueue<Func<ActivityPulseDispatchResult>> _outcomes = new(outcomes);
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);

        public ActivityPulseDispatchResult TrySendActivityPulse()
        {
            Interlocked.Increment(ref _callCount);
            Assert.True(_outcomes.TryDequeue(out var outcome));
            return outcome();
        }
    }

    private sealed class RecordingAppLogWriter : IAppLogWriter
    {
        public ConcurrentQueue<AppLogEntry> Entries { get; } = new();

        public void Write(AppLogEntry entry) => Entries.Enqueue(entry);
    }
}
