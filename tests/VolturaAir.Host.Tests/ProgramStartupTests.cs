using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed class ProgramStartupTests
{
    [Fact]
    public async Task StartupReadinessWaitsForMinimumDisplayAfterFastInitialization()
    {
        var minimumDisplay = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = Program.AwaitStartupReadinessAsync(() => Task.FromResult(42), minimumDisplay.Task);

        Assert.False(completion.IsCompleted);

        minimumDisplay.SetResult();

        Assert.Equal(42, await completion.WaitAsync(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task StartupReadinessWaitsForInitializationAfterMinimumDisplay()
    {
        var initialization = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = Program.AwaitStartupReadinessAsync(() => initialization.Task, Task.CompletedTask);

        Assert.False(completion.IsCompleted);

        initialization.SetResult(42);

        Assert.Equal(42, await completion.WaitAsync(TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task StartupFailureDoesNotWaitForMinimumDisplay()
    {
        var minimumDisplay = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failure = new InvalidOperationException("startup failed");
        var completion = Program.AwaitStartupReadinessAsync(
            () => Task.FromException<int>(failure),
            minimumDisplay.Task);

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => completion);

        Assert.Same(failure, actual);
        Assert.False(minimumDisplay.Task.IsCompleted);
    }

    [Fact]
    public void OffscreenScreenshotSkipsVisibleSplashMinimum()
    {
        Assert.True(Program.CreateStartupMinimumDisplayTask(splashVisible: false).IsCompletedSuccessfully);
    }

    [Fact]
    public void MainWindowIsHiddenWhenTrayStartupPreferenceIsEnabled()
    {
        Assert.False(Program.ShouldShowMainWindowOnStartup(Array.Empty<string>(), startHiddenInTraySetting: true, hasActiveController: false));
    }

    [Fact]
    public void MainWindowIsHiddenWhenMinimizedArgumentIsProvided()
    {
        var args = new[] { "--minimized" };

        Assert.False(Program.ShouldShowMainWindowOnStartup(args, startHiddenInTraySetting: false, hasActiveController: false));
    }

    [Fact]
    public void MainWindowDoesNotShowWhenControllerIsAlreadyConnected()
    {
        Assert.False(Program.ShouldShowMainWindowOnStartup(Array.Empty<string>(), startHiddenInTraySetting: false, hasActiveController: true));
    }

    [Fact]
    public void MainWindowShowsForNormalLaunchWithoutActiveController()
    {
        Assert.True(Program.ShouldShowMainWindowOnStartup(Array.Empty<string>(), startHiddenInTraySetting: false, hasActiveController: false));
    }
}
