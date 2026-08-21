using Microsoft.Win32;

namespace VolturaAir.Host.Tests;

[Collection(AppPermissionSettingsCollection.Name)]
public sealed class ScreenViewSettingsTests : IsolatedHostSettingsTest
{
    [Fact]
    public void MissingSettingsDefaultToAutomaticAndChoicesPersist()
    {
        Assert.Equal(DirectScreenQualityMode.Automatic, AppScreenViewSettings.Load().DirectQuality);

        AppScreenViewSettings.Save(new ScreenViewSettingsSnapshot(DirectScreenQualityMode.Quality));

        Assert.Equal(DirectScreenQualityMode.Quality, AppScreenViewSettings.Load().DirectQuality);
    }

    [Fact]
    public void MalformedSettingRecoversOnlyToScreenViewDefault()
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(HostSettingsRegistry.SettingsKeyPath, true);
        key.SetValue(AppScreenViewSettings.ValueName, "{\"directQuality\":999}", RegistryValueKind.String);

        Assert.Equal(AppScreenViewSettings.Default, AppScreenViewSettings.Load());
    }
}
