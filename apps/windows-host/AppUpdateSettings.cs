using Microsoft.Win32;

namespace VolturaAir.Host;

/// <summary>Host-owned, deliberately small durable state for the optional updater.</summary>
internal static class AppUpdateSettings
{
    internal static event EventHandler? Changed;
    private const string AutomaticDownloadsValue = "AutomaticUpdateDownloadsEnabled";
    private const string LastAttemptValue = "LastUpdateCheckAttemptUtc";

    internal static bool AutomaticUpdateDownloadsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(HostSettingsRegistry.SettingsKeyPath, writable: false);
        return key?.GetValue(AutomaticDownloadsValue) is not int value || value != 0;
    }

    internal static void SetAutomaticUpdateDownloadsEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(HostSettingsRegistry.SettingsKeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(HostSettingsRegistry.SettingsKeyPath, writable: true);
        key.SetValue(AutomaticDownloadsValue, enabled ? 1 : 0, RegistryValueKind.DWord);
        Changed?.Invoke(null, EventArgs.Empty);
    }

    internal static DateTimeOffset? LastUpdateCheckAttemptUtc()
    {
        using var key = Registry.CurrentUser.OpenSubKey(HostSettingsRegistry.SettingsKeyPath, writable: false);
        return key?.GetValue(LastAttemptValue) is string value && DateTimeOffset.TryParse(value, out var parsed)
            ? parsed.ToUniversalTime()
            : null;
    }

    internal static void SetLastUpdateCheckAttemptUtc(DateTimeOffset value)
    {
        using var key = Registry.CurrentUser.OpenSubKey(HostSettingsRegistry.SettingsKeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(HostSettingsRegistry.SettingsKeyPath, writable: true);
        key.SetValue(LastAttemptValue, value.UtcDateTime.ToString("O"), RegistryValueKind.String);
    }
}
