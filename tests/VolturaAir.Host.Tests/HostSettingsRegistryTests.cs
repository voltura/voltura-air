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

        Assert.False(AppClientControlSettings.IsEnabled());
        Assert.True(AppLoggingSettings.IsEnabled());
        Assert.True(AppDeveloperSettings.EnableAlphaFeatures());
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
    public void CloseToTrayNotificationIsOnlyMarkedOnce()
    {
        Assert.True(AppWindowSettings.TryMarkCloseToTrayNotificationShown());
        Assert.False(AppWindowSettings.TryMarkCloseToTrayNotificationShown());
    }
}
