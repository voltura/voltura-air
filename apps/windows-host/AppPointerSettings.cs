using Microsoft.Win32;

namespace VolturaAir.Host;

public static class AppPointerSettings
{
    private static string SettingsKeyPath => HostSettingsRegistry.SettingsKeyPath;
    private const string DefaultPointerSpeedValueName = "DefaultPointerSpeed";
    private const string CustomPointerEnabledValueName = "CustomPointerEnabled";
    private const string CustomPointerSizeValueName = "CustomPointerSize";
    private const string CustomPointerColorValueName = "CustomPointerColor";
    private const string UseCursorRecoveryWatchdogValueName = "UseCursorRecoveryWatchdog";
    public const int MinCustomPointerSize = 1;
    public const int MaxCustomPointerSize = 15;
    public const int DefaultCustomPointerSize = 6;
    public const uint DefaultCustomPointerColor = 0x12A894;

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

    public static CustomPointerSettings GetCustomPointer()
    {
        using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: false);
        return new CustomPointerSettings(
            key?.GetValue(CustomPointerEnabledValueName) is int enabled && enabled != 0,
            NormalizeCustomPointerSize(key?.GetValue(CustomPointerSizeValueName) as int? ?? DefaultCustomPointerSize),
            NormalizeCustomPointerColor(key?.GetValue(CustomPointerColorValueName) as int? ?? unchecked((int)DefaultCustomPointerColor)));
    }

    public static void SetCustomPointer(CustomPointerSettings settings)
    {
        var normalized = settings with
        {
            Size = NormalizeCustomPointerSize(settings.Size),
            Color = NormalizeCustomPointerColor(unchecked((int)settings.Color))
        };
        var current = GetCustomPointer();
        using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: true) ??
            Registry.CurrentUser.CreateSubKey(SettingsKeyPath, writable: true);
        key.SetValue(CustomPointerEnabledValueName, normalized.Enabled ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue(CustomPointerSizeValueName, normalized.Size, RegistryValueKind.DWord);
        key.SetValue(CustomPointerColorValueName, unchecked((int)normalized.Color), RegistryValueKind.DWord);

        if (current != normalized)
        {
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }

    public static bool UseCursorRecoveryWatchdog()
    {
        using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: false);
        return key?.GetValue(UseCursorRecoveryWatchdogValueName) is not int enabled || enabled != 0;
    }

    public static void SetUseCursorRecoveryWatchdog(bool enabled)
    {
        var current = UseCursorRecoveryWatchdog();
        using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: true) ??
            Registry.CurrentUser.CreateSubKey(SettingsKeyPath, writable: true);
        key.SetValue(UseCursorRecoveryWatchdogValueName, enabled ? 1 : 0, RegistryValueKind.DWord);

        if (current != enabled)
        {
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }

    public static int NormalizeCustomPointerSize(int size) => Math.Clamp(size, MinCustomPointerSize, MaxCustomPointerSize);

    public static uint NormalizeCustomPointerColor(int color) => unchecked((uint)color) & 0x00FFFFFF;
}

public readonly record struct CustomPointerSettings(bool Enabled, int Size, uint Color);
