using VolturaAir.Host;
using System.Globalization;

namespace VolturaAir.Host.Tests;

public sealed class PortSelectorTests
{
    [Fact]
    public void AutomaticModeReusesPersistedLastPortWhenAvailable()
    {
        var settings = AutomaticSettings() with { LastAutomaticPort = 51410 };

        var result = PortSelector.Select(settings, port => port == 51410, () => 60000);

        Assert.True(result.Succeeded);
        Assert.True(result.IsAutomatic);
        Assert.Equal(51410, result.Port);
        Assert.Null(result.Warning);
    }

    [Fact]
    public void AutomaticModeScansUpwardWhenPreferredPortIsOccupied()
    {
        var settings = AutomaticSettings();

        var result = PortSelector.Select(settings, port => port == PortSelector.PreferredPort + 1, () => 60000);

        Assert.True(result.Succeeded);
        Assert.Equal(PortSelector.PreferredPort + 1, result.Port);
        Assert.Contains(PortSelector.PreferredPort.ToString(CultureInfo.InvariantCulture), result.Warning);
        Assert.Contains((PortSelector.PreferredPort + 1).ToString(CultureInfo.InvariantCulture), result.Warning);
    }

    [Fact]
    public void AutomaticModeFallsBackToOsAssignedPortAfterRangeIsExhausted()
    {
        var settings = AutomaticSettings();

        var result = PortSelector.Select(settings, port => port < 0, () => 60000);

        Assert.True(result.Succeeded);
        Assert.True(result.IsAutomatic);
        Assert.Equal(60000, result.Port);
        Assert.Contains("Preferred Voltura Air ports", result.Warning);
    }

    [Fact]
    public void ManualModeDoesNotFallBackWhenPortIsOccupied()
    {
        var settings = AutomaticSettings() with
        {
            PortMode = PortSelectionMode.Manual,
            ManualPort = 51395
        };

        var result = PortSelector.Select(settings, port => port < 0, () => 60000);

        Assert.False(result.Succeeded);
        Assert.False(result.IsAutomatic);
        Assert.Equal(0, result.Port);
        Assert.Contains("already in use", result.ErrorMessage);
    }

    [Fact]
    public void ManualModeRejectsPrivilegedPorts()
    {
        var settings = AutomaticSettings() with
        {
            PortMode = PortSelectionMode.Manual,
            ManualPort = 80
        };

        var result = PortSelector.Select(settings, _ => true, () => 60000);

        Assert.False(result.Succeeded);
        Assert.Contains("49152", result.ErrorMessage);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(49151)]
    [InlineData(65536)]
    public void ManualModeReportsDynamicPrivatePortRangeForOutOfRangePorts(int port)
    {
        var settings = AutomaticSettings() with
        {
            PortMode = PortSelectionMode.Manual,
            ManualPort = port
        };

        var result = PortSelector.Select(settings, _ => true, () => 60000);

        Assert.False(result.Succeeded);
        Assert.Equal("Custom port must be between 49152 and 65535.", result.ErrorMessage);
    }

    [Fact]
    public void ManualModeRejectsCommonRegisteredPorts()
    {
        foreach (var reservedPort in new[] { 8080, 5985, 8888, 27017 })
        {
            var settings = AutomaticSettings() with
            {
                PortMode = PortSelectionMode.Manual,
                ManualPort = reservedPort
            };

            var result = PortSelector.Select(settings, _ => true, () => 60000);

            Assert.False(result.Succeeded);
            Assert.NotNull(result.ErrorMessage);
        }
    }

    [Fact]
    public void ManualModeAllowsNonReservedHighPorts()
    {
        var settings = AutomaticSettings() with
        {
            PortMode = PortSelectionMode.Manual,
            ManualPort = 51395
        };

        var result = PortSelector.Select(settings, _ => true, () => 60000);

        Assert.True(result.Succeeded);
        Assert.Equal(51395, result.Port);
    }

    private static NetworkSettingsSnapshot AutomaticSettings()
    {
        return new NetworkSettingsSnapshot(
            NetworkSelectionMode.Automatic,
            ManualHostAddress: null,
            ManualAdapterId: null,
            ManualAdapterName: null,
            PortSelectionMode.Automatic,
            ManualPort: null,
            LastAutomaticPort: null,
            LastAutomaticHostAddress: null);
    }
}
