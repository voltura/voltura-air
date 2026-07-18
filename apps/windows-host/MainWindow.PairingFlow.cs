using System.Globalization;

namespace VolturaAir.Host;

public partial class MainWindow
{
    private void NewPairing()
    {
        _pairingCode = CreatePairingCode();
        if (_activePage == HostPage.Connect && IsVisible)
        {
            SelectPage(HostPage.Connect);
        }
        else if (_activePage == HostPage.Connect)
        {
            _pageNeedsRefresh = true;
        }
    }

    private PairingDisplayCode CreatePairingCode(DateTimeOffset? now = null)
    {
        var pairingCode = _pairingManager.CreatePairingCode(now);
        var url = new UriBuilder(_clientUrl)
        {
            Path = PairingPath,
            Query = $"t={Uri.EscapeDataString(pairingCode.Value)}&v={Uri.EscapeDataString(AppVersion.Display)}"
        };

        if (!string.Equals(_clientUrl, _serverUrl, StringComparison.OrdinalIgnoreCase))
        {
            url.Query = $"{url.Query.TrimStart('?')}&h={Uri.EscapeDataString(CreateHostHint(_clientUrl, _serverUrl))}";
        }

        return new PairingDisplayCode(url.Uri.ToString(), pairingCode.RefreshAt);
    }

    private string GetVisiblePairingUrl()
    {
        return _usePublicScreenshotPairingUrl ? ProductSiteUrl : _pairingCode.Url;
    }

    internal bool RefreshPairingCodeIfDue(DateTimeOffset now)
    {
        if (_pairingCode.RefreshAt > now)
        {
            return false;
        }

        _pairingCode = CreatePairingCode(now);
        return true;
    }

    internal static string CreateHostHint(string clientUrl, string serverUrl)
    {
        if (Uri.TryCreate(clientUrl, UriKind.Absolute, out var clientUri) &&
            Uri.TryCreate(serverUrl, UriKind.Absolute, out var serverUri) &&
            string.Equals(clientUri.Scheme, serverUri.Scheme, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(clientUri.Host, serverUri.Host, StringComparison.OrdinalIgnoreCase))
        {
            return serverUri.Port.ToString(CultureInfo.InvariantCulture);
        }

        return serverUrl;
    }

    private sealed record PairingDisplayCode(string Url, DateTimeOffset RefreshAt);
}
