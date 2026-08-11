using Microsoft.Win32;
using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

[Collection(AppPermissionSettingsCollection.Name)]
public sealed class NetworkSettingsTests : IsolatedHostSettingsTest
{
    [Fact]
    public void EnhancedCapabilitiesDefaultOffAndRoundTripAsDword()
    {
        Assert.False(AppNetworkSettings.Load().EnhancedCapabilitiesEnabled);

        var settings = AppNetworkSettings.Load() with { EnhancedCapabilitiesEnabled = true };
        AppNetworkSettings.Save(settings);
        Assert.True(AppNetworkSettings.Load().EnhancedCapabilitiesEnabled);
        using var key = Registry.CurrentUser.OpenSubKey(HostSettingsRegistry.SettingsKeyPath, writable: false);
        Assert.Equal(1, key?.GetValue("EnhancedCapabilitiesEnabled"));

        AppNetworkSettings.Save(settings with { EnhancedCapabilitiesEnabled = false });
        Assert.False(AppNetworkSettings.Load().EnhancedCapabilitiesEnabled);
    }

    [Theory]
    [InlineData("true", RegistryValueKind.String)]
    [InlineData(2, RegistryValueKind.DWord)]
    [InlineData(-1, RegistryValueKind.DWord)]
    public void InvalidEnhancedCapabilitiesValuesNormalizeOff(object value, RegistryValueKind kind)
    {
        using var key = Registry.CurrentUser.CreateSubKey(HostSettingsRegistry.SettingsKeyPath, writable: true);
        key.SetValue("EnhancedCapabilitiesEnabled", value, kind);

        Assert.False(AppNetworkSettings.Load().EnhancedCapabilitiesEnabled);
    }
}
