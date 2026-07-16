using Microsoft.Win32;

namespace VolturaAir.Host;

public static class AppWindowSettings
{
    private static string SettingsKeyPath => HostSettingsRegistry.SettingsKeyPath;
    private const string StartHiddenInTrayValueName = "StartHiddenInTray";

    public static bool StartHiddenInTray()
    {
        using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: false);
        return key?.GetValue(StartHiddenInTrayValueName) is int value && value != 0;
    }

    public static void SetStartHiddenInTray(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: true) ??
            Registry.CurrentUser.CreateSubKey(SettingsKeyPath, writable: true);
        key.SetValue(StartHiddenInTrayValueName, enabled ? 1 : 0, RegistryValueKind.DWord);
    }
}
