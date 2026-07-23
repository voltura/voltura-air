using Microsoft.Win32;

namespace VolturaAir.Host;

public static class AppPointerSettings
{
    private static string SettingsKeyPath => HostSettingsRegistry.SettingsKeyPath;
    private const string DefaultPointerSpeedValueName = "DefaultPointerSpeed";
    private const string CustomPointerEnabledValueName = "CustomPointerEnabled";
    private const string CustomPointerSizeValueName = "CustomPointerSize";
    private const string CustomPointerColorValueName = "CustomPointerColor";
    private const string PresentationLaserSizeValueName = "PresentationLaserSize";
    private const string PresentationLaserColorValueName = "PresentationLaserColor";
    private const string UseCursorRecoveryWatchdogValueName = "UseCursorRecoveryWatchdog";
    private static readonly Lock Gate = new();
    public const int MinCustomPointerSize = 1;
    public const int MaxCustomPointerSize = 15;
    public const int DefaultCustomPointerSize = 6;
    public const uint DefaultCustomPointerColor = 0x12A894;
    public const int DefaultPresentationLaserSize = 6;
    private static PointerSettingsSnapshot _cachedSettings = PointerSettingsSnapshot.Default;

    static AppPointerSettings()
    {
        HostSettingsRegistry.SettingsScopeChanged += RefreshCachedSettings;
        RefreshCachedSettings();
    }

    public static event EventHandler? Changed;

    public static int GetDefaultPointerSpeed() => Volatile.Read(ref _cachedSettings).DefaultPointerSpeed;

    public static void SetDefaultPointerSpeed(int pointerSpeed)
    {
        var normalized = DevicePointerProfile.NormalizePointerSpeed(pointerSpeed);
        PointerSettingsSnapshot current;
        lock (Gate)
        {
            current = Volatile.Read(ref _cachedSettings);
            using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: true) ??
                Registry.CurrentUser.CreateSubKey(SettingsKeyPath, writable: true);
            key.SetValue(DefaultPointerSpeedValueName, normalized, RegistryValueKind.DWord);
            Volatile.Write(ref _cachedSettings, current with { DefaultPointerSpeed = normalized });
        }

        if (current.DefaultPointerSpeed != normalized)
        {
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }

    public static CustomPointerSettings GetCustomPointer() => Volatile.Read(ref _cachedSettings).CustomPointer;

    public static void SetCustomPointer(CustomPointerSettings settings)
    {
        var normalized = settings with
        {
            Size = NormalizeCustomPointerSize(settings.Size),
            Color = NormalizeCustomPointerColor(unchecked((int)settings.Color))
        };
        PointerSettingsSnapshot current;
        lock (Gate)
        {
            current = Volatile.Read(ref _cachedSettings);
            using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: true) ??
                Registry.CurrentUser.CreateSubKey(SettingsKeyPath, writable: true);
            key.SetValue(CustomPointerEnabledValueName, normalized.Enabled ? 1 : 0, RegistryValueKind.DWord);
            key.SetValue(CustomPointerSizeValueName, normalized.Size, RegistryValueKind.DWord);
            key.SetValue(CustomPointerColorValueName, unchecked((int)normalized.Color), RegistryValueKind.DWord);
            Volatile.Write(ref _cachedSettings, current with { CustomPointer = normalized });
        }

        if (current.CustomPointer != normalized)
        {
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }

    public static bool UseCursorRecoveryWatchdog() => Volatile.Read(ref _cachedSettings).UseCursorRecoveryWatchdog;

    public static void SetUseCursorRecoveryWatchdog(bool enabled)
    {
        PointerSettingsSnapshot current;
        lock (Gate)
        {
            current = Volatile.Read(ref _cachedSettings);
            using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: true) ??
                Registry.CurrentUser.CreateSubKey(SettingsKeyPath, writable: true);
            key.SetValue(UseCursorRecoveryWatchdogValueName, enabled ? 1 : 0, RegistryValueKind.DWord);
            Volatile.Write(ref _cachedSettings, current with { UseCursorRecoveryWatchdog = enabled });
        }

