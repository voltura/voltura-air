namespace VolturaAir.Host;

internal sealed record RelayIceServer(IReadOnlyList<string> Urls, string Username, string Credential);

internal sealed record RelayUsageSnapshot(
    long Bytes,
    DateTimeOffset CheckedAt,
    long? WarningBytes,
    long? CutoffBytes);

internal sealed record RelayTurnConfiguration(
    IReadOnlyList<RelayIceServer> IceServers,
    IReadOnlyList<string> HostIceServerUris,
    DateTimeOffset ExpiresAt,
    long UsageBytes,
    DateTimeOffset CheckedAt,
    RelayScreenQuality EffectiveQuality)
{
    public int MaximumBitrate => EffectiveQuality switch
    {
        RelayScreenQuality.DataSaver => 2_000_000,
        RelayScreenQuality.MaintainerFull when BuildFeatures.MaintainerRelayQuality => 8_000_000,
        _ => 4_000_000
    };
}
