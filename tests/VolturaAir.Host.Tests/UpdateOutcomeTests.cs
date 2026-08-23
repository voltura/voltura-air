using VolturaAir.Host.Features.Updates;

namespace VolturaAir.Host.Tests;

public sealed class UpdateOutcomeTests
{
    [Fact]
    public void StartupOutcomeArgumentsAreRecognizedAndReplacedOnceOnRelaunch()
    {
        Assert.Equal(UpdateStartupOutcome.Updated, UpdateService.GetStartupOutcome(["--updated"]));
        Assert.Equal(UpdateStartupOutcome.Failed, UpdateService.GetStartupOutcome(["--update-failed"]));
        Assert.Equal(UpdateStartupOutcome.None, UpdateService.GetStartupOutcome([]));

        Assert.Equal(
            ["--minimized", "--update-failed"],
            UpdateProcessLauncher.BuildRestartArguments(
                ["--updated", "--minimized", "--update-failed"],
                "--update-failed"));
        Assert.Equal(
            ["--minimized"],
            UpdateProcessLauncher.BuildRestartArguments(["--updated", "--minimized"], null));
    }

    [Fact]
    public void NullOrThrowingInstallerStartRelaunchesCurrentHost()
    {
        var relaunches = 0;

        Assert.False(UpdateProcessLauncher.TryLaunchInstaller(
            static () => null,
            () => relaunches++));
        Assert.False(UpdateProcessLauncher.TryLaunchInstaller(
            static () => throw new InvalidOperationException("start failed"),
            () => relaunches++));

        Assert.Equal(2, relaunches);
    }
}
