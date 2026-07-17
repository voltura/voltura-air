using Microsoft.Win32;

namespace VolturaAir.Host;

public static class AppLoggingSettings
{
    public const int DefaultMaxAgeDays = 2;
    public const int MinMaxAgeDays = 1;
    public const int MaxMaxAgeDays = 30;
    private static string SettingsKeyPath => HostSettingsRegistry.SettingsKeyPath;
    private const string EnabledValueName = "EnableApplicationLogging";
    private const string MaxAgeDaysValueName = "ApplicationLogMaxAgeDays";
    private static int _enabled;

    static AppLoggingSettings()
    {
        HostSettingsRegistry.SettingsScopeChanged += RefreshCachedValue;
        RefreshCachedValue();
    }

    public static bool IsEnabled()
    {
        return Volatile.Read(ref _enabled) != 0;
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: true) ??
            Registry.CurrentUser.CreateSubKey(SettingsKeyPath, writable: true);
        key.SetValue(EnabledValueName, enabled ? 1 : 0, RegistryValueKind.DWord);
        Volatile.Write(ref _enabled, enabled ? 1 : 0);
    }

    public static int GetMaxAgeDays()
    {
        using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: false);
        return key?.GetValue(MaxAgeDaysValueName) is int value
            ? Math.Clamp(value, MinMaxAgeDays, MaxMaxAgeDays)
            : DefaultMaxAgeDays;
    }

    public static void SetMaxAgeDays(int days)
    {
        using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: true) ??
            Registry.CurrentUser.CreateSubKey(SettingsKeyPath, writable: true);
        key.SetValue(MaxAgeDaysValueName, Math.Clamp(days, MinMaxAgeDays, MaxMaxAgeDays), RegistryValueKind.DWord);
    }

    private static bool ReadEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: false);
            return key?.GetValue(EnabledValueName) is int value && value != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }

    private static void RefreshCachedValue()
    {
        Volatile.Write(ref _enabled, ReadEnabled() ? 1 : 0);
    }
}
