using System.Globalization;
using VolturaAir.Host.Ui;

namespace VolturaAir.Host.Features.Connect;

internal sealed class ConnectPageController(
    PairingManager pairingManager,
    WebHostService webHost,
    string? clientUrl,
    bool usePublicScreenshotPairingUrl,
    HostClipboardFeedback clipboard,
    Action requestViewRefresh,
    Action openConnectionPage)
{
    private readonly PairingLinkController _pairingLinks = new(pairingManager, webHost.ServerUrl, clientUrl);

    public string PairingUrl => _pairingLinks.Url;

    public string ServerUrl => _pairingLinks.ServerUrl;

    public ConnectPageView CreateView()
    {
        _pairingLinks.RefreshIfDue(DateTimeOffset.UtcNow);
        var pairingLink = GetVisiblePairingUrl();
        var hasMultipleAdapters = LanAddressSelector.GetCandidates()
            .Select(candidate => candidate.AdapterId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Skip(1)
            .Any();
        return new ConnectPageView(
            PairingQrCodeRenderer.Create(pairingLink),
            pairingLink,
            webHost.ServerUrl,
            webHost.SelectedAdapterName,
            hasMultipleAdapters,
            webHost.AdvertisedHostAddress,
            webHost.Port.ToString(CultureInfo.InvariantCulture),
            webHost.AddressSelectionWarning,
            webHost.SelectedAdapterName,
            webHost.PortSelectionWarning,
            _pairingLinks.RefreshAt,
            CreateNewCode,
            () => clipboard.Copy(GetVisiblePairingUrl(), "Link copied"),
            openConnectionPage);
    }

    public void CreateNewCode()
    {
        _pairingLinks.CreateNew();
        requestViewRefresh();
    }

    public void UpdateServerUrl(string serverUrl)
    {
        if (_pairingLinks.UpdateServerUrl(serverUrl))
        {
            requestViewRefresh();
        }
    }

    private string GetVisiblePairingUrl()
    {
        return usePublicScreenshotPairingUrl ? ProductWebsite.Url : _pairingLinks.Url;
    }

    private string GetConnectionStatus()
    {
        if (!pairingManager.IsPaired)
        {
            return "Waiting for a phone or tablet on the same network";
        }

        return pairingManager.HasActiveController
            ? $"Connected to {pairingManager.ActiveDeviceSummary}"
            : $"{pairingManager.PairedDeviceCount} paired device{Plural(pairingManager.PairedDeviceCount)}. Ready for another.";
    }

    private static string Plural(int count) => count == 1 ? string.Empty : "s";
}
