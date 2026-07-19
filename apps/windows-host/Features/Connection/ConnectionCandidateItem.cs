using System.Net.NetworkInformation;

namespace VolturaAir.Host.Features.Connection;

internal sealed record ConnectionCandidateItem(
    LanAddressCandidate Candidate,
    string Adapter,
    string Address,
    string Status,
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
                var status = candidate == recommended
                    ? "Recommended"
                    : candidate.IsLikelyVpnOrVirtual
                        ? "Not recommended"
                        : string.Empty;
                var isSelected = candidate.Address.Equals(selectedCandidate?.Address);
                return new ConnectionCandidateItem(
                    candidate,
                    $"{GetAdapterTypeDisplayName(candidate)} - {GetAdapterDescription(candidate)}",
                    candidate.Address.ToString(),
                    string.IsNullOrWhiteSpace(status) ? "Available" : status,
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
