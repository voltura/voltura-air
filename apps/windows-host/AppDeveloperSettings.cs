using Microsoft.Win32;

namespace VolturaAir.Host;

public static class AppDeveloperSettings
{
    private const string SettingsKeyPath = @"Software\VolturaAir";
    private const string EnableGestureDebugValueName = "EnableGestureDebug";

    public static event EventHandler? Changed;

    public static bool EnableGestureDebug()
    {
        using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: false);
        return key?.GetValue(EnableGestureDebugValueName) is int value && value != 0;
    }

    public static void SetEnableGestureDebug(bool enabled)
    {
        var current = EnableGestureDebug();
        using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: true) ?? Registry.CurrentUser.CreateSubKey(SettingsKeyPath, writable: true);
        key.SetValue(EnableGestureDebugValueName, enabled ? 1 : 0, RegistryValueKind.DWord);

        if (current != enabled)
        {
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }
}
