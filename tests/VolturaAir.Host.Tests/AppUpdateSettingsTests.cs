namespace VolturaAir.Host.Tests;

public sealed class AppUpdateSettingsTests : IsolatedHostSettingsTest
{
    [Fact]
    public void DefaultsToAutomaticDownloadsAndPublishesChanges()
    {
        var changes = 0;
        EventHandler handler = (_, _) => changes += 1;
        AppUpdateSettings.Changed += handler;
        try
        {
            Assert.True(AppUpdateSettings.AutomaticUpdateDownloadsEnabled());
            AppUpdateSettings.SetAutomaticUpdateDownloadsEnabled(false);
            Assert.False(AppUpdateSettings.AutomaticUpdateDownloadsEnabled());
            AppUpdateSettings.SetAutomaticUpdateDownloadsEnabled(true);
            Assert.True(AppUpdateSettings.AutomaticUpdateDownloadsEnabled());
            Assert.Equal(2, changes);
        }
        finally
        {
            AppUpdateSettings.Changed -= handler;
        }
    }
}
