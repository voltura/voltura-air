using Microsoft.Win32;

namespace VolturaAir.Host;

public static class AppAppearanceSettings
{
    private static string SettingsKeyPath => HostSettingsRegistry.SettingsKeyPath;
    private const string ShowModeButtonsValueName = "ShowModeButtons";
    private const string DeviceControlDepthValueName = "DeviceControlDepth";
    private const string HostControlDepthValueName = "HostControlDepth";
    private const string DeviceAccentColorValueName = "DeviceAccentColor";

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

    public static string? DeviceAccentColor()
    {
        using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: false);
        return AccentColor.NormalizePersisted(key?.GetValue(DeviceAccentColorValueName) as string);
    }

    public static void SetDeviceAccentColor(string? accentColor)
    {
        if (accentColor is not null && !AccentColor.IsCanonical(accentColor))
        {
            throw new ArgumentException("Accent color must use canonical #RRGGBB format.", nameof(accentColor));
        }

        var current = DeviceAccentColor();
        using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: true) ??
            Registry.CurrentUser.CreateSubKey(SettingsKeyPath, writable: true);
        if (accentColor is null)
        {
            key.DeleteValue(DeviceAccentColorValueName, throwOnMissingValue: false);
        }
        else
        {
            key.SetValue(DeviceAccentColorValueName, accentColor, RegistryValueKind.String);
        }

        if (!string.Equals(current, accentColor, StringComparison.Ordinal))
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
