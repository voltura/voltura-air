using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

public sealed class ProgramStartupTests
{
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
