namespace VolturaAir.Host;

public static class AppClientControlSettings
{
    private static string SettingsKeyPath => HostSettingsRegistry.SettingsKeyPath;
    private const string ValueName = "AllowPairedDeviceHostControl";

    public static event EventHandler? Changed;

    public static bool IsEnabled()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: false);
        return key?.GetValue(ValueName) is int value && value != 0;
    }

    public static void SetEnabled(bool enabled)
    {
        var current = IsEnabled();
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: true) ??
            Microsoft.Win32.Registry.CurrentUser.CreateSubKey(SettingsKeyPath, writable: true);
        key.SetValue(ValueName, enabled ? 1 : 0, Microsoft.Win32.RegistryValueKind.DWord);

        if (current != enabled)
        {
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }
}
