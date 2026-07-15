using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace VolturaAir.Host;

internal sealed record LanAddressCandidate(
    IPAddress Address,
    string AdapterId,
    string AdapterName,
    string AdapterDescription,
    NetworkInterfaceType AdapterType,
    bool HasIpv4Gateway,
    int Score)
{
    public bool IsLikelyVpnOrVirtual => LanAddressSelector.IsLikelyVpnOrVirtualAdapter($"{AdapterName} {AdapterDescription}");
}

internal sealed record LanAddressSelectionResult(
    IPAddress Address,
    LanAddressCandidate? Candidate,
    bool UsedManualAddress,
    string? Warning);

internal static class LanAddressSelector
{
    private static readonly string[] VpnOrVirtualMarkers =
    [
        "vpn",
        "private internet access",
        "pia",
        "wireguard",
        "openvpn",
        "tailscale",
        "zerotier",
        "nord",
        "proton",
        "mullvad",
        "virtual",
        "hyper-v",
        "vmware",
        "virtualbox",
        "wintun",
        "tap",
        "tunnel"
    ];

    public static IReadOnlyList<LanAddressCandidate> GetCandidates()
    {
        var candidates = new List<LanAddressCandidate>();

        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (adapter.OperationalStatus != OperationalStatus.Up)
            {
                continue;
            }

            if (adapter.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            {
                continue;
            }

            IPInterfaceProperties properties;
            try
            {
                properties = adapter.GetIPProperties();
            }
            catch (NetworkInformationException)
            {
                continue;
            }

            var hasIpv4Gateway = properties.GatewayAddresses.Any(gateway =>
                gateway.Address.AddressFamily == AddressFamily.InterNetwork &&
                !gateway.Address.Equals(IPAddress.Any));

            foreach (var unicast in properties.UnicastAddresses)
            {
                var address = unicast.Address;
                if (address.AddressFamily != AddressFamily.InterNetwork)
                {
                    continue;
                }

                if (IPAddress.IsLoopback(address) || !IsPrivateIpv4Address(address))
                {
                    continue;
                }

                candidates.Add(CreateCandidate(
                    address,
                    adapter.Name,
                    adapter.Description,
                    adapter.NetworkInterfaceType,
                    hasIpv4Gateway,
                    adapter.Id));
            }
        }

        return [.. OrderCandidates(candidates)];
    }

    public static LanAddressCandidate CreateCandidate(
        IPAddress address,
        string adapterName,
        string adapterDescription,
        NetworkInterfaceType adapterType,
        bool hasIpv4Gateway,
        string? adapterId = null)
    {
        return new LanAddressCandidate(
            address,
            string.IsNullOrWhiteSpace(adapterId) ? adapterName : adapterId,
            adapterName,
            adapterDescription,
            adapterType,
            hasIpv4Gateway,
            ScoreLanAddressCandidate(adapterName, adapterDescription, adapterType, hasIpv4Gateway, address));
    }

    public static LanAddressSelectionResult? Select(
        IEnumerable<LanAddressCandidate> candidates,
        NetworkSettingsSnapshot settings)
    {
        var ordered = OrderCandidates(candidates).ToArray();
        if (settings.NetworkMode == NetworkSelectionMode.Manual)
        {
            return SelectManual(ordered, settings);
        }

        if (IPAddress.TryParse(settings.LastAutomaticHostAddress, out var lastAutomaticAddress))
        {
            var lastAutomaticCandidate = ordered.FirstOrDefault(candidate => candidate.Address.Equals(lastAutomaticAddress));
            if (lastAutomaticCandidate is not null)
            {
                return new LanAddressSelectionResult(
                    lastAutomaticCandidate.Address,
                    lastAutomaticCandidate,
                    UsedManualAddress: false,
                    Warning: BuildAdapterWarning(lastAutomaticCandidate, ordered.Length));
            }
        }

        var selected = ordered.FirstOrDefault();
        return selected is null
            ? null
            : new LanAddressSelectionResult(
                selected.Address,
                selected,
                UsedManualAddress: false,
                Warning: BuildAdapterWarning(selected, ordered.Length));
    }

