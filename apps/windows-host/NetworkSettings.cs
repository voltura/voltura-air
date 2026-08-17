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
    RelayScreenQuality RelayScreenQuality = RelayScreenQuality.Standard,
    bool EnhancedCapabilitiesEnabled = false);

internal static class AppNetworkSettings
{
    internal const string ValueName = "NetworkSettingsJson";
    internal static NetworkSettingsSnapshot Default { get; } = new(
        NetworkSelectionMode.Automatic,
        null,
        null,
        null,
        PortSelectionMode.Automatic,
        null,
        null,
        null,
        ConnectionTransportMode.DirectLan,
        null,
        RelayScreenQuality.Standard,
        false);

    public static NetworkSettingsSnapshot Load()
    {
        return HostSettingsJsonValue.Load(ValueName, Default, Default, IsValid);
    }

    public static void Save(NetworkSettingsSnapshot settings)
    {
        if (!IsValid(settings)) throw new ArgumentException("The network settings are invalid.", nameof(settings));
        HostSettingsJsonValue.Save(ValueName, settings);
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

    private static bool IsValid(NetworkSettingsSnapshot settings) =>
        Enum.IsDefined(settings.NetworkMode) &&
        Enum.IsDefined(settings.PortMode) &&
        Enum.IsDefined(settings.TransportMode) &&
        Enum.IsDefined(settings.RelayScreenQuality) &&
        settings.RelayScreenQuality == NormalizeRelayQuality(settings.RelayScreenQuality) &&
        IsOptionalBounded(settings.ManualHostAddress, ProtocolStringLimits.IpAddress) &&
        IsOptionalBounded(settings.ManualAdapterId, ProtocolStringLimits.AdapterName) &&
        IsOptionalBounded(settings.ManualAdapterName, ProtocolStringLimits.AdapterName) &&
        IsOptionalBounded(settings.LastAutomaticHostAddress, ProtocolStringLimits.IpAddress) &&
        (settings.ManualPort is null || PortSelector.IsValidPort(settings.ManualPort.Value)) &&
        (settings.LastAutomaticPort is null || PortSelector.IsValidPort(settings.LastAutomaticPort.Value)) &&
        (settings.CustomRelayEndpoint is null || string.Equals(
            settings.CustomRelayEndpoint,
            NormalizeRelayEndpoint(settings.CustomRelayEndpoint),
            StringComparison.Ordinal));

    private static bool IsOptionalBounded(string? value, int maximumLength) =>
        value is null || value.Length is > 0 && value.Length <= maximumLength;

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
