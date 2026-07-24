using Microsoft.Win32;

namespace VolturaAir.Host.Tests;

[Collection(AppPermissionSettingsCollection.Name)]
public sealed class HostSettingsRegistryTests : IsolatedHostSettingsTest
{
    [Fact]
    public void AlphaFeaturesAreEnabledByDefaultAndCanBeExplicitlyDisabled()
    {
        Assert.True(AppDeveloperSettings.EnableAlphaFeatures());

        AppDeveloperSettings.SetEnableAlphaFeatures(false);
        Assert.False(AppDeveloperSettings.EnableAlphaFeatures());
    }

    [Fact]
    public void ActiveIsolatedScopeRefreshesCachedHotPathSettings()
    {
        AppClientControlSettings.SetEnabled(false);
        AppLoggingSettings.SetEnabled(true);
        AppDeveloperSettings.SetEnableAlphaFeatures(true);
        AppPermissionSettings.Save(HostPermissions.DefaultGlobal with { AllowRemoteInput = false });

        Assert.False(AppClientControlSettings.IsEnabled());
        Assert.True(AppLoggingSettings.IsEnabled());
        Assert.True(AppDeveloperSettings.EnableAlphaFeatures());
        Assert.False(AppPermissionSettings.Load().AllowRemoteInput);
    }

    [Fact]
    public void PermissionHotPathUsesWriteThroughCache()
    {
        var blocked = HostPermissions.DefaultGlobal with { AllowRemoteInput = false };
        AppPermissionSettings.Save(blocked);

        using (var key = Registry.CurrentUser.OpenSubKey(HostSettingsRegistry.SettingsKeyPath, writable: true))
        {
            Assert.NotNull(key);
            key.SetValue("AllowRemoteInput", 1, RegistryValueKind.DWord);
        }

        Assert.Same(blocked, AppPermissionSettings.Load());

        var allowed = blocked with { AllowRemoteInput = true };
        AppPermissionSettings.Save(allowed);
        Assert.Same(allowed, AppPermissionSettings.Load());
    }

    [Fact]
    public void LegacyCursorRecoverySettingIsRemoved()
    {
        using (var key = Registry.CurrentUser.OpenSubKey(HostSettingsRegistry.SettingsKeyPath, writable: true))
        {
            Assert.NotNull(key);
            key.SetValue("UseCursorRecoveryWatchdog", 0, RegistryValueKind.DWord);
        }

        AppPointerSettings.RemoveLegacyCursorRecoverySetting();

        using var refreshed = Registry.CurrentUser.OpenSubKey(HostSettingsRegistry.SettingsKeyPath, writable: false);
        Assert.NotNull(refreshed);
        Assert.Null(refreshed.GetValue("UseCursorRecoveryWatchdog"));
    }

    [Fact]
    public void PointerCommunicationSettingsUseAWriteThroughCache()
    {
        var expectedLaser = new PresentationLaserPointerSettings(9, PresentationLaserColor.Blue);
        AppPointerSettings.SetPresentationLaserPointer(expectedLaser);

        using (var key = Registry.CurrentUser.OpenSubKey(HostSettingsRegistry.SettingsKeyPath, writable: true))
        {
            Assert.NotNull(key);
            key.SetValue("PresentationLaserSize", 1, RegistryValueKind.DWord);
            key.SetValue("PresentationLaserColor", (int)PresentationLaserColor.Red, RegistryValueKind.DWord);
        }

        Assert.Equal(expectedLaser, AppPointerSettings.GetPresentationLaserPointer());

        var updatedLaser = new PresentationLaserPointerSettings(5, PresentationLaserColor.Green);
        AppPointerSettings.SetPresentationLaserPointer(updatedLaser);
        Assert.Equal(updatedLaser, AppPointerSettings.GetPresentationLaserPointer());
    }

    [Fact]
    public void AlphaFeaturesCanBeEnabledAndDisabled()
    {
        AppDeveloperSettings.SetEnableAlphaFeatures(false);
        Assert.False(AppDeveloperSettings.EnableAlphaFeatures());

        AppDeveloperSettings.SetEnableAlphaFeatures(true);
        Assert.True(AppDeveloperSettings.EnableAlphaFeatures());

        AppDeveloperSettings.SetEnableAlphaFeatures(false);
        Assert.False(AppDeveloperSettings.EnableAlphaFeatures());
    }

    [Fact]
    public void ModeButtonsAreEnabledByDefaultAndCanBeDisabled()
    {
        Assert.True(AppAppearanceSettings.ShowModeButtons());

        AppAppearanceSettings.SetShowModeButtons(false);
        Assert.False(AppAppearanceSettings.ShowModeButtons());

        AppAppearanceSettings.SetShowModeButtons(true);
        Assert.True(AppAppearanceSettings.ShowModeButtons());
    }

    [Fact]
    public void CloseToTrayNotificationIsOnlyMarkedOnce()
    {
        Assert.True(AppWindowSettings.TryMarkCloseToTrayNotificationShown());
        Assert.False(AppWindowSettings.TryMarkCloseToTrayNotificationShown());
    }
}
