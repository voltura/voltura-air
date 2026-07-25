using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed class PowerPointAutomationServiceTests
{
    [Fact]
    public async Task SlowStartedOperationRemainsPendingUntilItsAuthoritativeResult()
    {
        var operation = new TaskCompletionSource<int>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var slowWasReported = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var awaited = PowerPointAutomationService.AwaitAuthoritativeOperationAsync(
            operation.Task,
            TimeSpan.FromMilliseconds(1),
            TimeSpan.FromSeconds(1),
            () => slowWasReported.SetResult());

        await slowWasReported.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.False(awaited.IsCompleted);

        operation.SetResult(42);

        var result = await awaited;
        Assert.True(result.Completed);
        Assert.Equal(42, result.Result);
    }

    [Fact]
    public async Task StartedOperationStopsHoldingTheResponseAfterItsBound()
    {
        var operation = new TaskCompletionSource<int>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var slowReports = 0;

        var result = await PowerPointAutomationService.AwaitAuthoritativeOperationAsync(
            operation.Task,
            TimeSpan.FromMilliseconds(1),
            TimeSpan.FromMilliseconds(10),
            () => slowReports++);

        Assert.False(result.Completed);
        Assert.Equal(1, slowReports);
        Assert.False(operation.Task.IsCompleted);
    }

    [Fact]
    public async Task PointerRestoreWaitsForLateMutationBeforeRunningOnce()
    {
        var latePointerEnable = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var restoreCount = 0;

        var restore = PowerPointAutomationService.QueueAfterOperationAsync(
            latePointerEnable.Task,
            () =>
            {
                restoreCount++;
                return Task.FromResult("auto-arrow");
            });

        Assert.False(restore.IsCompleted);
        Assert.Equal(0, restoreCount);

        latePointerEnable.SetResult();

        Assert.Equal("auto-arrow", await restore);
        Assert.Equal(1, restoreCount);
    }

    [Fact]
    public async Task PointerRestoreStillRunsAfterLateMutationFails()
    {
        var latePointerEnable = Task.FromException(
            new InvalidOperationException("COM failure."));
        var restored = false;

        var result = await PowerPointAutomationService.QueueAfterOperationAsync(
            latePointerEnable,
            () =>
            {
                restored = true;
                return Task.FromResult(7);
            });

        Assert.True(restored);
        Assert.Equal(7, result);
    }
}