        if (current.UseCursorRecoveryWatchdog != enabled)
        {
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }

    public static int NormalizeCustomPointerSize(int size) => Math.Clamp(size, MinCustomPointerSize, MaxCustomPointerSize);

    public static uint NormalizeCustomPointerColor(int color) => unchecked((uint)color) & 0x00FFFFFF;

    public static PresentationLaserPointerSettings GetPresentationLaserPointer() =>
        Volatile.Read(ref _cachedSettings).PresentationLaserPointer;

    public static void SetPresentationLaserPointer(PresentationLaserPointerSettings settings)
    {
        var normalized = settings with
        {
            Size = NormalizeCustomPointerSize(settings.Size),
            Color = Enum.IsDefined(settings.Color) ? settings.Color : PresentationLaserColor.Red
        };
        PointerSettingsSnapshot current;
        lock (Gate)
        {
            current = Volatile.Read(ref _cachedSettings);
            using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: true) ??
                Registry.CurrentUser.CreateSubKey(SettingsKeyPath, writable: true);
            key.SetValue(PresentationLaserSizeValueName, normalized.Size, RegistryValueKind.DWord);
            key.SetValue(PresentationLaserColorValueName, (int)normalized.Color, RegistryValueKind.DWord);
            Volatile.Write(ref _cachedSettings, current with { PresentationLaserPointer = normalized });
        }

        if (current.PresentationLaserPointer != normalized)
        {
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }

    private static void RefreshCachedSettings()
    {
        lock (Gate)
        {
            Volatile.Write(ref _cachedSettings, ReadSettings());
        }
    }

    private static PointerSettingsSnapshot ReadSettings()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: false);
            return new PointerSettingsSnapshot(
                key?.GetValue(DefaultPointerSpeedValueName) is int pointerSpeed
                    ? DevicePointerProfile.NormalizePointerSpeed(pointerSpeed)
                    : DevicePointerProfile.DefaultPointerSpeed,
                new CustomPointerSettings(
                    key?.GetValue(CustomPointerEnabledValueName) is int enabled && enabled != 0,
                    NormalizeCustomPointerSize(key?.GetValue(CustomPointerSizeValueName) as int? ?? DefaultCustomPointerSize),
                    NormalizeCustomPointerColor(key?.GetValue(CustomPointerColorValueName) as int? ?? unchecked((int)DefaultCustomPointerColor))),
                new PresentationLaserPointerSettings(
                    NormalizeCustomPointerSize(key?.GetValue(PresentationLaserSizeValueName) as int? ?? DefaultPresentationLaserSize),
                    key?.GetValue(PresentationLaserColorValueName) is int laserColor &&
                        Enum.IsDefined((PresentationLaserColor)laserColor)
                            ? (PresentationLaserColor)laserColor
                            : PresentationLaserColor.Red),
                key?.GetValue(UseCursorRecoveryWatchdogValueName) is not int watchdogEnabled || watchdogEnabled != 0);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return PointerSettingsSnapshot.Default;
        }
    }

    private sealed record PointerSettingsSnapshot(
        int DefaultPointerSpeed,
        CustomPointerSettings CustomPointer,
        PresentationLaserPointerSettings PresentationLaserPointer,
        bool UseCursorRecoveryWatchdog)
    {
        public static PointerSettingsSnapshot Default { get; } = new(
            DevicePointerProfile.DefaultPointerSpeed,
            new CustomPointerSettings(false, DefaultCustomPointerSize, DefaultCustomPointerColor),
            new PresentationLaserPointerSettings(DefaultPresentationLaserSize, PresentationLaserColor.Red),
            true);
    }
}

public readonly record struct CustomPointerSettings(bool Enabled, int Size, uint Color);

public readonly record struct PresentationLaserPointerSettings(int Size, PresentationLaserColor Color);

public enum PresentationLaserColor
{
    Red,
    Green,
    Blue
}
