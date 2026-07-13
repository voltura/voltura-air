using Microsoft.Win32;

namespace VolturaAir.Host;

public static class AppNotificationSettings
{
    private const string SettingsKeyPath = @"Software\VolturaAir";
    private const string ConnectionStatusNotificationsValueName = "ShowConnectionStatusNotifications";
    private const string PairingWindowOnDisconnectValueName = "ShowPairingWindowOnDisconnect";

    public static bool ShowConnectionStatusNotifications()
    {
        return GetEnabledValue(ConnectionStatusNotificationsValueName, defaultValue: true);
    }

    public static void SetShowConnectionStatusNotifications(bool enabled)
    {
        SetEnabledValue(ConnectionStatusNotificationsValueName, enabled);
    }

    public static bool ShowPairingWindowOnDisconnect()
    {
        return GetEnabledValue(PairingWindowOnDisconnectValueName, defaultValue: false);
    }

    public static void SetShowPairingWindowOnDisconnect(bool enabled)
    {
        SetEnabledValue(PairingWindowOnDisconnectValueName, enabled);
    }

    private static bool GetEnabledValue(string valueName, bool defaultValue)
    {
        using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: false);
        return key?.GetValue(valueName) is int value ? value != 0 : defaultValue;
    }

    private static void SetEnabledValue(string valueName, bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: true) ?? Registry.CurrentUser.CreateSubKey(SettingsKeyPath, writable: true);
        key.SetValue(valueName, enabled ? 1 : 0, RegistryValueKind.DWord);
    }
}
