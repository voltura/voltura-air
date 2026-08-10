using Microsoft.Win32;
using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

[Collection(AppPermissionSettingsCollection.Name)]
public sealed class AppActivitySimulationSettingsTests : IsolatedHostSettingsTest
{
    [Fact]
    public void MissingSettingDefaultsOffAndRoundTrips()
    {
        Assert.False(AppActivitySimulationSettings.Load());

        AppActivitySimulationSettings.Save(true);
        Assert.True(AppActivitySimulationSettings.Load());

        AppActivitySimulationSettings.Save(false);
        Assert.False(AppActivitySimulationSettings.Load());
    }

    [Fact]
    public void MalformedSettingDefaultsOff()
    {
        using var key = Registry.CurrentUser.CreateSubKey(HostSettingsRegistry.SettingsKeyPath, writable: true);
        key.SetValue("SimulateActivityEnabled", "true", RegistryValueKind.String);

        Assert.False(AppActivitySimulationSettings.Load());

        key.SetValue("SimulateActivityEnabled", 2, RegistryValueKind.DWord);
        Assert.False(AppActivitySimulationSettings.Load());
    }

    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    [InlineData(typeof(System.Security.SecurityException))]
    public void ReadFailureDefaultsOff(Type exceptionType)
    {
        var exception = (Exception)Activator.CreateInstance(exceptionType)!;

        Assert.False(AppActivitySimulationSettings.Load(() => throw exception));
    }
}
