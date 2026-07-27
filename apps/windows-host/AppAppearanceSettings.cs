using Microsoft.Win32;

namespace VolturaAir.Host;

public static class AppAppearanceSettings
{
    private static string SettingsKeyPath => HostSettingsRegistry.SettingsKeyPath;
    private const string ShowModeButtonsValueName = "ShowModeButtons";
    private const string DeviceControlDepthValueName = "DeviceControlDepth";
    private const string HostControlDepthValueName = "HostControlDepth";

    public static event EventHandler? Changed;
    public static event EventHandler? HostControlDepthChanged;

    public static bool ShowModeButtons()
    {
        using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: false);
        return key?.GetValue(ShowModeButtonsValueName) is not int value || value != 0;
    }

    public static void SetShowModeButtons(bool showModeButtons)
    {
        var current = ShowModeButtons();
        using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: true) ??
            Registry.CurrentUser.CreateSubKey(SettingsKeyPath, writable: true);
        key.SetValue(ShowModeButtonsValueName, showModeButtons ? 1 : 0, RegistryValueKind.DWord);

        if (current != showModeButtons)
        {
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }

    public static bool DeviceControlDepth()
    {
        using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: false);
        return key?.GetValue(DeviceControlDepthValueName) is not int value || value != 0;
    }

    public static void SetDeviceControlDepth(bool enabled)
    {
        var current = DeviceControlDepth();
        WriteBoolean(DeviceControlDepthValueName, enabled);
        if (current != enabled)
        {
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }

    public static bool HostControlDepth()
    {
        using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: false);
        return key?.GetValue(HostControlDepthValueName) is int value && value != 0;
    }

    public static void SetHostControlDepth(bool enabled)
    {
        var current = HostControlDepth();
        WriteBoolean(HostControlDepthValueName, enabled);
        if (current != enabled)
        {
            HostControlDepthChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    private static void WriteBoolean(string valueName, bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: true) ??
            Registry.CurrentUser.CreateSubKey(SettingsKeyPath, writable: true);
        key.SetValue(valueName, enabled ? 1 : 0, RegistryValueKind.DWord);
    }
}
