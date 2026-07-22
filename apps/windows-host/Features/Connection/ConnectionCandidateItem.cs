using System.Net.NetworkInformation;

namespace VolturaAir.Host.Features.Connection;

internal sealed record ConnectionCandidateItem(
    LanAddressCandidate Candidate,
    string Adapter,
    string Address,
    string Status,
    string AccessibleName,
    bool IsSelected)
{
    public static ConnectionCandidateItem[] Create(
        IReadOnlyList<LanAddressCandidate> candidates,
        LanAddressCandidate? selectedCandidate)
    {
        var recommended = candidates.OrderByDescending(candidate => candidate.Score).FirstOrDefault();
        return
        [
            .. candidates.Select(candidate =>
            {
                var status = candidate.IsLikelyVpnOrVirtual
                    ? "Virtual adapter — unlikely to work with devices"
                    : candidate == recommended
                        ? "Recommended"
                        : string.Empty;
                var isSelected = candidate.Address.Equals(selectedCandidate?.Address);
                return new ConnectionCandidateItem(
                    candidate,
                    $"{GetAdapterTypeDisplayName(candidate)} — {GetAdapterDescription(candidate)}",
                    candidate.Address.ToString(),
                    status,
                    string.Join(", ", new[]
                    {
                        $"{GetAdapterTypeDisplayName(candidate)} — {GetAdapterDescription(candidate)}",
                        candidate.Address.ToString(),
                        status
                    }.Where(value => !string.IsNullOrWhiteSpace(value))),
                    isSelected);
            })
        ];
    }

    private static string GetAdapterTypeDisplayName(LanAddressCandidate candidate)
    {
        return candidate.AdapterType switch
        {
            NetworkInterfaceType.Wireless80211 => "Wi-Fi",
            NetworkInterfaceType.Ethernet => "Ethernet",
            _ => candidate.AdapterType.ToString()
        };
    }

    private static string GetAdapterDescription(LanAddressCandidate candidate)
    {
        return string.IsNullOrWhiteSpace(candidate.AdapterDescription)
            ? candidate.AdapterName
            : candidate.AdapterDescription;
    }
}
