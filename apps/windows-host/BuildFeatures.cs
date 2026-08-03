namespace VolturaAir.Host;

internal static class BuildFeatures
{
#if VOLTURA_MAINTAINER_RELAY
    public const bool MaintainerRelayQuality = true;
#else
    public const bool MaintainerRelayQuality = false;
#endif
}
