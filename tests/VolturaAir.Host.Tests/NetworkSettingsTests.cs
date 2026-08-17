using Microsoft.Win32;
using VolturaAir.Host;

namespace VolturaAir.Host.Tests;

[Collection(AppPermissionSettingsCollection.Name)]
public sealed class NetworkSettingsTests : IsolatedHostSettingsTest
{
    [Fact]
    public void EnhancedCapabilitiesDefaultOffAndRoundTripInOneJsonValue()
    {
        Assert.False(AppNetworkSettings.Load().EnhancedCapabilitiesEnabled);

        var settings = AppNetworkSettings.Load() with { EnhancedCapabilitiesEnabled = true };
        AppNetworkSettings.Save(settings);
        Assert.True(AppNetworkSettings.Load().EnhancedCapabilitiesEnabled);
        using var key = Registry.CurrentUser.OpenSubKey(HostSettingsRegistry.SettingsKeyPath, writable: false);
        string json = Assert.IsType<string>(key?.GetValue(AppNetworkSettings.ValueName));
        Assert.Contains("\"enhancedCapabilitiesEnabled\":true", json, StringComparison.Ordinal);
        Assert.Null(key?.GetValue("EnhancedCapabilitiesEnabled"));

        AppNetworkSettings.Save(settings with { EnhancedCapabilitiesEnabled = false });
        Assert.False(AppNetworkSettings.Load().EnhancedCapabilitiesEnabled);
    }

    [Fact]
    public void MalformedOrLegacyNetworkValuesUseCurrentDefaults()
    {
        using var key = Registry.CurrentUser.CreateSubKey(HostSettingsRegistry.SettingsKeyPath, writable: true);
        key.SetValue("EnhancedCapabilitiesEnabled", 1, RegistryValueKind.DWord);
        key.SetValue(AppNetworkSettings.ValueName, "{\"enhancedCapabilitiesEnabled\":true}", RegistryValueKind.String);

        Assert.False(AppNetworkSettings.Load().EnhancedCapabilitiesEnabled);
    }

    [Fact]
    public void FailedAtomicNetworkWritePreservesTheCompletePreviousValue()
    {
        var original = AppNetworkSettings.Load() with { EnhancedCapabilitiesEnabled = true };
        AppNetworkSettings.Save(original);
        HostSettingsJsonValue.BeforeWriteForTests = (_, _) => throw new IOException("injected write failure");
        try
        {
            Assert.Throws<IOException>(() => AppNetworkSettings.Save(original with
            {
                EnhancedCapabilitiesEnabled = false,
                TransportMode = ConnectionTransportMode.Relay
            }));
        }
        finally
        {
            HostSettingsJsonValue.BeforeWriteForTests = null;
        }
        Assert.Equal(original, AppNetworkSettings.Load());
    }
}
