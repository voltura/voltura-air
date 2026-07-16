using System.Globalization;
using Microsoft.Win32;

namespace VolturaAir.Host;

public static class AppAwakeSettings
{
    private static string SettingsKeyPath => HostSettingsRegistry.SettingsKeyPath;
    private const string ModeValueName = "AwakeMode";
    private const string KeepScreenOnValueName = "AwakeKeepScreenOn";
    private const string IntervalMinutesValueName = "AwakeIntervalMinutes";
    private const string ExpiresAtValueName = "AwakeExpiresAt";

    public static AwakeState Load()
    {
        using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: false);
        var mode = key?.GetValue(ModeValueName) is int modeValue && Enum.IsDefined(typeof(AwakeMode), modeValue)
            ? (AwakeMode)modeValue
            : AwakeMode.Off;
        var keepScreenOn = key?.GetValue(KeepScreenOnValueName) is int screenValue && screenValue != 0;
        var intervalMinutes = key?.GetValue(IntervalMinutesValueName) is int intervalValue
            ? NormalizeIntervalMinutes(intervalValue)
            : 60;
        var expiresAt = TryParseExpiration(key?.GetValue(ExpiresAtValueName) as string);

        if (mode is AwakeMode.Timed or AwakeMode.Expiration &&
            (expiresAt is null || expiresAt <= DateTimeOffset.Now))
        {
            mode = AwakeMode.Off;
            expiresAt = null;
        }

        return new AwakeState(mode, keepScreenOn, intervalMinutes, expiresAt);
    }

    public static void Save(AwakeState state)
    {
        using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: true) ??
            Registry.CurrentUser.CreateSubKey(SettingsKeyPath, writable: true);

        key.SetValue(ModeValueName, (int)state.Mode, RegistryValueKind.DWord);
        key.SetValue(KeepScreenOnValueName, state.KeepScreenOn ? 1 : 0, RegistryValueKind.DWord);
        key.SetValue(IntervalMinutesValueName, NormalizeIntervalMinutes(state.IntervalMinutes), RegistryValueKind.DWord);
        if (state.ExpiresAt is { } expiresAt)
        {
            key.SetValue(ExpiresAtValueName, expiresAt.ToString("O", CultureInfo.InvariantCulture), RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(ExpiresAtValueName, throwOnMissingValue: false);
        }
    }

    internal static int NormalizeIntervalMinutes(int value) => Math.Clamp(value, 1, 525_600);

    private static DateTimeOffset? TryParseExpiration(string? value)
    {
        return DateTimeOffset.TryParseExact(value, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
    }
}
