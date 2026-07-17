namespace VolturaAir.Host.Tests;

[Collection(AppPermissionSettingsCollection.Name)]
public sealed class HostSettingsRegistryTests
{
    [Fact]
    public void IsolatedScopeRefreshesCachedHotPathSettingsAndRestoresProductionValues()
    {
        var productionClientControl = AppClientControlSettings.IsEnabled();
        var productionLogging = AppLoggingSettings.IsEnabled();
        var productionAlphaFeatures = AppDeveloperSettings.EnableAlphaFeatures();

        using (HostSettingsRegistry.BeginIsolatedScope())
        {
            Assert.False(AppClientControlSettings.IsEnabled());
            Assert.False(AppLoggingSettings.IsEnabled());
            Assert.False(AppDeveloperSettings.EnableAlphaFeatures());

            var isolatedClientControl = !productionClientControl;
            var isolatedLogging = !productionLogging;
            AppClientControlSettings.SetEnabled(isolatedClientControl);
            AppLoggingSettings.SetEnabled(isolatedLogging);
            AppDeveloperSettings.SetEnableAlphaFeatures(true);

            Assert.Equal(isolatedClientControl, AppClientControlSettings.IsEnabled());
            Assert.Equal(isolatedLogging, AppLoggingSettings.IsEnabled());
            Assert.True(AppDeveloperSettings.EnableAlphaFeatures());
        }

        Assert.Equal(productionClientControl, AppClientControlSettings.IsEnabled());
        Assert.Equal(productionLogging, AppLoggingSettings.IsEnabled());
        Assert.Equal(productionAlphaFeatures, AppDeveloperSettings.EnableAlphaFeatures());
    }

    [Fact]
    public void CursorRecoveryWatchdogIsEnabledByDefaultAndCanBeDisabled()
    {
        using var scope = HostSettingsRegistry.BeginIsolatedScope();

        Assert.True(AppPointerSettings.UseCursorRecoveryWatchdog());

        AppPointerSettings.SetUseCursorRecoveryWatchdog(false);
        Assert.False(AppPointerSettings.UseCursorRecoveryWatchdog());

        AppPointerSettings.SetUseCursorRecoveryWatchdog(true);
        Assert.True(AppPointerSettings.UseCursorRecoveryWatchdog());
    }

    [Fact]
    public void AlphaFeaturesAreDisabledByDefaultAndCanBeEnabled()
    {
        using var scope = HostSettingsRegistry.BeginIsolatedScope();

        Assert.False(AppDeveloperSettings.EnableAlphaFeatures());

        AppDeveloperSettings.SetEnableAlphaFeatures(true);
        Assert.True(AppDeveloperSettings.EnableAlphaFeatures());

        AppDeveloperSettings.SetEnableAlphaFeatures(false);
        Assert.False(AppDeveloperSettings.EnableAlphaFeatures());
    }
}
