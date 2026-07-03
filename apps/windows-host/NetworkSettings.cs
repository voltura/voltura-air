using Microsoft.Win32;

namespace VolturaAir.Host;

internal enum NetworkSelectionMode
{
    Automatic,
    Manual
}

internal enum PortSelectionMode
{
    Automatic,
    Manual
}

internal sealed record NetworkSettingsSnapshot(
    NetworkSelectionMode NetworkMode,
    string? ManualHostAddress,
    PortSelectionMode PortMode,
    int? ManualPort,
    int? LastAutomaticPort,
    string? LastAutomaticHostAddress);

internal static class AppNetworkSettings
{
    private const string SettingsKeyPath = @"Software\VolturaAir";
    private const string NetworkModeValueName = "NetworkMode";
    private const string ManualHostAddressValueName = "ManualHostAddress";
    private const string PortModeValueName = "PortMode";
    private const string ManualPortValueName = "ManualPort";
    private const string LastAutomaticPortValueName = "LastAutomaticPort";
    private const string LastAutomaticHostAddressValueName = "LastAutomaticHostAddress";

    public static NetworkSettingsSnapshot Load()
    {
        using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: false);
        return new NetworkSettingsSnapshot(
            ParseEnum(key?.GetValue(NetworkModeValueName) as string, NetworkSelectionMode.Automatic),
            key?.GetValue(ManualHostAddressValueName) as string,
            ParseEnum(key?.GetValue(PortModeValueName) as string, PortSelectionMode.Automatic),
            ReadPort(key, ManualPortValueName),
            ReadPort(key, LastAutomaticPortValueName),
            key?.GetValue(LastAutomaticHostAddressValueName) as string);
    }

    public static void Save(NetworkSettingsSnapshot settings)
    {
        using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: true) ??
            Registry.CurrentUser.CreateSubKey(SettingsKeyPath, writable: true);

        key.SetValue(NetworkModeValueName, settings.NetworkMode.ToString(), RegistryValueKind.String);
        SetOptionalString(key, ManualHostAddressValueName, settings.ManualHostAddress);
        key.SetValue(PortModeValueName, settings.PortMode.ToString(), RegistryValueKind.String);
        SetOptionalPort(key, ManualPortValueName, settings.ManualPort);
        SetOptionalPort(key, LastAutomaticPortValueName, settings.LastAutomaticPort);
        SetOptionalString(key, LastAutomaticHostAddressValueName, settings.LastAutomaticHostAddress);
    }

    public static void SetLastAutomaticPort(int port)
    {
        if (!PortSelector.IsValidPort(port))
        {
            return;
        }

        var settings = Load();
        Save(settings with { LastAutomaticPort = port });
    }

    public static void SetLastAutomaticHostAddress(string hostAddress)
    {
        var settings = Load();
        Save(settings with { LastAutomaticHostAddress = hostAddress });
    }

    private static TEnum ParseEnum<TEnum>(string? value, TEnum fallback)
        where TEnum : struct
    {
        return Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) ? parsed : fallback;
    }

    private static int? ReadPort(RegistryKey? key, string valueName)
    {
        return key?.GetValue(valueName) is int value && PortSelector.IsValidPort(value)
            ? value
            : null;
    }

    private static void SetOptionalString(RegistryKey key, string valueName, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            key.DeleteValue(valueName, throwOnMissingValue: false);
            return;
        }

        key.SetValue(valueName, value, RegistryValueKind.String);
    }

    private static void SetOptionalPort(RegistryKey key, string valueName, int? port)
    {
        if (port is null || !PortSelector.IsValidPort(port.Value))
        {
            key.DeleteValue(valueName, throwOnMissingValue: false);
            return;
        }

        key.SetValue(valueName, port.Value, RegistryValueKind.DWord);
    }
}
