using Microsoft.Win32;

namespace VolturaAir.Host;

public static class AppPermissionSettings
{
    private const string SettingsKeyPath = @"Software\VolturaAir";
    private const string AllowPcSleepValueName = "AllowPcSleep";
    private const string AllowVolumeControlValueName = "AllowVolumeControl";

    public static event EventHandler? Changed;

    public static HostPermissionSet Load()
    {
        using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: false);
        return new HostPermissionSet(
            AllowPcSleep: GetBooleanValue(key, AllowPcSleepValueName, HostPermissions.DefaultGlobal.AllowPcSleep),
            AllowVolumeControl: GetBooleanValue(key, AllowVolumeControlValueName, HostPermissions.DefaultGlobal.AllowVolumeControl));
    }

    public static void Save(HostPermissionSet permissions)
    {
        var current = Load();
        using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: true) ??
            Registry.CurrentUser.CreateSubKey(SettingsKeyPath, writable: true);

        key.SetValue(AllowPcSleepValueName, permissions.AllowPcSleep ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue(AllowVolumeControlValueName, permissions.AllowVolumeControl ? 1 : 0, RegistryValueKind.DWord);

        if (current != permissions)
        {
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }

    private static bool GetBooleanValue(RegistryKey? key, string valueName, bool defaultValue)
    {
        return key?.GetValue(valueName) is int value ? value != 0 : defaultValue;
    }
}