    public static int ScoreLanAddressCandidate(
        string adapterName,
        string adapterDescription,
        NetworkInterfaceType adapterType,
        bool hasIpv4Gateway,
        IPAddress address)
    {
        var text = $"{adapterName} {adapterDescription}".ToLowerInvariant();
        var score = 0;

        if (hasIpv4Gateway)
        {
            score += 1000;
        }

        if (adapterType == NetworkInterfaceType.Wireless80211)
        {
            score += 300;
        }
        else if (adapterType == NetworkInterfaceType.Ethernet)
        {
            score += 250;
        }

        if (IsLikelyVpnOrVirtualAdapter(text))
        {
            score -= 2000;
        }

        var bytes = address.GetAddressBytes();
        if (bytes[0] == 192 && bytes[1] == 168)
        {
            score += 30;
        }
        else if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
        {
            score += 20;
        }
        else if (bytes[0] == 10)
        {
            score += 10;
        }

        return score;
    }

    public static bool IsPrivateIpv4Address(IPAddress address)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        return bytes[0] == 10 ||
               bytes[0] == 192 && bytes[1] == 168 ||
               bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31;
    }

    public static bool IsLikelyVpnOrVirtualAdapter(string adapterText)
    {
        return VpnOrVirtualMarkers.Any(marker => adapterText.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    internal static string GetAdapterDisplayName(LanAddressCandidate candidate)
    {
        var description = string.IsNullOrWhiteSpace(candidate.AdapterDescription)
            ? candidate.AdapterName
            : candidate.AdapterDescription;
        return $"{candidate.AdapterName} ({description})";
    }

    private static LanAddressSelectionResult? SelectManual(
        LanAddressCandidate[] ordered,
        NetworkSettingsSnapshot settings)
    {
        var savedAdapterId = settings.ManualAdapterId?.Trim();
        if (!string.IsNullOrWhiteSpace(savedAdapterId))
        {
            var adapterMatch = ordered.FirstOrDefault(candidate => string.Equals(candidate.AdapterId, savedAdapterId, StringComparison.OrdinalIgnoreCase));
            if (adapterMatch is not null)
            {
                var addressChanged = IPAddress.TryParse(settings.ManualHostAddress, out var manualAddress) && !adapterMatch.Address.Equals(manualAddress);
                var warning = addressChanged
                    ? "Saved network adapter now uses a different IP address. Using the current adapter address."
                    : BuildAdapterWarning(adapterMatch, ordered.Length);

                return new LanAddressSelectionResult(adapterMatch.Address, adapterMatch, UsedManualAddress: true, warning);
            }

            return SelectFallback(
                ordered,
                "Saved network adapter is not currently available. Using recommended network instead.");
        }

        if (IPAddress.TryParse(settings.ManualHostAddress, out var manualHostAddress))
        {
            var manualCandidate = ordered.FirstOrDefault(candidate => candidate.Address.Equals(manualHostAddress));
            if (manualCandidate is not null)
            {
                return new LanAddressSelectionResult(
                    manualCandidate.Address,
                    manualCandidate,
                    UsedManualAddress: true,
                    Warning: BuildAdapterWarning(manualCandidate, ordered.Length));
            }

            return SelectFallback(
                ordered,
                "Saved network address is not currently available. Using recommended network instead.");
        }

        return SelectFallback(
            ordered,
            "Saved network address is not valid. Using recommended network instead.");
    }

    private static LanAddressSelectionResult? SelectFallback(LanAddressCandidate[] ordered, string warning)
    {
        var fallback = ordered.Length > 0 ? ordered[0] : null;
        return fallback is null
            ? null
            : new LanAddressSelectionResult(fallback.Address, fallback, UsedManualAddress: false, warning);
    }

    private static string? BuildAdapterWarning(LanAddressCandidate candidate, int candidateCount)
    {
        if (candidate.IsLikelyVpnOrVirtual)
        {
            return "Selected network looks like VPN or virtual networking and may not be reachable from your phone or tablet.";
        }

        return candidateCount > 1
            ? $"Multiple network adapters found. Voltura Air selected {GetAdapterDisplayName(candidate)}. If your phone cannot connect, choose the adapter connected to the same Wi-Fi/LAN."
            : null;
    }

    private static IOrderedEnumerable<LanAddressCandidate> OrderCandidates(IEnumerable<LanAddressCandidate> candidates)
    {
        return candidates
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.AdapterName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.Address.ToString(), StringComparer.Ordinal);
    }
}
