using Microsoft.Win32;

namespace VolturaAir.Host.Tests;

[Collection(AppPermissionSettingsCollection.Name)]
public sealed class HostSettingsRegistryTests : IsolatedHostSettingsTest
{
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
    public void CursorRecoveryWatchdogIsEnabledByDefaultAndCanBeDisabled()
    {
        Assert.True(AppPointerSettings.UseCursorRecoveryWatchdog());

        AppPointerSettings.SetUseCursorRecoveryWatchdog(false);
        Assert.False(AppPointerSettings.UseCursorRecoveryWatchdog());

        AppPointerSettings.SetUseCursorRecoveryWatchdog(true);
        Assert.True(AppPointerSettings.UseCursorRecoveryWatchdog());
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
