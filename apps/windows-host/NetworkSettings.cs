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

internal enum ConnectionTransportMode
{
    DirectLan,
    Relay
}

internal enum RelayScreenQuality
{
    DataSaver,
    Standard,
    MaintainerFull
}

internal sealed record NetworkSettingsSnapshot(
    NetworkSelectionMode NetworkMode,
    string? ManualHostAddress,
    string? ManualAdapterId,
    string? ManualAdapterName,
    PortSelectionMode PortMode,
    int? ManualPort,
    int? LastAutomaticPort,
    string? LastAutomaticHostAddress,
    ConnectionTransportMode TransportMode = ConnectionTransportMode.DirectLan,
    string? CustomRelayEndpoint = null,
    RelayScreenQuality RelayScreenQuality = RelayScreenQuality.Standard);

internal static class AppNetworkSettings
{
    private static string SettingsKeyPath => HostSettingsRegistry.SettingsKeyPath;
    private const string NetworkModeValueName = "NetworkMode";
    private const string ManualHostAddressValueName = "ManualHostAddress";
    private const string ManualAdapterIdValueName = "ManualAdapterId";
    private const string ManualAdapterNameValueName = "ManualAdapterName";
    private const string PortModeValueName = "PortMode";
    private const string ManualPortValueName = "ManualPort";
    private const string LastAutomaticPortValueName = "LastAutomaticPort";
    private const string LastAutomaticHostAddressValueName = "LastAutomaticHostAddress";
    private const string TransportModeValueName = "ConnectionTransportMode";
    private const string CustomRelayEndpointValueName = "CustomRelayEndpoint";
    private const string RelayScreenQualityValueName = "RelayScreenQuality";

    public static NetworkSettingsSnapshot Load()
    {
        using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: false);
        return new NetworkSettingsSnapshot(
            ParseEnum(key?.GetValue(NetworkModeValueName) as string, NetworkSelectionMode.Automatic),
            key?.GetValue(ManualHostAddressValueName) as string,
            key?.GetValue(ManualAdapterIdValueName) as string,
            key?.GetValue(ManualAdapterNameValueName) as string,
            ParseEnum(key?.GetValue(PortModeValueName) as string, PortSelectionMode.Automatic),
            ReadPort(key, ManualPortValueName),
            ReadPort(key, LastAutomaticPortValueName),
            key?.GetValue(LastAutomaticHostAddressValueName) as string,
            ParseEnum(key?.GetValue(TransportModeValueName) as string, ConnectionTransportMode.DirectLan),
            NormalizeRelayEndpoint(key?.GetValue(CustomRelayEndpointValueName) as string),
            NormalizeRelayQuality(ParseEnum(key?.GetValue(RelayScreenQualityValueName) as string, RelayScreenQuality.Standard)));
    }

    public static void Save(NetworkSettingsSnapshot settings)
    {
        using var key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath, writable: true) ??
            Registry.CurrentUser.CreateSubKey(SettingsKeyPath, writable: true);

        key.SetValue(NetworkModeValueName, settings.NetworkMode.ToString(), RegistryValueKind.String);
        SetOptionalString(key, ManualHostAddressValueName, settings.ManualHostAddress);
        SetOptionalString(key, ManualAdapterIdValueName, settings.ManualAdapterId);
        SetOptionalString(key, ManualAdapterNameValueName, settings.ManualAdapterName);
        key.SetValue(PortModeValueName, settings.PortMode.ToString(), RegistryValueKind.String);
        SetOptionalPort(key, ManualPortValueName, settings.ManualPort);
        SetOptionalPort(key, LastAutomaticPortValueName, settings.LastAutomaticPort);
        SetOptionalString(key, LastAutomaticHostAddressValueName, settings.LastAutomaticHostAddress);
        key.SetValue(TransportModeValueName, settings.TransportMode.ToString(), RegistryValueKind.String);
        SetOptionalString(key, CustomRelayEndpointValueName, NormalizeRelayEndpoint(settings.CustomRelayEndpoint));
        key.SetValue(RelayScreenQualityValueName, NormalizeRelayQuality(settings.RelayScreenQuality).ToString(), RegistryValueKind.String);
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

    internal static string? NormalizeRelayEndpoint(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 512 ||
            !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var endpoint) ||
            !string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(endpoint.UserInfo) || !string.IsNullOrEmpty(endpoint.Query) ||
            !string.IsNullOrEmpty(endpoint.Fragment) || endpoint.AbsolutePath != "/")
        {
            return null;
        }

        return endpoint.GetLeftPart(UriPartial.Authority);
    }

    private static RelayScreenQuality NormalizeRelayQuality(RelayScreenQuality value) =>
        value == RelayScreenQuality.MaintainerFull && !BuildFeatures.MaintainerRelayQuality
            ? RelayScreenQuality.Standard
            : value;
}
