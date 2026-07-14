using Microsoft.Win32;

namespace VolturaAir.Host;

public static class AppPointerSettings
{
    private const string SettingsKeyPath = @"Software\VolturaAir";
    private const string DefaultPointerSpeedValueName = "DefaultPointerSpeed";
    private const string HighlightPointerValueName = "HighlightPointer";

    public static event EventHandler? Changed;

    public static int GetDefaultPointerSpeed()
    {
        using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: false);
        return key?.GetValue(DefaultPointerSpeedValueName) is int value
            ? DevicePointerProfile.NormalizePointerSpeed(value)
            : DevicePointerProfile.DefaultPointerSpeed;
    }

    public static void SetDefaultPointerSpeed(int pointerSpeed)
    {
        var normalized = DevicePointerProfile.NormalizePointerSpeed(pointerSpeed);
        var current = GetDefaultPointerSpeed();
        using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: true) ??
            Registry.CurrentUser.CreateSubKey(SettingsKeyPath, writable: true);
        key.SetValue(DefaultPointerSpeedValueName, normalized, RegistryValueKind.DWord);

        if (current != normalized)
        {
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }

    public static bool HighlightPointer()
    {
        using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: false);
        return key?.GetValue(HighlightPointerValueName) is int value && value != 0;
    }

    public static void SetHighlightPointer(bool enabled)
    {
        var current = HighlightPointer();
        using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: true) ??
            Registry.CurrentUser.CreateSubKey(SettingsKeyPath, writable: true);
        key.SetValue(HighlightPointerValueName, enabled ? 1 : 0, RegistryValueKind.DWord);

        if (current != enabled)
        {
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }
}
