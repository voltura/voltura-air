namespace VolturaAir.Host.Tests;

[Collection(AppPermissionSettingsCollection.Name)]
public sealed class HostSettingsRegistryTests
{
    [Fact]
    public void IsolatedScopeRefreshesCachedHotPathSettingsAndRestoresProductionValues()
    {
        var productionClientControl = AppClientControlSettings.IsEnabled();
        var productionLogging = AppLoggingSettings.IsEnabled();

        using (HostSettingsRegistry.BeginIsolatedScope())
        {
            Assert.False(AppClientControlSettings.IsEnabled());
            Assert.False(AppLoggingSettings.IsEnabled());

            var isolatedClientControl = !productionClientControl;
            var isolatedLogging = !productionLogging;
            AppClientControlSettings.SetEnabled(isolatedClientControl);
            AppLoggingSettings.SetEnabled(isolatedLogging);

            Assert.Equal(isolatedClientControl, AppClientControlSettings.IsEnabled());
            Assert.Equal(isolatedLogging, AppLoggingSettings.IsEnabled());
        }

        Assert.Equal(productionClientControl, AppClientControlSettings.IsEnabled());
        Assert.Equal(productionLogging, AppLoggingSettings.IsEnabled());
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
}
