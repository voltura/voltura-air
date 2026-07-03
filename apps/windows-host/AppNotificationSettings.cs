using Microsoft.Win32;

namespace VolturaAir.Host;

public static class AppNotificationSettings
{
    private const string SettingsKeyPath = @"Software\VolturaAir";
    private const string ConnectionStatusNotificationsValueName = "ShowConnectionStatusNotifications";

    public static bool ShowConnectionStatusNotifications()
    {
        using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: false);
        return key?.GetValue(ConnectionStatusNotificationsValueName) is not int value || value != 0;
    }

    public static void SetShowConnectionStatusNotifications(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: true) ?? Registry.CurrentUser.CreateSubKey(SettingsKeyPath, writable: true);
        key.SetValue(ConnectionStatusNotificationsValueName, enabled ? 1 : 0, RegistryValueKind.DWord);
    }
}
