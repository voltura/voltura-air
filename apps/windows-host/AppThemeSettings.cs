using Microsoft.Win32;

namespace VolturaAir.Host;

public enum AppThemeMode
{
    System,
    Light,
    Dark
}

public static class AppThemeSettings
{
    private const string SettingsKeyPath = @"Software\VolturaAir";
    private const string ThemeValueName = "ThemeMode";

    public static event EventHandler? Changed;

    public static AppThemeMode GetMode()
    {
        using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: false);
        return Parse(key?.GetValue(ThemeValueName) as string);
    }

    public static void SetMode(AppThemeMode mode)
    {
        using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: true) ?? Registry.CurrentUser.CreateSubKey(SettingsKeyPath, writable: true);
        key.SetValue(ThemeValueName, mode.ToString(), RegistryValueKind.String);
        Changed?.Invoke(null, EventArgs.Empty);
    }

    private static AppThemeMode Parse(string? value)
    {
        return Enum.TryParse<AppThemeMode>(value, ignoreCase: true, out var mode)
            ? mode
            : AppThemeMode.System;
    }
}
